using System;
using System.Linq;
using System.Threading.Tasks;
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
			// vectorField is filled asynchronously, so defer OnRender to the readback callback — otherwise CPU
			// consumers (e.g. the particle force field) would be notified before the new data lands and, now that
			// we only render on change, would never see it.
			ReadIntoCPU();
		} else {
			// CPU-mode (or no GPU texture): vectorField is already current.
			OnRender?.Invoke();
		}
	}

	protected abstract void RenderInternal();

	AsyncGPUReadbackRequest? readbackRequest;

	// Reads the vector field texture into the VectorField object. Will only run if not already running.
	public async Task ReadIntoCPU(bool forceImmediate = false) {
		// Ensure the RenderTexture is not null
		if (renderTexture == null) {
			Debug.LogError("RenderTexture is not assigned.");
			return;
		}

		try {
			if (readbackRequest == null || ((AsyncGPUReadbackRequest)readbackRequest).done) {
				// Perform async readback for better performance
				// AsyncGPUReadback.Request(renderTexture, 0, Callback);
				readbackRequest = await AsyncGPUReadback.RequestAsync(renderTexture, 0);
				Callback((AsyncGPUReadbackRequest)readbackRequest);
			}

			if (forceImmediate || vectorField == null) {
				((AsyncGPUReadbackRequest)readbackRequest).WaitForCompletion();
			}
		} catch (OperationCanceledException) {
			// Expected when a domain/script reload interrupts the in-flight GPU readback. Safe to ignore.
		} catch (Exception e) {
			Debug.LogError(e);
		} finally {
			readbackRequest = null;
		}

		void Callback(AsyncGPUReadbackRequest request) {
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
