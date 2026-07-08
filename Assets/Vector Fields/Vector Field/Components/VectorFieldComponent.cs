using System;
using System.Collections.Generic;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

// One field per GameObject: the field's placement, orientation, and grid footprint are all derived from this
// transform, so a second component would share them and gain nothing a GroupVectorFieldComponent (composition via
// child transforms) doesn't already provide — while breaking the singular GetComponent<VectorFieldComponent> lookups
// that consumers rely on. Compose co-located fields with a group, not by stacking components.
[ExecuteAlways]
[DisallowMultipleComponent]
public abstract class VectorFieldComponent : MonoBehaviour {
	protected GroupVectorFieldComponent group {
		get {
			if (this == null) {
				Debug.LogError("VectorFieldComponent is null");
				return null;
			}
			// Nearest ancestor group (equivalent to a nearest-matching-ancestor search, which
			// returns the closest matching ancestor and includes inactive ones), but as a plain parent walk so it
			// doesn't allocate a List + LINQ enumerator on every access.
			for (var t = transform.parent; t != null; t = t.parent)
				if (t.TryGetComponent(out GroupVectorFieldComponent ancestorGroup))
					return ancestorGroup;
			return null;
		}
	}

	// The spatial grid (world<->cell mapping + size), owned by the component and serialized here. Bound to this
	// transform on init. Public so the painting API / drawing tool can read its world<->grid conversions and floor
	// plane directly (satisfies IPaintTarget.grid on the drawable subclass).
	[field: SerializeField] public GridTransform grid { get; private set; } = new GridTransform();
	public Vector3 planeNormal => transform.forward;

	// Grid convenience surface. Subclasses (and consumers) go through these instead of reaching into `grid` directly.
	public Vector2Int GridSize => grid.Size;
	public Matrix4x4 GridToWorldMatrix => grid.GridToWorldMatrix;
	public Matrix4x4 GridToLocalMatrix => grid.GridToLocalMatrix;
	public Vector2 WorldToGridPosition(Vector3 worldPosition) => grid.WorldToGridPosition(worldPosition);

	[Space]
	[NonSerialized] public RenderTexture renderTexture;

	// The CPU copy of the field, for consumers that read it on the CPU (the particle force field, the debug
	// renderer, EvaluateVector). Transient: GPU components fill it via readback when a consumer wants it, CPU
	// components (polygon / group-CPU) compute into it directly. Not serialized — it's rebuilt every render, and
	// authored data (Drawable) lives in its own paintField.
	[NonSerialized] public VectorFieldMap vectorField;

	// A uniform scalar on the field's OUTPUT. Together with `cookie` it forms the field's output transform: applied to
	// the rendered result in Render() (see ApplyOutputTransform), NOT baked into the component's internal/authored
	// state. So the simulator's solver and a drawable's paint field never see it, but every consumer does — the GPU
	// render texture, the group blend, the visualizer, and the read-back CPU vectorField are all pre-scaled, so
	// EvaluateVector / TrySample* must NOT re-multiply by it.
	public float magnitude = 1;

	// Optional mask on this field's output: multiplies the field's strength by the cookie (radial falloff, curve, or
	// texture). Defaults to None (no masking). Part of the same output transform as `magnitude` (applied by Render(),
	// after RenderInternal, over the extent just written) — including on groups, where it masks the combined result.
	public VectorFieldCookieSource cookie = new VectorFieldCookieSource();

	// Fired synchronously when the field has been rendered (the GPU renderTexture is current). For consumers that
	// read the texture directly.
	public event Action OnRendered;
	// Fired when the CPU vectorField has been populated and is fresh. For consumers that read vectorField (the
	// particle force field, CPU sampling). In CPU mode this fires synchronously with the render; in GPU mode it
	// fires after the readback (synchronously if any consumer requested immediate, otherwise from the async callback).
	public event Action OnCpuDataReady;

	// Cached world matrix from the last tick, to detect when this object has moved/rotated/scaled (so the field
	// re-renders). Captures parent motion too, since it's the full localToWorldMatrix.
	Matrix4x4 lastLocalToWorld;
	bool haveLastLocalToWorld;

