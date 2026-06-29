using UnityEngine;

// A *stateful* vector field: instead of recomputing its output from parameters each frame (like noise/stamp/polygon),
// it integrates an incompressible 2D fluid forward in time. The field at frame N is derived from the field at frame
// N-1 — that statefulness is what makes wind swirl around obstacles, form wakes, and conserve mass rather than just
// scrolling a noise function.
//
// It is the one component that breaks the "render only when dirty" model: while playing it marks itself dirty every
// frame and advances the solver on a FIXED timestep (sims go unstable on variable dt). The solver runs on its own
// raw-float velocity ping-pong textures and only encodes into the base renderTexture at the end, so the rest of the
// toolset (particle force field, group blend, GPU/CPU sampling, visualizer) consumes it exactly like any other field.
//
// Inputs that compose with the existing toolset:
//   forceField  — another VectorFieldComponent whose output is injected as a continuous force each step. Point a
//                 NoiseVectorFieldComponent at it for gusty wind, or a StampVectorFieldComponent for a fan/emitter.
//   obstacles   — a mask texture (e.g. rasterized from a PolygonVectorField) the fluid flows around.
[ExecuteAlways]
public class SimulatedVectorFieldComponent : VectorFieldComponent {

	static ComputeShader fluidComputeShader;
	static ComputeShader FluidComputeShader => fluidComputeShader ? fluidComputeShader : (fluidComputeShader = Resources.Load<ComputeShader>("FluidSimulation"));

	[Header("Simulation")]
	[Tooltip("Fixed solver rate. The sim steps in increments of 1/this regardless of frame rate.")]
	public float simulationFps = 60f;
	[Tooltip("Cap on solver steps per frame, so a hitch can't spiral into a death-loop of catch-up steps.")]
	public int maxSubstepsPerFrame = 4;
	[Tooltip("Simulated seconds per real second — how fast the fluid evolves, independent of step rate. " +
		"simulationFps controls smoothness; this controls speed. At fps=60 each step covers a sub-texel distance " +
		"and advection stalls, so raise this (e.g. 10-30) to get visible flow while keeping a high, smooth step rate.")]
	public float timeScale = 1f;
	[Tooltip("Jacobi iterations for the pressure solve. More = more accurately incompressible, but costlier. 20-40 is typical.")]
	public int pressureIterations = 30;
	[Range(0f, 1f), Tooltip("Per-step velocity damping. 1 = inviscid (energy persists), lower fakes viscosity / drag.")]
	public float viscosityDamp = 0.999f;
	[Tooltip("Run the sim in edit mode too. Off by default — sims are usually only meaningful while playing.")]
	public bool simulateInEditMode = false;

	public enum AdvectionMode {
		// Plain semi-Lagrangian: stable and cheap, but heavily diffusive — flow smooths to mush and decays fast.
		SemiLagrangian,
		// MacCormack: a forward + reverse pass that cancels most of that diffusion. ~2x advection cost; vortices and
		// detail persist far longer. The recommended default.
		MacCormack,
	}
	[Header("Advection")]
	[Tooltip("MacCormack cancels most of the numerical diffusion that makes plain semi-Lagrangian flow decay to mush.")]
	public AdvectionMode advectionMode = AdvectionMode.MacCormack;

	[Header("Vorticity confinement")]
	[Tooltip("Re-injects the small-scale swirl that diffusion eats, keeping the flow lively. 0 disables it; " +
		"0.1-0.5 is a useful range. Too high looks turbulent/noisy.")]
	public float vorticityStrength = 0.2f;

