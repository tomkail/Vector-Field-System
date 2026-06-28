using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GroupVectorFieldComponent : VectorFieldComponent {
	[System.Serializable]
	public class VectorFieldLayer {
		public VectorFieldComponent component;

		[Range(0, 1)]
		public float strength = 1;

		public BlendMode blendMode = BlendMode.Add;
		public enum BlendMode {
			// Add to current value
			Add,
			// Lerp between current and new value based on brush alpha
			Blend
		}

		[EnumFlagsButtonGroup] public Component components = Component.All;
		[Flags]
		public enum Component {
			None = 0,
			// Which aspects of the incoming vector affect the result. These are independent bits, so they compose:
			// Magnitude | Direction == All.
			Magnitude = 1 << 0,
			Direction = 1 << 1,
			All = Magnitude | Direction,
		}

		// public Texture2D texture;
	}

	public List<VectorFieldLayer> layers = new List<VectorFieldLayer>();
	IEnumerable<VectorFieldComponent> childComponents => this.GetComponentsX(ComponentX.ComponentSearchParams<VectorFieldComponent>.AllDescendentsExcludingSelf(false));

	public Mode mode = Mode.CPU;
	public enum Mode {
		CPU,
		GPU
	}

	void RefreshLayers() {
		layers.RemoveAll(x => x.component == null);
		List<VectorFieldComponent> added = new List<VectorFieldComponent>();
		List<VectorFieldComponent> removed = new List<VectorFieldComponent>();
		IEnumerableX.GetChanges(childComponents, layers.Select(x => x.component), out added, out removed);
		foreach (var component in added) {
			Debug.Log("Added " + component, this);
			layers.Add(new VectorFieldLayer() {
				component = component
			});
		}
		foreach (var component in removed) {
			layers.RemoveAll(x => x.component == component);
		}
		layers = layers.OrderBy(x => x.component.transform.GetHeirarchyIndex()).ToList();
	}

	protected override void RenderInternal() {
		RefreshLayers();

		// Pull each child up to date before blending so we always combine fresh data, regardless of the order the
		// dirty pump happens to visit components in.
		foreach (var layer in layers)
			if (layer.component != null) layer.component.EnsureUpToDate();

		// For performance we should iterate layers first, then iterate points.
		// For each layer we should first determine the points on both canvases that are in the overlap.
		// var points = gridRenderer.GetPointsInWorldBounds(child.transform.GetBounds());

		if (mode == Mode.CPU)
			RenderInternalCPU();
		else
			RenderInternalGPU();
	}


	// Re-blend when the set of direct child layers changes. Per-child value/transform changes already propagate
	// here via VectorFieldComponent.SetDirty, and the group's own transform is tracked by base.Update.
	void OnTransformChildrenChanged() => SetDirty();

	// Re-blend on changes to the blend mode or to any layer's settings (strength / blend mode / component mask /
	// the component referenced). Inspector edits also come through OnValidate; this covers runtime mutation.
	Mode lastMode;
	int lastLayersHash;
	protected override bool ParametersChanged() {
		bool changed = base.ParametersChanged();
		if (lastMode != mode) { lastMode = mode; changed = true; }
		int hash = ComputeLayersHash();
		if (lastLayersHash != hash) { lastLayersHash = hash; changed = true; }
		return changed;
	}

	int ComputeLayersHash() {
		var hash = new HashCode();
		hash.Add(layers.Count);
		foreach (var layer in layers) {
			hash.Add(layer.component != null ? layer.component.GetEntityId().GetHashCode() : 0);
			hash.Add(layer.strength);
			hash.Add((int)layer.blendMode);
			hash.Add((int)layer.components);
		}
		return hash.ToHashCode();
	}

	void RenderInternalGPU() {
		if (layers.Count == 0 || CombineVectorFieldsComputeShader == null)
			return;

		// Create two temporary RenderTextures
		RenderTexture currentRT = RenderTexture.GetTemporary(gridRenderer.gridSize.x, gridRenderer.gridSize.y, 0, RenderTextureFormat.ARGBFloat);
		RenderTexture nextRT = RenderTexture.GetTemporary(gridRenderer.gridSize.x, gridRenderer.gridSize.y, 0, RenderTextureFormat.ARGBFloat);

		RenderTexture.active = currentRT;
		GL.Clear(true, true, new Color(0.5f, 0.5f, 0, 1));
		RenderTexture.active = null;

		for (int i = 0; i < layers.Count; i++) {
			if (layers[i] == null || layers[i].component.renderTexture == null) continue;
			if (layers[i].components == VectorFieldLayer.Component.None) continue;

			CombineVectorFields(new CombineVectorFieldsParams() {
				vectorFieldALocalToWorldMatrix = transform.localToWorldMatrix,
				vectorFieldA = currentRT,
				vectorFieldBLocalToWorldMatrix = layers[i].component.transform.localToWorldMatrix,
				vectorFieldB = layers[i].component.renderTexture,
				blendMode = layers[i].blendMode,
				components = layers[i].components,
				strength = layers[i].strength
			}, nextRT);

			// Swap render textures
			(currentRT, nextRT) = (nextRT, currentRT);
		}

		EnsureHasValidRenderTexture();
		Graphics.Blit(currentRT, renderTexture);

		// Release temporary render textures
		RenderTexture.ReleaseTemporary(currentRT);
		RenderTexture.ReleaseTemporary(nextRT);
	}

	static Shader combineVectorFieldsComputeShader;
	public static Shader CombineVectorFieldsComputeShader => combineVectorFieldsComputeShader ? combineVectorFieldsComputeShader : (combineVectorFieldsComputeShader = Resources.Load<Shader>("CombineVectorFields"));

	public class CombineVectorFieldsParams {
		public Matrix4x4 vectorFieldALocalToWorldMatrix;
		public RenderTexture vectorFieldA;

		public Matrix4x4 vectorFieldBLocalToWorldMatrix;
		public RenderTexture vectorFieldB;

		public VectorFieldLayer.BlendMode blendMode;
		public VectorFieldLayer.Component components;
		public float strength;
	}
	public static void CombineVectorFields(CombineVectorFieldsParams combineVectorFieldsParams, RenderTexture targetTarget) {
		var material = new Material(CombineVectorFieldsComputeShader);
		// GetRelativeTransform(layers[i].component.transform, transform)
		material.SetTexture("_VectorField", combineVectorFieldsParams.vectorFieldB);
		material.SetMatrix("_RelativeTransform", GetRelativeTransform(combineVectorFieldsParams.vectorFieldBLocalToWorldMatrix, combineVectorFieldsParams.vectorFieldALocalToWorldMatrix));
		material.SetFloat("_Strength", combineVectorFieldsParams.strength);
		material.SetInt("_BlendMode", (int)combineVectorFieldsParams.blendMode);
		// Pass the component flags as a bitmask (Magnitude = 1, Direction = 2, All = 3); the shader checks the bits.
		material.SetInt("_Components", (int)combineVectorFieldsParams.components);

		// RenderTexture.active = targetTarget;
		// GL.Clear(true, true, Color.black);
		// RenderTexture.active = null;

		Graphics.Blit(combineVectorFieldsParams.vectorFieldA, targetTarget, material);

		DestroyImmediate(material);
	}

	static Matrix4x4 GetRelativeTransform(Transform brushTransform, Transform canvasTransform) {
		// Adjust for UV coordinate space (translate UV to object space)
		Matrix4x4 UVtoObj = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0));
		// Adjust back from object space to UV space after transformations
		Matrix4x4 ObjToUV = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
		// Compute the matrix that transforms from t1's UV space to t2's UV space
		return ObjToUV * brushTransform.worldToLocalMatrix * canvasTransform.localToWorldMatrix * UVtoObj;
	}

	static Matrix4x4 GetRelativeTransform(Matrix4x4 brushMatrix4x4, Matrix4x4 canvasMatrix4x4) {
		// Adjust for UV coordinate space (translate UV to object space)
		Matrix4x4 UVtoObj = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0));
		// Adjust back from object space to UV space after transformations
		Matrix4x4 ObjToUV = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
		// Compute the matrix that transforms from t1's UV space to t2's UV space
		return ObjToUV * brushMatrix4x4.inverse * canvasMatrix4x4 * UVtoObj;
	}

	void RenderInternalCPU() {
		vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
		// var points = vectorField.Points();

		var validLayers = layers.Where(layer => layer.component.isActiveAndEnabled && layer.strength > 0).ToList();
		foreach (var layer in validLayers) {
			var points = gridRenderer.GetPointsInWorldBounds(layer.component.GetBounds());
			foreach (var point in points) {
				Vector2 current = vectorField.GetValueAtGridPoint(point);

				var pointWorldPosition = gridRenderer.cellCenter.GridToWorldPoint(point);
				Vector2 incoming = transform.InverseTransformDirection(layer.component.EvaluateWorldVector(pointWorldPosition));

				vectorField.SetValueAtGridPoint(point, BlendVector(current, incoming, layer.strength, layer.blendMode, layer.components));
			}
		}
	}

	// The canonical per-vector blend, shared in spirit with BlendVectors() in CombineVectorFields.shader — keep the
	// two in sync. Magnitude and Direction are independent aspects of the incoming vector that compose, so
	// Magnitude | Direction (== All) takes both. All normalization is zero-safe (a zero vector normalizes to zero)
	// to avoid NaNs from the zero base / cancelling sums.
	public static Vector2 BlendVector(Vector2 current, Vector2 incoming, float strength, VectorFieldLayer.BlendMode blendMode, VectorFieldLayer.Component components) {
		bool hasMagnitude = (components & VectorFieldLayer.Component.Magnitude) != 0;
		bool hasDirection = (components & VectorFieldLayer.Component.Direction) != 0;
		if (!hasMagnitude && !hasDirection) return current;

		if (blendMode == VectorFieldLayer.BlendMode.Add) {
			if (hasMagnitude && hasDirection) return current + incoming * strength;
			// Magnitude only: lengthen along the current direction by the incoming magnitude.
			if (hasMagnitude) return current + SafeNormalize(current) * incoming.magnitude * strength;
			// Direction only: add a push of the current magnitude toward the incoming direction.
			return current + SafeNormalize(incoming) * current.magnitude * strength;
		} else { // Blend
			if (hasMagnitude && hasDirection) return Vector2.Lerp(current, incoming, strength);
			// Magnitude only: keep the current direction, blend its length toward the incoming length.
			if (hasMagnitude) return SafeNormalize(current) * Mathf.Lerp(current.magnitude, incoming.magnitude, strength);
			// Direction only: rotate the current direction toward the incoming one, keep the current magnitude.
			return SafeNormalize(Vector2.Lerp(SafeNormalize(current), SafeNormalize(incoming), strength)) * current.magnitude;
		}
	}

	static Vector2 SafeNormalize(Vector2 v) {
		float magnitude = v.magnitude;
		return magnitude > 1e-6f ? v / magnitude : Vector2.zero;
	}
}