	// Consumers register that they need the CPU vectorField; the readback only runs while at least one is registered,
	// and runs synchronously (same frame) if any of them needs the data immediately. One readback per render serves
	// them all via OnCpuDataReady. Registrants must unregister (typically in OnDisable) — destroyed ones are pruned
	// defensively, but leaking keeps the CPU copy alive needlessly.
	HashSet<Component> _cpuConsumers;
	HashSet<Component> _immediateCpuConsumers;
	HashSet<Component> cpuConsumers => _cpuConsumers ??= new HashSet<Component>();
	HashSet<Component> immediateCpuConsumers => _immediateCpuConsumers ??= new HashSet<Component>();

	public void RegisterCpuConsumer(Component consumer, bool immediate) {
		if (consumer == null) return;
		cpuConsumers.Add(consumer);
		if (immediate) immediateCpuConsumers.Add(consumer);
		else immediateCpuConsumers.Remove(consumer);
		SetDirty(); // make sure the CPU copy gets produced
	}

	public void UnregisterCpuConsumer(Component consumer) {
		if (consumer == null) return;
		cpuConsumers.Remove(consumer);
		immediateCpuConsumers.Remove(consumer);
	}

	bool WantsCpuData { get { cpuConsumers.RemoveWhere(c => c == null); return cpuConsumers.Count > 0; } }
	bool WantsImmediateCpuData { get { immediateCpuConsumers.RemoveWhere(c => c == null); return immediateCpuConsumers.Count > 0; } }

	// Read-only state for tooling/diagnostics.
	public IReadOnlyCollection<Component> CpuConsumers { get { cpuConsumers.RemoveWhere(c => c == null); return cpuConsumers; } }
	public bool IsImmediateCpuConsumer(Component consumer) => consumer != null && immediateCpuConsumers.Contains(consumer);
	public bool IsDirty => isDirty;
	public bool IsReadbackPending => pendingReadback.HasValue && !pendingReadback.Value.done;

	// Set by SetDirty on any change that affects this field's output, consumed by EnsureUpToDate which renders at
	// most once per dirty episode. Starts true so the first frame renders.
	[NonSerialized] bool isDirty = true;
	GroupVectorFieldComponent lastGroup;

	// This is called when the application starts, when a scene loads, when a component is created (in editor or runtime)
	protected virtual void Awake() {
		renderTexture = null;
	}

	// This is called after Awake and on recompile
	protected virtual void OnEnable() {
		EnsureInitialized();
		SetDirty();
		EnsureUpToDate();
	}

	protected virtual void EnsureInitialized() {
		grid.Bind(transform);
#if UNITY_EDITOR
		// In edit mode Unity only calls Update on a repaint, so drive the dirty pump off the editor's own tick as
		// well to process changes promptly while idle. Tick is a cheap no-op whenever nothing is dirty.
		EditorApplication.update -= Tick;
		EditorApplication.update += Tick;
#endif
	}

	protected virtual void OnDisable() {
		// We're leaving the blend; let our group re-render without us.
		if (group != null) group.SetDirty();

		// Release the GPU texture: render textures aren't garbage collected, so leaving it alive across a
		// disable/enable would leak it. It's rebuilt from the component's source on the next render.
		if (renderTexture != null) DestroyRenderTexture();
		cookie?.Dispose();
#if UNITY_EDITOR
		EditorApplication.update -= Tick;
#endif
	}

	protected virtual void OnDestroy() {
		DestroyRenderTexture();
		cookie?.Dispose();
		regionUploader.Dispose();
	}

#if UNITY_EDITOR
	protected virtual void OnValidate() {
		if (!isActiveAndEnabled) return;
		// Grid size defaults to 64×64 and self-clamps to ≥1 on each axis (see GridTransform.Size), so no explicit
		// defaulting/guarding is needed here.
		grid.Bind(transform);
		lastLocalToWorld = transform.localToWorldMatrix;
		haveLastLocalToWorld = true;
		SetDirty();
	}
#endif


	// Unity's per-frame callback (play mode, and edit mode on repaint). Routes through the same pump as the editor tick.
	public virtual void Update() => Tick();

	// Single pump: detect changes, then render if dirty. Idempotent — a no-op when nothing changed and clean.
	void Tick() {
		if (this == null || !isActiveAndEnabled) return;
		if (TransformChanged() || ParametersChanged()) SetDirty();
		EnsureUpToDate();
	}

