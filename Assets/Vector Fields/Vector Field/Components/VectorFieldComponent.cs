using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways, RequireComponent(typeof(GridRenderer))]
public abstract class VectorFieldComponent : MonoBehaviour {
	protected GroupVectorFieldComponent group {
		get {
			if (this == null) {
				Debug.LogError("VectorFieldComponent is null");
				return null;
			}
			return this.GetComponentsX(ComponentX.ComponentSearchParams<GroupVectorFieldComponent>.AllAncestorsExcludingSelf(true)).FirstOrDefault();
		}
	}

	public GridRenderer gridRenderer { get; private set; }
	public Vector3 planeNormal => transform.forward;

	[Space]
	[AssetSaver] public Texture2D savedTexture;
	[NonSerialized] public RenderTexture renderTexture;

	// The vector field data is stored in textures
	public Vector2Map vectorField;

	public float magnitude = 1;

	public Texture2D cookieTexture;
	// public VectorFieldCookieTextureCreator cookieTextureCreator;

	public delegate void OnUpdateDelegate();
	public event OnUpdateDelegate OnRender;

	SerializableTransform lastTransform;

	public bool keepCPUUpdated = true;

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
		if (savedTexture != null) {
			ConvertTexture2DToRenderTexture();
		}
		SetDirty();
		EnsureUpToDate();
	}

	protected virtual void EnsureInitialized() {
		gridRenderer = GetComponent<GridRenderer>();
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

		if (renderTexture != null) {
			ConvertRenderTextureToTexture2D();
		}
#if UNITY_EDITOR
		EditorApplication.update -= Tick;
#endif
	}

	protected virtual void OnDestroy() {
		// ObjectX.DestroyAutomatic(vectorFieldTexture);
	}

