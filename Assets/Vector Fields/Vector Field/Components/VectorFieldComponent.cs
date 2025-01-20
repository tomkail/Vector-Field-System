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

	// This is called when the application starts, when a scene loads, when a component is created (in editor or runtime)
	protected virtual void Awake() {
		Debug.Log("Awake", this);
		renderTexture = null;
	}

	// This is called after Awake and on recompile
	protected virtual void OnEnable() {
		Debug.Log("OnEnable", this);
		EnsureInitialized();
		if (savedTexture != null) {
			ConvertTexture2DToRenderTexture();
		}
		SetDirty();
		Update();
	}

	protected virtual void EnsureInitialized() {
		// This will leak?
		// if (vectorFieldTexture != null) vectorFieldTexture = null;
		gridRenderer = GetComponent<GridRenderer>();
		EditorApplication.update += SetDirty;
	}

	protected virtual void OnDisable() {
		TryRenderGroup();

		if (renderTexture != null) {
			ConvertRenderTextureToTexture2D();
		}
		EditorApplication.update -= SetDirty;
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
		if (!isActiveAndEnabled) return;
	}
#endif


	public virtual void Update() {
		if (!isActiveAndEnabled) return;
		var newSTransform = new SerializableTransform(transform);
		if (lastTransform != newSTransform) {
			lastTransform = newSTransform;
			SetDirty();
		}
	}

	public virtual void SetDirty() {
		if (!isActiveAndEnabled) {
			Debug.Log("Setting dirty for disabled VectorFieldComponent");
			EditorApplication.update += SetDirty;
			return;
		}

		Render();
		TryRenderGroup();
	}

	void TryRenderGroup() {
		if (group != null && group.isActiveAndEnabled) group.Render();
	}

	public void Render() {
		RenderInternal();
		if (keepCPUUpdated && renderTexture != null) ReadIntoCPU();
		OnRender?.Invoke();
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
			Vector2[] vectors = VectorFieldUtils.ColorsToVectors(rawData, 1);
			vectorField = new Vector2Map(new Point(request.width, request.height), vectors);
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

		savedTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);

		RenderTexture.active = renderTexture;
		savedTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
		savedTexture.Apply();
		RenderTexture.active = null;

		// Debug.Log("RenderTexture converted to Texture2D for serialization.");
	}

	void ConvertTexture2DToRenderTexture() {
		if (savedTexture == null) return;

		renderTexture = new RenderTexture(savedTexture.width, savedTexture.height, 24);
		RenderTexture.active = renderTexture;
		Graphics.Blit(savedTexture, renderTexture);
		RenderTexture.active = null;

		// Debug.Log("Texture2D restored to RenderTexture after deserialization.");
	}
}