	// Ordered by how much spatial information they use, least to most. Integer values must stay in sync with the
	// FORCE_* defines in FluidSimulation.compute.
	public enum ForceMapping {
		// Stretches the whole force field to fill the sim, regardless of resolution. Ignores position/rotation/scale —
		// it just maps the field's full extent onto the sim's full extent. Vectors are applied as-is (not rotated).
		Stretched,
		// 1:1 grid-cell copy: sim cell (x,y) reads force texel (x,y). Ignores the force field's transform and assumes
		// the two grids share resolution and alignment. Cheapest.
		DirectTexel,
		// Samples the force field by world position and rotates its vectors into the sim's frame. Moving, rotating, or
		// resizing the force field now affects the sim, and differing resolutions/placements just work.
		WorldSpace,
	}
	[Header("Forcing")]
	public VectorFieldComponent forceField;
	[Tooltip("DirectTexel: 1:1 cell copy (grids must match), transform ignored. WorldSpace: transform-aware — " +
		"move/rotate/resize the force field and it pushes the fluid accordingly. Stretched: fill the sim with the " +
		"whole force field at any resolution, ignoring transform.")]
	public ForceMapping forceMapping = ForceMapping.Stretched;
	public float forceStrength = 1f;

	public enum BoundaryMode {
		// Periodic: flow leaving one edge re-enters the opposite edge. Best for seamless/tiling wind & sea maps.
		Wrap,
		// Solid no-slip border: the fluid is contained in a box and deflects off the edges.
		Wall,
		// Outflow / absorbing: fluid flows out of the edges without reflecting back.
		Open,
	}
	[Header("Boundaries")]
	[Tooltip("What happens at the domain edges. Interior obstacle masks apply regardless of this.")]
	public BoundaryMode boundaryMode = BoundaryMode.Wrap;
	[Tooltip("Optional mask the fluid flows around (>0.5 = solid). Independent of the edge mode above.")]
	public Texture2D obstacles;

	[Header("Output")]
	[Tooltip("Scales raw solver velocity into the encoded [-1,1] field range before it enters the pipeline.")]
	public float outputScale = 1f;

	// Raw-float solver state (NOT encoded). Velocity is ping-ponged; velC is the MacCormack scratch buffer;
	// pressure/divergence/curl are scratch.
	RenderTexture velA, velB, velC;
	RenderTexture pressureA, pressureB;
	RenderTexture divergence;
	RenderTexture curl;
	Point allocatedSize = new Point(-1, -1);
	bool seeded;

	// Leftover sub-frame time carried between frames so the fixed-step accumulator stays exact.
	float accumulator;

	int kAddForces, kAdvect, kAdvectMacCormack, kComputeVorticity, kVorticityConfinement, kDivergence, kPressure, kProject, kEncode;
	bool kernelsResolved;

	const int ThreadsX = 8, ThreadsY = 8;

	bool ShouldSimulate => Application.isPlaying || simulateInEditMode;

	// The tick wrinkle: drive the fixed-step accumulator and force a re-render every frame while simulating, so the
	// base pump runs RenderInternal (which steps the solver). When not simulating we fall back to normal dirty-only
	// behaviour and just keep showing the last encoded state.
	public override void Update() {
		if (ShouldSimulate && isActiveAndEnabled) {
			accumulator += Application.isPlaying ? Time.deltaTime : (1f / Mathf.Max(1f, simulationFps));
			SetDirty();
		}
		base.Update();
	}

	protected override void RenderInternal() {
		EnsureHasValidRenderTexture();
		EnsureSimTextures();
		ResolveKernels();

		if (!seeded) { ClearSimState(); seeded = true; }

		if (ShouldSimulate) {
			// fixedDt is the real-time cadence we consume the accumulator at (smoothness); the solver advances by
			// fixedDt * timeScale of *simulated* time per step (speed). Decoupling them lets the flow move fast
			// while still stepping often enough that advection doesn't stall on sub-texel back-traces.
			float fixedDt = 1f / Mathf.Max(1f, simulationFps);
			float simDt = fixedDt * timeScale;
			int steps = 0;
			while (accumulator >= fixedDt && steps < maxSubstepsPerFrame) {
				Step(simDt);
				accumulator -= fixedDt;
				steps++;
			}
			// If we hit the substep cap we're behind real time; drop the backlog rather than accumulate lag.
			if (steps == maxSubstepsPerFrame) accumulator = 0f;
		}

		Encode(); // raw velocity (velA) -> encoded base renderTexture for the rest of the pipeline
	}