	bool TransformChanged() {
		var current = transform.localToWorldMatrix;
		if (!haveLastLocalToWorld || lastLocalToWorld != current) {
			lastLocalToWorld = current;
			haveLastLocalToWorld = true;
			return true;
		}
		return false;
	}

	// Contributes this field's output-affecting parameters to the per-tick change hash. Each override calls base then
	// adds its own fields; the base owns the compare-and-cache (ParametersChanged below), so subclasses never keep
	// their own lastX mirror — adding a watched field is one hash.Add line, and forgetting to cache one can't happen.
	protected virtual void CollectParameters(ref HashCode hash) {
		hash.Add(magnitude);
		var gridSize = GridSize;
		hash.Add(gridSize.x);
		hash.Add(gridSize.y);
		// Content hash catches any cookie field change (mode/softness/curve/texture) without enumerating them, and
		// without allocating a JSON string every tick.
		hash.Add(cookie != null ? cookie.GetContentHash() : 0);
	}

	// Hashes the current parameters (via CollectParameters) and compares to the previous tick. Returns true on the
	// first call and on any change. Non-virtual — subclasses customize the *inputs* through CollectParameters, not
	// this compare. A hash collision would skip one re-render; accepted (and already relied on for cookie/brush/layer
	// hashing), negligible over a HashCode of the actual values.
	bool haveParametersHash;
	int lastParametersHash;
	protected bool ParametersChanged() {
		var hash = new HashCode();
		CollectParameters(ref hash);
		int current = hash.ToHashCode();
		if (haveParametersHash && current == lastParametersHash) return false;
		haveParametersHash = true;
		lastParametersHash = current;
		return true;
	}

	// True on the first render and whenever the output transform (magnitude or cookie) changed since the previous
	// render. A producer that writes the render texture in sub-rects (the drawable's region upload) uses this to force
	// a whole-grid re-write when the transform changed, since a region write would leave the rest of the texture baked
	// with the old transform. Advances its own cached hash, so call it exactly once per render.
	bool haveOutputTransformHash;
	int lastOutputTransformHash;
	protected bool OutputTransformChangedSinceLastRender() {
		var hash = new HashCode();
		hash.Add(magnitude);
		hash.Add(cookie != null ? cookie.GetContentHash() : 0);
		int current = hash.ToHashCode();
		bool changed = !haveOutputTransformHash || current != lastOutputTransformHash;
		haveOutputTransformHash = true;
		lastOutputTransformHash = current;
		return changed;
	}

	// Marks this field (and its parent group, recursively up the chain) as needing a re-render. Does NOT render
	// immediately — rendering is deferred to the next EnsureUpToDate, so repeated calls in a frame coalesce.
	public virtual void SetDirty() {
		isDirty = true;
		lastGroup = group;
		if (lastGroup != null) lastGroup.SetDirty();
	}

	void OnTransformParentChanged() {
		// Re-blend the group we left as well as the one we joined.
		var previous = lastGroup;
		var current = group;
		if (previous != null && previous != current) previous.SetDirty();
		SetDirty();
	}

	// The single place RenderInternal is driven from: renders only if dirty. Consumers can call this before
	// reading to guarantee fresh data (pull), and groups call it on each child before blending.
	public void EnsureUpToDate() {
		if (!isActiveAndEnabled || !isDirty) return;
		// A group pulls its children up to date during its own OnEnable, which can run before a child's OnEnable has
		// initialized it — so make sure the grid is bound to this transform before rendering.
		grid.Bind(transform);
		// Guard against a degenerate grid size (e.g. an object enabled with a serialized (0,0) size before editor-only
		// OnValidate has defaulted it): rendering it would try to allocate a zero-dimension RenderTexture and throw.
		// Leave isDirty set so we render for real as soon as a valid size arrives.
		var gridSize = GridSize;
		if (gridSize.x < 1 || gridSize.y < 1) return;
		isDirty = false;
		Render();
	}

	// Set by RenderInternal (via the Write* helpers) to the sub-rect it just wrote, so the output transform below runs
	// over only those texels; null = whole grid. Reset each Render(). This is what lets a drawable's region upload stay
	// region-scoped even with a magnitude/cookie active — the transform only ever re-scales freshly-written raw texels,
	// never the already-transformed rest, so it can't compound.
	protected RectInt? outputTransformRegion;

