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

		// The two fields below are per-layer modulators that apply in EVERY blend mode (they're orthogonal to the
		// mode itself). Their defaults are no-ops, so a plain Add / Blend layer behaves exactly as before.

		// Scales this layer's effective strength by how aligned it is with the field beneath it:
		// x = (dot(currentDir, incomingDir) + 1) / 2, so 0 = fully opposed, 0.5 = perpendicular, 1 = fully aligned.
		// Default is a flat 1 (no effect). E.g. a 0->1 ramp applies the layer only where it agrees with the flow.
		public AnimationCurve alignmentRamp = AnimationCurve.Constant(0, 1, 1);
		// Multiplies the incoming vector by the underlying field's magnitude before blending, so this layer only acts
		// where there's already flow and scales with its speed. This is the turbulence coupling: an Add layer with a
		// 0->1 alignmentRamp and this enabled reproduces the old flow-modulated turbulence.
		public bool scaleByFieldMagnitude = false;
		// Cached GPU bake of alignmentRamp, reused across renders (rebaked when the curve changes).
		[NonSerialized] public Texture2D alignmentRampTexture;

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
			hash.Add(layer.scaleByFieldMagnitude);
			// Indexer (not .keys) so this per-tick hash doesn't allocate a Keyframe[] every call.
			if (layer.alignmentRamp != null) {
				hash.Add(layer.alignmentRamp.length);
				for (int k = 0; k < layer.alignmentRamp.length; k++) {
					var key = layer.alignmentRamp[k];
					hash.Add(key.time);
					hash.Add(key.value);
				}
			}
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

			// Both modulators default to no-ops; only do the work (and only bake the ramp) when a layer actually uses
			// one. The shader's modulation path is keyword-gated, so an unmodulated layer pays nothing for either.
			bool usesAlignment = !IsIdentityRamp(layers[i].alignmentRamp);
			Texture2D alignmentRamp = usesAlignment
				? CreateRampTextureFromAnimationCurve(layers[i].alignmentRamp, 256, ref layers[i].alignmentRampTexture)
				: null;

			CombineVectorFields(new CombineVectorFieldsParams() {
				vectorFieldALocalToWorldMatrix = transform.localToWorldMatrix,
				vectorFieldA = currentRT,
				vectorFieldBLocalToWorldMatrix = layers[i].component.transform.localToWorldMatrix,
				vectorFieldB = layers[i].component.renderTexture,
				blendMode = layers[i].blendMode,
				components = layers[i].components,
				strength = layers[i].strength,
				alignmentRamp = alignmentRamp,
				scaleByFieldMagnitude = layers[i].scaleByFieldMagnitude
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
		// Baked alignmentRamp; non-null only when the ramp isn't the identity (flat 1). Null => no alignment weighting.
		public Texture2D alignmentRamp;
		public bool scaleByFieldMagnitude;
	}
	public static void CombineVectorFields(CombineVectorFieldsParams combineVectorFieldsParams, RenderTexture targetTarget) {
		var material = new Material(CombineVectorFieldsComputeShader);
		// GetRelativeTransform(layers[i].component.transform, transform)
		material.SetTexture("_VectorField", combineVectorFieldsParams.vectorFieldB);
		material.SetMatrix("_RelativeTransform", GetRelativeTransform(combineVectorFieldsParams.vectorFieldBLocalToWorldMatrix, combineVectorFieldsParams.vectorFieldALocalToWorldMatrix));
		// Pure, scale-free rotation taking a direction from the layer's local frame into the group's. Mirrors the CPU
		// path's InverseTransformDirection(TransformDirection(...)) (rotation only); the shader rotates the sampled
		// vector with this and projects onto the group plane. (_RelativeTransform still handles the sample position.)
		var relativeRotation = Quaternion.Inverse(combineVectorFieldsParams.vectorFieldALocalToWorldMatrix.rotation) * combineVectorFieldsParams.vectorFieldBLocalToWorldMatrix.rotation;
		material.SetMatrix("_VectorRotation", Matrix4x4.Rotate(relativeRotation));
		material.SetFloat("_Strength", combineVectorFieldsParams.strength);
		material.SetInt("_BlendMode", (int)combineVectorFieldsParams.blendMode);
		// Pass the component flags as a bitmask (Magnitude = 1, Direction = 2, All = 3); the shader checks the bits.
		material.SetInt("_Components", (int)combineVectorFieldsParams.components);

		// The alignment-ramp / field-magnitude modulation is keyword-gated so an unmodulated layer compiles it out
		// entirely. Enable it only when one of the two is actually in use.
		bool usesAlignment = combineVectorFieldsParams.alignmentRamp != null;
		bool modulate = usesAlignment || combineVectorFieldsParams.scaleByFieldMagnitude;
		if (modulate) {
			material.EnableKeyword("VF_MODULATION");
			material.SetTexture("_AlignmentRamp", usesAlignment ? combineVectorFieldsParams.alignmentRamp : Texture2D.whiteTexture);
			material.SetInt("_ScaleByFieldMagnitude", combineVectorFieldsParams.scaleByFieldMagnitude ? 1 : 0);
		} else {
			material.DisableKeyword("VF_MODULATION");
		}

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
			// CPU combine samples each child's CPU vectorField (via EvaluateWorldVector). The pull in RenderInternal
			// already re-rendered the child, but a GPU-backed child only fills its CPU copy when something needs it —
			// so force its readback here. (CPU-backed children have no renderTexture and are already current.)
			if (layer.component.renderTexture != null) layer.component.ReadIntoCPU(forceImmediate: true);

			var points = gridRenderer.GetPointsInWorldBounds(layer.component.GetBounds());
			foreach (var point in points) {
				Vector2 current = vectorField.GetValueAtGridPoint(point);

				var pointWorldPosition = gridRenderer.cellCenter.GridToWorldPoint(point);
				Vector2 incoming = transform.InverseTransformDirection(layer.component.EvaluateWorldVector(pointWorldPosition));

				// Pass the ramp only when it's not the identity, so an unmodulated layer skips the per-point evaluation.
				var ramp = IsIdentityRamp(layer.alignmentRamp) ? null : layer.alignmentRamp;
				vectorField.SetValueAtGridPoint(point, BlendVector(current, incoming, layer.strength, layer.blendMode, layer.components, ramp, layer.scaleByFieldMagnitude));
			}
		}
	}

	// A ramp does nothing when every key sits at 1 (the flat-1 default). Cheap conservative check — a curve that
	// bows between equal endpoints would be treated as identity, which only costs a skipped bake, never correctness.
	// Uses the indexer rather than .keys to avoid allocating a Keyframe[].
	static bool IsIdentityRamp(AnimationCurve ramp) {
		if (ramp == null || ramp.length == 0) return true;
		for (int i = 0; i < ramp.length; i++)
			if (!Mathf.Approximately(ramp[i].value, 1f)) return false;
		return true;
	}

	// The canonical per-vector blend, shared in spirit with BlendVectors() in CombineVectorFields.shader — keep the
	// two in sync. Magnitude and Direction are independent aspects of the incoming vector that compose, so
	// Magnitude | Direction (== All) takes both. All normalization is zero-safe (a zero vector normalizes to zero)
	// to avoid NaNs from the zero base / cancelling sums.
	public static Vector2 BlendVector(Vector2 current, Vector2 incoming, float strength, VectorFieldLayer.BlendMode blendMode, VectorFieldLayer.Component components, AnimationCurve alignmentRamp = null, bool scaleByFieldMagnitude = false) {
		bool hasMagnitude = (components & VectorFieldLayer.Component.Magnitude) != 0;
		bool hasDirection = (components & VectorFieldLayer.Component.Direction) != 0;
		if (!hasMagnitude && !hasDirection) return current;

		// Per-layer modulators that apply in every mode (mirror of the shader's VF_MODULATION path):
		// the alignment ramp scales effective strength by how aligned this layer is with the field beneath it, and
		// the field-magnitude coupling scales the incoming vector by the underlying flow speed.
		if (alignmentRamp != null) {
			float alignment = Vector2.Dot(SafeNormalize(current), SafeNormalize(incoming));
			strength *= alignmentRamp.Evaluate(Mathf.Clamp01(alignment * 0.5f + 0.5f));
		}
		if (scaleByFieldMagnitude) incoming *= current.magnitude;

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