	// One fixed-dt solver step: forces -> advect -> project (divergence, pressure solve, gradient subtract).
	void Step(float dt) {
		var cs = FluidComputeShader;
		cs.SetInt("width", gridRenderer.gridSize.x);
		cs.SetInt("height", gridRenderer.gridSize.y);
		cs.SetFloat("dt", dt);
		cs.SetFloat("viscosityDamp", viscosityDamp);
		cs.SetInt("boundaryMode", (int)boundaryMode);

		// The advection/MacCormack samplers honour the texture wrap mode, so it must match the boundary mode:
		// Repeat for periodic edges, Clamp otherwise.
		var wrap = boundaryMode == BoundaryMode.Wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
		velA.wrapMode = velB.wrapMode = velC.wrapMode = wrap;

		bool hasObstacles = obstacles != null;
		cs.SetInt("hasObstacles", hasObstacles ? 1 : 0);

		// 1) Inject forces (+ apply viscosity damp). velA -> velB.
		// Ignore a force field pointed back at ourselves — it would feed the solver its own encoded output as a force.
		bool hasForce = forceField != null && forceField != this && forceField.renderTexture != null;
		cs.SetInt("hasForce", hasForce ? 1 : 0);
		cs.SetFloat("forceStrength", forceStrength);
		cs.SetInt("forceMapping", (int)forceMapping);
		cs.SetTexture(kAddForces, "ForceField", hasForce ? forceField.renderTexture : (Texture)Texture2D.blackTexture);

		// WorldSpace mapping needs the grid<->world transforms of both fields and the relative rotation that brings the
		// force field's local vectors into ours. (Only consulted when hasForce && forceMapping == WorldSpace.)
		if (hasForce && forceMapping == ForceMapping.WorldSpace) {
			cs.SetMatrix("simGridToWorld", gridRenderer.cellCenter.gridToWorldMatrix);
			cs.SetMatrix("forceWorldToGrid", forceField.gridRenderer.cellCenter.gridToWorldMatrix.inverse);
			cs.SetVector("forceGridSize", new Vector4(forceField.gridRenderer.gridSize.x, forceField.gridRenderer.gridSize.y, 0, 0));
			var relativeRotation = Quaternion.Inverse(transform.rotation) * forceField.transform.rotation;
			cs.SetMatrix("forceToSimDir", Matrix4x4.Rotate(relativeRotation));
		}
		BindObstacles(kAddForces, hasObstacles);
		cs.SetTexture(kAddForces, "VelocityIn", velA);
		cs.SetTexture(kAddForces, "VelocityOut", velB);
		Dispatch(kAddForces);
		Swap(ref velA, ref velB);

		// 2) Advect velocity through itself.
		// Forward semi-Lagrangian: velA -> velB.
		BindObstacles(kAdvect, hasObstacles);
		cs.SetTexture(kAdvect, "VelocityIn", velA);
		cs.SetTexture(kAdvect, "VelocityOut", velB);
		Dispatch(kAdvect);

		if (advectionMode == AdvectionMode.MacCormack) {
			// Correction pass: original velA (φ) + forward velB (φ̂) -> corrected velC. Then velC becomes current.
			BindObstacles(kAdvectMacCormack, hasObstacles);
			cs.SetTexture(kAdvectMacCormack, "VelocityIn", velA);
			cs.SetTexture(kAdvectMacCormack, "ForwardVelocity", velB);
			cs.SetTexture(kAdvectMacCormack, "VelocityOut", velC);
			Dispatch(kAdvectMacCormack);
			Swap(ref velA, ref velC);
		} else {
			// Plain semi-Lagrangian: the forward result is the answer.
			Swap(ref velA, ref velB);
		}

		// 2b) Vorticity confinement: re-inject the swirl diffusion eats. velA -> curl, then (velA, curl) -> velB.
		if (vorticityStrength > 0f) {
			cs.SetFloat("vorticityStrength", vorticityStrength);
			BindObstacles(kComputeVorticity, hasObstacles);
			cs.SetTexture(kComputeVorticity, "VelocityIn", velA);
			cs.SetTexture(kComputeVorticity, "CurlOut", curl);
			Dispatch(kComputeVorticity);

			BindObstacles(kVorticityConfinement, hasObstacles);
			cs.SetTexture(kVorticityConfinement, "VelocityIn", velA);
			cs.SetTexture(kVorticityConfinement, "CurlIn", curl);
			cs.SetTexture(kVorticityConfinement, "VelocityOut", velB);
			Dispatch(kVorticityConfinement);
			Swap(ref velA, ref velB);
		}

		// 3a) Divergence of velA -> divergence.
		BindObstacles(kDivergence, hasObstacles);
		cs.SetTexture(kDivergence, "VelocityIn", velA);
		cs.SetTexture(kDivergence, "DivergenceOut", divergence);
		Dispatch(kDivergence);

		// 3b) Solve ∇²p = div with Jacobi iterations, ping-ponging pressureA/pressureB. Start from zero.
		ClearTexture(pressureA);
		BindObstacles(kPressure, hasObstacles);
		cs.SetTexture(kPressure, "DivergenceIn", divergence);
		for (int i = 0; i < pressureIterations; i++) {
			cs.SetTexture(kPressure, "PressureIn", pressureA);
			cs.SetTexture(kPressure, "PressureOut", pressureB);
			Dispatch(kPressure);
			Swap(ref pressureA, ref pressureB);
		}

		// 3c) Subtract pressure gradient: velA - ∇pressureA -> velB.
		BindObstacles(kProject, hasObstacles);
		cs.SetTexture(kProject, "VelocityIn", velA);
		cs.SetTexture(kProject, "PressureIn", pressureA);
		cs.SetTexture(kProject, "VelocityOut", velB);
		Dispatch(kProject);
		Swap(ref velA, ref velB);
	}