	public void Render() {
		outputTransformRegion = null;
		RenderInternal();

		// Apply the field's output transform — magnitude (scalar) and cookie (mask) — to the render texture in place,
		// over the extent RenderInternal just wrote (outputTransformRegion; whole grid when null). This is OUTPUT-space:
		// it scales the encoded field every consumer reads (GPU texture, group blend, visualizer, and the CPU readback
		// below) exactly once, without touching the component's internal/authored state — so consumers always
		// experience magnitude+cookie whether they sample on the GPU or the CPU. No-op when magnitude ≈ 1 and cookie is
		// None. (Pure CPU-mode producers with no render texture — none exist today — would need to bake this themselves.)
		if (renderTexture != null && cookie != null)
			cookie.Apply(renderTexture, GridSize, magnitude, outputTransformRegion);

		OnRendered?.Invoke();

		if (renderTexture == null) {
			// CPU-mode: RenderInternal already built vectorField, so it's ready now.
			OnCpuDataReady?.Invoke();
		} else if (WantsCpuData) {
			// GPU-mode with a registered CPU consumer: read the (output-transformed) texture back into vectorField
			// (synchronously if any consumer needs it this frame, otherwise async with no stall). HandleReadback
			// fires OnCpuDataReady. vectorField is the consumer-facing output copy — for components that author their
			// field on the CPU (see UploadSource), it's distinct from the authored buffer so this never clobbers it.
			ReadIntoCPU(forceImmediate: WantsImmediateCpuData);
		}
		// GPU-mode with no CPU consumer: skip the readback entirely — nobody needs the CPU copy.
	}

	protected abstract void RenderInternal();

	// The CPU field that WriteVectorFieldToRenderTexture[Region] uploads to the GPU. Defaults to vectorField (the
	// field RenderInternal computes into). Components that author their field on the CPU and keep it separate from
	// the readback target (e.g. the painted field) override this to point at their authored buffer, so uploads come
	// from the authored data while the cookie-masked readback lands in vectorField for consumers.
	protected virtual VectorFieldMap UploadSource => vectorField;

	AsyncGPUReadbackRequest? pendingReadback;

	// Reads the render texture into the CPU vectorField, then fires OnCpuDataReady. forceImmediate blocks until the
	// readback completes (data ready this frame); otherwise it's async (no stall). One readback serves every
	// registered consumer.
	public void ReadIntoCPU(bool forceImmediate = false) {
		if (renderTexture == null) {
			Debug.LogError("RenderTexture is not assigned.");
			return;
		}

		if (forceImmediate) {
			var request = AsyncGPUReadback.Request(renderTexture, 0);
			request.WaitForCompletion();
			HandleReadback(request);
			return;
		}

		// Coalesce overlapping requests, but key off .done rather than a sticky flag: a dropped completion callback
		// can never wedge this permanently — the next render simply issues a fresh request.
		if (pendingReadback.HasValue && !pendingReadback.Value.done) return;
		pendingReadback = AsyncGPUReadback.Request(renderTexture, 0, request => {
			if (this == null) return; // destroyed / reloaded while in flight
			HandleReadback(request);
		});
	}

	void HandleReadback(AsyncGPUReadbackRequest request) {
		if (request.hasError) {
			Debug.LogError("AsyncGPUReadback encountered an error.");
			return;
		}
		var rawData = request.GetData<Color>();
		// Reuse the existing map (and its array) when the size is unchanged; only reallocate on a resize.
		if (vectorField == null || vectorField.size.x != request.width || vectorField.size.y != request.height)
			vectorField = new VectorFieldMap(new Vector2Int(request.width, request.height));
		VectorFieldUtils.ColorsToVectors(rawData, 1, vectorField.values);

		// CPU copy is now current — notify consumers that read vectorField.
		OnCpuDataReady?.Invoke();
	}