#if UNITY_EDITOR
	protected virtual void OnValidate() {
		if (!isActiveAndEnabled) return;
		gridRenderer = GetComponent<GridRenderer>();
		gridRenderer.modeModule = ScriptableObject.CreateInstance<GridRendererManhattanModeModule>();
		gridRenderer.scaleWithGridSize = false;
		if (gridRenderer.gridSize == Point.zero) gridRenderer.gridSize = new Point(64, 64);
		if (gridRenderer.gridSize.x < 1) gridRenderer.gridSize = new Point(1, gridRenderer.gridSize.y);
		if (gridRenderer.gridSize.y < 1) gridRenderer.gridSize = new Point(gridRenderer.gridSize.x, 1);
		// gridRenderer.showGizmos = true;
		lastTransform = new SerializableTransform(transform);
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
		var current = new SerializableTransform(transform);
		if (lastTransform != current) {
			lastTransform = current;
			return true;
		}
		return false;
	}

	// Snapshot of output-affecting parameters. Each override compares-and-caches its own fields (calling base),
	// updating every cached field on every call (no short-circuiting) so a change to any one is never missed.
	float lastMagnitude = float.NaN;
	Point lastGridSize = new Point(-1, -1);
	Texture2D lastCookieTexture;
	protected virtual bool ParametersChanged() {
		bool changed = false;
		if (lastMagnitude != magnitude) { lastMagnitude = magnitude; changed = true; }
		var gridSize = gridRenderer != null ? gridRenderer.gridSize : Point.zero;
		if (lastGridSize != gridSize) { lastGridSize = gridSize; changed = true; }
		if (lastCookieTexture != cookieTexture) { lastCookieTexture = cookieTexture; changed = true; }
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
		isDirty = false;
		Render();
	}

	public void Render() {
		RenderInternal();
		if (keepCPUUpdated && renderTexture != null) {
			// Populate the CPU vectorField synchronously so OnRender fires this frame with fresh data, matching the
			// CPU-combine path. The async readback didn't reliably notify consumers (e.g. the particle force field);
			// since we now render only on change rather than every frame, the readback stall is affordable.
			ReadIntoCPU(forceImmediate: true);
		} else {
			// CPU-mode (or no GPU texture): vectorField is already current.
			OnRender?.Invoke();
		}
	}

	protected abstract void RenderInternal();

	bool readbackInFlight;

	// Reads the render texture into the CPU vectorField, then fires OnRender. Uses the callback-based AsyncGPUReadback
	// API (not await) so completion is reliably pumped by the engine in both edit and play mode — important now that
	// we only render on change: a single dropped continuation would otherwise leave consumers (the particle force
	// field) stuck on stale data. Pass forceImmediate to block until the readback completes (e.g. before sampling).
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

		if (readbackInFlight) return; // a newer readback will be kicked off by the next render
		readbackInFlight = true;
		AsyncGPUReadback.Request(renderTexture, 0, request => {
			readbackInFlight = false;
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
			vectorField = new Vector2Map(new Point(request.width, request.height));
		VectorFieldUtils.ColorsToVectors(rawData, 1, vectorField.values);

		// CPU copy is now current — notify consumers that read vectorField.
		OnRender?.Invoke();
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
		var renderTextureDescriptor = new RenderTextureDescriptor(gridRenderer.gridSize.x, gridRenderer.gridSize.y, RenderTextureFormat.ARGBFloat, 0) {
			enableRandomWrite = true,
		};
		if (renderTexture == null) {
			renderTexture = new RenderTexture(renderTextureDescriptor) {
				filterMode = FilterMode.Bilinear
			};
		} else if (!RenderTextureDescriptorsMatch(renderTexture.descriptor, renderTextureDescriptor)) {
			var rtFilterMode = renderTexture.filterMode;

			if (RenderTexture.active == renderTexture) RenderTexture.active = null;
			renderTexture.Release();

			renderTexture.descriptor = renderTextureDescriptor;
			renderTexture.Create();
			renderTexture.filterMode = rtFilterMode;
		}
		static bool RenderTextureDescriptorsMatch(RenderTextureDescriptor descriptorA, RenderTextureDescriptor descriptorB) {
			if (descriptorA.depthBufferBits != descriptorB.depthBufferBits) return false;
			if (descriptorA.width != descriptorB.width) return false;
			if (descriptorA.height != descriptorB.height) return false;
			if (descriptorA.depthStencilFormat != descriptorB.depthStencilFormat) return false;
			if (descriptorA.enableRandomWrite != descriptorB.enableRandomWrite) return false;
			if (descriptorA.colorFormat != descriptorB.colorFormat) return false;
			if (descriptorA.dimension != descriptorB.dimension) return false;
			return true;
		}
	}

	public Vector3 EvaluateWorldVector(Vector3 position) {
		return transform.TransformDirection(EvaluateVector(position));
	}

	public virtual Vector2 EvaluateVector(Vector3 position) {
		var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(position);
		return vectorField.GetValueAtGridPosition(gridPosition) * magnitude;
	}

	public Quaternion EvaluateRotation(Vector3 position) {
		// return transform.rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value)
		return Quaternion.LookRotation(EvaluateWorldVector(position), planeNormal);
	}

	// --- GPU sampling -------------------------------------------------------------------------------------------
	// Sample the field straight from the GPU render texture, reading only the handful of texels a query needs,
	// instead of mirroring the whole grid to the CPU (keepCPUUpdated). Decoding matches ReadIntoCPU exactly so
	// these agree with EvaluateVector. The synchronous variants stall for the readback (fine for occasional use);
	// use the async variant on hot paths. All return false / skip when the platform can't do GPU readback or
	// nothing has been rendered yet.

	public static bool SupportsGPUSampling => SystemInfo.supportsAsyncGPUReadback;

	// Local-space vector (matches EvaluateVector), sampled bilinearly. Blocks until the readback completes.
	public bool TrySampleVector(Vector3 worldPosition, out Vector2 localVector) {
		localVector = Vector2.zero;
		if (renderTexture == null || !SupportsGPUSampling) return false;

		var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(worldPosition);
		var region = GetSampleRegion(gridPosition);
		var request = AsyncGPUReadback.Request(renderTexture, 0, region.x, region.width, region.y, region.height, 0, 1, TextureFormat.RGBAFloat);
		request.WaitForCompletion();
		if (request.hasError) return false;

		localVector = SampleRegion(request.GetData<Color>(), region, gridPosition) * magnitude;
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

		var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(worldPosition);
		var region = GetSampleRegion(gridPosition);
		AsyncGPUReadback.Request(renderTexture, 0, region.x, region.width, region.y, region.height, 0, 1, TextureFormat.RGBAFloat, request => {
			if (request.hasError || this == null) return;
			var local = SampleRegion(request.GetData<Color>(), region, gridPosition) * magnitude;
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
			var gp = gridRenderer.cellCenter.WorldToGridPosition(worldPositions[i]);
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
			results[i] = SampleRegion(data, region, gridPositions[i]) * magnitude;
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
		var bounds = gridRenderer.edge.NormalizedToWorldRect(new Rect(0, 0, 1, 1));
		return BoundsX.CreateEncapsulating(bounds);
	}



	public static Texture2D CreateRampTextureFromAnimationCurve(AnimationCurve curve, int textureWidth, ref Texture2D texture) {
		// if (curveTexture == null || curveTexture.width != textureWidth || curveTexture.height != 1 || curveTexture.format != TextureFormat.RFloat || curveTexture.wrapMode != TextureWrapMode.Clamp) {
		//     if (curveTexture != null) ObjectX.DestroyAutomatic(curveTexture);
		// }
		if (texture == null) {
			texture = new Texture2D(textureWidth, 1, TextureFormat.RFloat, false, true) {
				wrapMode = TextureWrapMode.Clamp
			};
		}
		for (int i = 0; i < textureWidth; i++) {
			float t = i / (float)(textureWidth - 1);
			float value = curve.Evaluate(t);
			texture.SetPixel(i, 0, new Color(value, value, value, value));
		}
		texture.Apply();
		return texture;
	}

	void ConvertRenderTextureToTexture2D() {
		if (renderTexture == null) return;

		// linear: true — this stores encoded vector data, so it must not be sRGB-converted on read-back.
		savedTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false, true);

		RenderTexture.active = renderTexture;
		savedTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
		savedTexture.Apply();
		RenderTexture.active = null;

		// Debug.Log("RenderTexture converted to Texture2D for serialization.");
	}

	void ConvertTexture2DToRenderTexture() {
		if (savedTexture == null) return;

		// Linear read/write so the Blit preserves the encoded vectors instead of applying sRGB (Built-in -> URP safe).
		renderTexture = new RenderTexture(savedTexture.width, savedTexture.height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
		RenderTexture.active = renderTexture;
		Graphics.Blit(savedTexture, renderTexture);
		RenderTexture.active = null;

		// Debug.Log("Texture2D restored to RenderTexture after deserialization.");
	}
}