	void Encode() {
		var cs = FluidComputeShader;
		cs.SetInt("width", gridRenderer.gridSize.x);
		cs.SetInt("height", gridRenderer.gridSize.y);
		cs.SetInt("boundaryMode", (int)boundaryMode);
		cs.SetFloat("outputScale", outputScale);
		cs.SetTexture(kEncode, "VelocityIn", velA);
		cs.SetTexture(kEncode, "Result", renderTexture);
		// Encode calls InBounds, which the compiler ties to the same boundary block as Obstacles — so the kernel's
		// resource table includes Obstacles and Unity demands it be bound even though Encode never samples it.
		BindObstacles(kEncode, obstacles != null);
		Dispatch(kEncode);
	}

	// Every kernel declares Obstacles, so it must be bound on each even when unused — bind a black (all-zero, i.e.
	// "no solid") fallback when there's no mask. Unity errors on any declared-but-unbound texture property.
	void BindObstacles(int kernel, bool hasObstacles) {
		FluidComputeShader.SetTexture(kernel, "Obstacles", hasObstacles ? (Texture)obstacles : Texture2D.blackTexture);
	}

	void Dispatch(int kernel) {
		int gx = Mathf.CeilToInt((float)gridRenderer.gridSize.x / ThreadsX);
		int gy = Mathf.CeilToInt((float)gridRenderer.gridSize.y / ThreadsY);
		FluidComputeShader.Dispatch(kernel, gx, gy, 1);
	}

	static void Swap(ref RenderTexture a, ref RenderTexture b) { (a, b) = (b, a); }