	// Uploads a CPU-computed vectorField into renderTexture, using the same encoding HandleReadback decodes
	// (maxComponent 1, i.e. color = vector * 0.5 + 0.5), so a value sampled back equals the original. CPU-only
	// components (drawable, polygon) call this at the end of RenderInternal so their output participates in the draw
	// path, GPU group blend, and shader visualizer — all of which sample renderTexture, not the CPU map.
	//
	// uploadTexture is a persistent CPU-side mirror of the field: the full path rewrites all of it, the region path
	// rewrites just a sub-rect, and both then Apply + Blit. Reused across calls (only reallocated on a grid-size
	// change), as is the uploadColors encode buffer, so steady-state painting allocates nothing.
	Texture2D uploadTexture;
	Color[] uploadColors;
	bool EnsureUploadTexture() {
		var src = UploadSource;
		if (src == null) return false;
		EnsureHasValidRenderTexture();
		int width = src.size.x;
		int height = src.size.y;
		if (uploadTexture == null || uploadTexture.width != width || uploadTexture.height != height) {
			if (uploadTexture != null) VectorFieldObjectUtils.DestroyAutomatic(uploadTexture);
			// linear: true — this stores encoded vector data and must not be sRGB-converted on the Blit.
			uploadTexture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
		}
		return true;
	}

	protected void WriteVectorFieldToRenderTexture() {
		// Whole grid rewritten (raw) → the output transform must cover the whole grid.
		outputTransformRegion = null;
		if (!EnsureUploadTexture()) return;
		var src = UploadSource;
		int count = src.values.Length;
		if (uploadColors == null || uploadColors.Length != count) uploadColors = new Color[count];
		VectorFieldUtils.VectorsToColors(src.values, 1, uploadColors);
		uploadTexture.SetPixels(uploadColors);
		uploadTexture.Apply(false);
		Graphics.Blit(uploadTexture, renderTexture);
	}

	// Uploads only the given grid rect of vectorField, re-encoding just that sub-rect of the persistent mirror
	// instead of the whole grid (the win for brush painting, where each stroke touches a small region). The rest of
	// uploadTexture is left intact from prior uploads, so the GPU still receives a complete field. Falls back to the
	// full path whenever the mirror isn't already a valid full copy (first render / grid resize). Region is in grid
	// coordinates (origin bottom-left, matching the field and the readback) and is clamped to the field bounds.
	Color[] regionColors;
	readonly GpuRegionUploader regionUploader = new GpuRegionUploader();
	protected void WriteVectorFieldRegionToRenderTexture(RectInt region) {
		var src = UploadSource;
		int width = src != null ? src.size.x : 0;
		int height = src != null ? src.size.y : 0;
		// Patching requires both an up-to-date full mirror to layer onto AND a matching renderTexture to blit into;
		// if either is missing (first render, resize, or renderTexture released across a disable/enable), the full
		// path rebuilds both. (Checking renderTexture here also keeps us from ever blitting into the backbuffer.)
		bool canPatch = uploadTexture != null && uploadTexture.width == width && uploadTexture.height == height
			&& renderTexture != null && renderTexture.width == width && renderTexture.height == height;
		if (!canPatch) { WriteVectorFieldToRenderTexture(); return; }

		int x0 = Mathf.Clamp(region.xMin, 0, width);
		int y0 = Mathf.Clamp(region.yMin, 0, height);
		int x1 = Mathf.Clamp(region.xMax, 0, width);
		int y1 = Mathf.Clamp(region.yMax, 0, height);
		int w = x1 - x0, h = y1 - y0;
		if (w <= 0 || h <= 0) {
			// Nothing written → transform nothing (an empty region, NOT null, which would mean "whole grid" and
			// wrongly re-scale texels already transformed by a prior render, compounding them).
			outputTransformRegion = new RectInt(x0, y0, 0, 0);
			return;
		}

		// Encode just this sub-rect (brush-sized, so tiny next to a full-grid encode). Array length must equal the
		// block exactly for both the region copy and the SetPixels fallback below.
		int count = w * h;
		if (regionColors == null || regionColors.Length != count) regionColors = new Color[count];
		for (int ry = 0; ry < h; ry++)
			for (int rx = 0; rx < w; rx++)
				regionColors[ry * w + rx] = VectorFieldUtils.VectorToColor(src.GetValueAtGridPoint(x0 + rx, y0 + ry), 1);

		// Transfer ONLY this region to the GPU (renderTexture already holds a complete field to patch). This is the win
		// for painting: no full-texture Apply/Blit per frame. If region copies aren't supported, fall back to the mirror
		// + full re-upload path.
		if (regionUploader.TryUploadRegion(regionColors, w, h, renderTexture, x0, y0)) {
			// Only this region was overwritten with raw values → transform only it; the rest keeps its prior transform.
			outputTransformRegion = new RectInt(x0, y0, w, h);
			return;
		}

		// Fallback: the whole mirror is blitted, so the whole texture was rewritten raw → transform the whole grid.
		uploadTexture.SetPixels(x0, y0, w, h, regionColors);
		uploadTexture.Apply(false);
		Graphics.Blit(uploadTexture, renderTexture);
		outputTransformRegion = null;
	}

	public void ReleaseRenderTexture() {
		if (renderTexture == null) return;
		if (RenderTexture.active == renderTexture) RenderTexture.active = null;
		renderTexture.Release();
	}

	public void DestroyRenderTexture() {
		if (renderTexture == null) return;
		if (RenderTexture.active == renderTexture) RenderTexture.active = null;
		if (Application.isPlaying) Destroy(renderTexture);
		else DestroyImmediate(renderTexture);
		renderTexture = null;
	}

	public void EnsureHasValidRenderTexture() {
		VectorFieldRenderTextureUtils.EnsureValid(ref renderTexture, grid.Size.x, grid.Size.y);
	}

	public Vector3 EvaluateWorldVector(Vector3 position) {
		return transform.TransformDirection(EvaluateVector(position));
	}

	public virtual Vector2 EvaluateVector(Vector3 position) {
		// CPU sampling needs the CPU mirror, which only exists while a CPU consumer is registered (GPU-only fields
		// leave it null). Register as a CPU consumer, or use TrySampleVector to read straight from the GPU texture.
		if (vectorField == null) {
			Debug.LogWarning("EvaluateVector called but this field has no CPU copy. Register a CPU consumer (RegisterCpuConsumer) or use TrySampleVector for GPU sampling.", this);
			return Vector2.zero;
		}
		var gridPosition = grid.WorldToGridPosition(position);
		// magnitude is already baked into vectorField (read back from the output-transformed texture), so no re-scale.
		return vectorField.GetValueAtGridPosition(gridPosition);
	}

	public Quaternion EvaluateRotation(Vector3 position) {
		// return transform.rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value)
		return Quaternion.LookRotation(EvaluateWorldVector(position), planeNormal);
	}

	// --- GPU sampling -------------------------------------------------------------------------------------------
	// Sample the field straight from the GPU render texture, reading only the handful of texels a query needs,
	// instead of mirroring the whole grid to the CPU. Decoding matches ReadIntoCPU exactly so
	// these agree with EvaluateVector. The synchronous variants stall for the readback (fine for occasional use);
	// use the async variant on hot paths. All return false / skip when the platform can't do GPU readback or
	// nothing has been rendered yet.

	public static bool SupportsGPUSampling => SystemInfo.supportsAsyncGPUReadback;

	// Local-space vector (matches EvaluateVector), sampled bilinearly. Blocks until the readback completes.
	public bool TrySampleVector(Vector3 worldPosition, out Vector2 localVector) {
		localVector = Vector2.zero;
		if (renderTexture == null || !SupportsGPUSampling) return false;

		var gridPosition = grid.WorldToGridPosition(worldPosition);
		var region = GetSampleRegion(gridPosition);
		var request = AsyncGPUReadback.Request(renderTexture, 0, region.x, region.width, region.y, region.height, 0, 1, TextureFormat.RGBAFloat);
		request.WaitForCompletion();
		if (request.hasError) return false;

		// magnitude is already baked into the texture by the output transform, so no re-scale here.
		localVector = SampleRegion(request.GetData<Color>(), region, gridPosition);
		return true;
	}

	// World-space vector (matches EvaluateWorldVector). Blocks until the readback completes.
	public bool TrySampleWorldVector(Vector3 worldPosition, out Vector3 worldVector) {
		worldVector = Vector3.zero;
		if (!TrySampleVector(worldPosition, out var local)) return false;
		worldVector = transform.TransformDirection(local);
		return true;
	}