	// --- solver texture lifecycle ---------------------------------------------------------------------------------
	void EnsureSimTextures() {
		var size = gridRenderer.gridSize;
		if (allocatedSize == size && velA != null) return;

		ReleaseSimTextures();
		velA       = NewSimTexture(size, RenderTextureFormat.RGFloat, bilinear: true);
		velB       = NewSimTexture(size, RenderTextureFormat.RGFloat, bilinear: true);
		velC       = NewSimTexture(size, RenderTextureFormat.RGFloat, bilinear: true);
		pressureA  = NewSimTexture(size, RenderTextureFormat.RFloat,  bilinear: false);
		pressureB  = NewSimTexture(size, RenderTextureFormat.RFloat,  bilinear: false);
		divergence = NewSimTexture(size, RenderTextureFormat.RFloat,  bilinear: false);
		curl       = NewSimTexture(size, RenderTextureFormat.RFloat,  bilinear: false);
		allocatedSize = size;
		seeded = false;   // re-seed (clear) on resize
	}

	static RenderTexture NewSimTexture(Point size, RenderTextureFormat format, bool bilinear) {
		var rt = new RenderTexture(size.x, size.y, 0, format, RenderTextureReadWrite.Linear) {
			enableRandomWrite = true,
			filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point,
			wrapMode = TextureWrapMode.Clamp,
		};
		rt.Create();
		return rt;
	}

	void ClearSimState() {
		ClearTexture(velA); ClearTexture(velB); ClearTexture(velC);
		ClearTexture(pressureA); ClearTexture(pressureB);
		ClearTexture(divergence); ClearTexture(curl);
		accumulator = 0f;
	}

	static void ClearTexture(RenderTexture rt) {
		if (rt == null) return;
		var prev = RenderTexture.active;
		RenderTexture.active = rt;
		GL.Clear(false, true, Color.clear);
		RenderTexture.active = prev;
	}

	void ReleaseSimTextures() {
		foreach (var rt in new[] { velA, velB, velC, pressureA, pressureB, divergence, curl }) {
			if (rt == null) continue;
			if (RenderTexture.active == rt) RenderTexture.active = null;
			rt.Release();
		}
		velA = velB = velC = pressureA = pressureB = divergence = curl = null;
		allocatedSize = new Point(-1, -1);
	}

	void ResolveKernels() {
		if (kernelsResolved) return;
		var cs = FluidComputeShader;
		kAddForces            = cs.FindKernel("AddForces");
		kAdvect               = cs.FindKernel("Advect");
		kAdvectMacCormack     = cs.FindKernel("AdvectMacCormack");
		kComputeVorticity     = cs.FindKernel("ComputeVorticity");
		kVorticityConfinement = cs.FindKernel("VorticityConfinement");
		kDivergence           = cs.FindKernel("Divergence");
		kPressure             = cs.FindKernel("PressureJacobi");
		kProject              = cs.FindKernel("Project");
		kEncode               = cs.FindKernel("Encode");
		kernelsResolved = true;
	}

	protected override void OnDisable() {
		base.OnDisable();
		ReleaseSimTextures();
		seeded = false;
	}

	// The forceField participates in our output, so we must re-render when it changes. The base already re-renders
	// every frame while simulating, so this mainly matters for the edit-mode-paused case.
	VectorFieldComponent lastForceField;
	float lastForceStrength = float.NaN, lastOutputScale = float.NaN, lastViscosityDamp = float.NaN;
	protected override bool ParametersChanged() {
		bool changed = base.ParametersChanged();
		if (lastForceField != forceField)       { lastForceField = forceField;       changed = true; }
		if (lastForceStrength != forceStrength)  { lastForceStrength = forceStrength;  changed = true; }
		if (lastOutputScale != outputScale)      { lastOutputScale = outputScale;      changed = true; }
		if (lastViscosityDamp != viscosityDamp)  { lastViscosityDamp = viscosityDamp;  changed = true; }
		return changed;
	}
}