	// Non-blocking world-space sample. Invokes onComplete with the world vector once the GPU readback returns,
	// or does nothing if the platform/render texture can't satisfy the request.
	public void SampleWorldVectorAsync(Vector3 worldPosition, Action<Vector3> onComplete) {
		if (renderTexture == null || !SupportsGPUSampling || onComplete == null) return;

		var gridPosition = grid.WorldToGridPosition(worldPosition);
		var region = GetSampleRegion(gridPosition);
		AsyncGPUReadback.Request(renderTexture, 0, region.x, region.width, region.y, region.height, 0, 1, TextureFormat.RGBAFloat, request => {
			if (request.hasError || this == null) return;
			var local = SampleRegion(request.GetData<Color>(), region, gridPosition);   // magnitude already baked in
			onComplete(transform.TransformDirection(local));
		});
	}

	// Batched local-space sample. Reads one region covering all query points, then interpolates each from it,
	// so N clustered queries cost a single readback. Writes into results (which must be at least worldPositions
	// long) and returns false without touching results if the field can't be sampled.
	public bool TrySampleVectors(IReadOnlyList<Vector3> worldPositions, Vector2[] results) {
		if (renderTexture == null || !SupportsGPUSampling || worldPositions.Count == 0) return false;

		var gridPositions = new Vector2[worldPositions.Count];
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		for (int i = 0; i < worldPositions.Count; i++) {
			var gp = grid.WorldToGridPosition(worldPositions[i]);
			gridPositions[i] = gp;
			minX = Mathf.Min(minX, gp.x); minY = Mathf.Min(minY, gp.y);
			maxX = Mathf.Max(maxX, gp.x); maxY = Mathf.Max(maxY, gp.y);
		}

		var region = GetSampleRegion(new Vector2(minX, minY), new Vector2(maxX, maxY));
		var request = AsyncGPUReadback.Request(renderTexture, 0, region.x, region.width, region.y, region.height, 0, 1, TextureFormat.RGBAFloat);
		request.WaitForCompletion();
		if (request.hasError) return false;

		var data = request.GetData<Color>();
		for (int i = 0; i < gridPositions.Length; i++)
			results[i] = SampleRegion(data, region, gridPositions[i]);   // magnitude already baked in
		return true;
	}

	// The clamped texel rectangle (in render-texture space) that covers the bilinear footprint of the given grid
	// position range. Always at least 1x1; 2x2 for an interior point.
	RectInt GetSampleRegion(Vector2 gridPositionMin, Vector2 gridPositionMax) {
		int maxTexelX = renderTexture.width - 1;
		int maxTexelY = renderTexture.height - 1;
		int left = Mathf.Clamp(Mathf.FloorToInt(gridPositionMin.x), 0, maxTexelX);
		int bottom = Mathf.Clamp(Mathf.FloorToInt(gridPositionMin.y), 0, maxTexelY);
		int right = Mathf.Clamp(Mathf.FloorToInt(gridPositionMax.x) + 1, 0, maxTexelX);
		int top = Mathf.Clamp(Mathf.FloorToInt(gridPositionMax.y) + 1, 0, maxTexelY);
		return new RectInt(left, bottom, right - left + 1, top - bottom + 1);
	}

	RectInt GetSampleRegion(Vector2 gridPosition) => GetSampleRegion(gridPosition, gridPosition);

	// Bilinearly interpolate a single grid position out of an already-read texel region.
	static Vector2 SampleRegion(NativeArray<Color> data, RectInt region, Vector2 gridPosition) {
		int left = Mathf.Clamp(Mathf.FloorToInt(gridPosition.x), region.x, region.xMax - 1);
		int bottom = Mathf.Clamp(Mathf.FloorToInt(gridPosition.y), region.y, region.yMax - 1);
		int right = Mathf.Min(left + 1, region.xMax - 1);
		int top = Mathf.Min(bottom + 1, region.yMax - 1);

		Vector2 Texel(int x, int y) => VectorFieldUtils.ColorToVector(data[(y - region.y) * region.width + (x - region.x)], 1);

		Vector2 frac = new Vector2(gridPosition.x - Mathf.Floor(gridPosition.x), gridPosition.y - Mathf.Floor(gridPosition.y));
		Vector2 x1 = Vector2.Lerp(Texel(left, bottom), Texel(right, bottom), frac.x);
		Vector2 x2 = Vector2.Lerp(Texel(left, top), Texel(right, top), frac.x);
		return Vector2.Lerp(x1, x2, frac.y);
	}

	public Bounds GetBounds() {
		return grid.GetWorldBounds();
	}
}
