using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Editor-facing wrapper around the code-callable VectorFieldCombiner: collects child vector fields as layers and
// blends them on the GPU. The actual blend (per-layer blit, transform projection, ping-pong) lives in
// VectorFieldCombiner, so the same combine can be driven from code without a Group component.
public class GroupVectorFieldComponent : VectorFieldComponent {
	[System.Serializable]
	public class VectorFieldLayer {
		public VectorFieldComponent component;

		[Range(0, 1)]
		public float strength = 1;

		public VectorFieldCombiner.BlendMode blendMode = VectorFieldCombiner.BlendMode.Add;

		// The two fields below are per-layer modulators that apply in EVERY blend mode (they're orthogonal to the
		// mode itself). Their defaults are no-ops, so a plain Add / Blend layer behaves exactly as before.

		// Scales this layer's effective strength by how aligned it is with the field beneath it:
		// x = (dot(currentDir, incomingDir) + 1) / 2, so 0 = fully opposed, 0.5 = perpendicular, 1 = fully aligned.
		// Default is a flat 1 (no effect). E.g. a 0->1 ramp applies the layer only where it agrees with the flow.
		[CurveRange(0, 0, 1, 1)] public AnimationCurve alignmentRamp = AnimationCurve.Constant(0, 1, 1);
		// Multiplies the incoming vector by the underlying field's magnitude before blending, so this layer only acts
		// where there's already flow and scales with its speed. This is the turbulence coupling: an Add layer with a
		// 0->1 alignmentRamp and this enabled reproduces the old flow-modulated turbulence.
		public bool scaleByFieldMagnitude = false;
		// Cached GPU bake of alignmentRamp, reused across renders (rebaked when the curve changes).
		[NonSerialized] public Texture2D alignmentRampTexture;

		[EnumFlagsButtonGroup] public VectorFieldCombiner.Component components = VectorFieldCombiner.Component.All;
	}

	public List<VectorFieldLayer> layers = new List<VectorFieldLayer>();
	IEnumerable<VectorFieldComponent> childComponents => this.GetComponentsX(ComponentX.ComponentSearchParams<VectorFieldComponent>.AllDescendentsExcludingSelf(false));

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

		// Build the layer inputs for the combiner: each visible child's render texture + its transform + blend
		// settings. The alignment ramp is baked here (the component owns the curve + its cached texture) and passed
		// in already-baked, keeping the combiner free of AnimationCurve/MonoBehaviour concerns.
		var inputs = new List<VectorFieldCombiner.Layer>(layers.Count);
		for (int i = 0; i < layers.Count; i++) {
			var layer = layers[i];
			if (layer == null || layer.component == null || layer.component.renderTexture == null) continue;
			if (layer.components == VectorFieldCombiner.Component.None) continue;

			// Both modulators default to no-ops; only do the work (and only bake the ramp) when a layer actually uses
			// one. The shader's modulation path is keyword-gated, so an unmodulated layer pays nothing for either.
			bool usesAlignment = !IsIdentityRamp(layer.alignmentRamp);
			Texture2D alignmentRamp = usesAlignment
				? VectorFieldUtils.CreateRampTextureFromAnimationCurve(layer.alignmentRamp, 256, ref layer.alignmentRampTexture)
				: null;

			inputs.Add(new VectorFieldCombiner.Layer {
				field = layer.component.renderTexture,
				localToWorldMatrix = layer.component.transform.localToWorldMatrix,
				strength = layer.strength,
				blendMode = layer.blendMode,
				components = layer.components,
				alignmentRamp = alignmentRamp,
				scaleByFieldMagnitude = layer.scaleByFieldMagnitude
			});
		}

		EnsureHasValidRenderTexture();
		VectorFieldCombiner.Combine(renderTexture, new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y), transform.localToWorldMatrix, inputs);
	}


	// Re-blend when the set of direct child layers changes. Per-child value/transform changes already propagate
	// here via VectorFieldComponent.SetDirty, and the group's own transform is tracked by base.Update.
	void OnTransformChildrenChanged() => SetDirty();

	// Re-blend on changes to any layer's settings (strength / blend mode / component mask / the component
	// referenced). Inspector edits also come through OnValidate; this covers runtime mutation.
	int lastLayersHash;
	protected override bool ParametersChanged() {
		bool changed = base.ParametersChanged();
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

	// A ramp does nothing when every key sits at 1 (the flat-1 default). Cheap conservative check — a curve that
	// bows between equal endpoints would be treated as identity, which only costs a skipped bake, never correctness.
	// Uses the indexer rather than .keys to avoid allocating a Keyframe[].
	static bool IsIdentityRamp(AnimationCurve ramp) {
		if (ramp == null || ramp.length == 0) return true;
		for (int i = 0; i < ramp.length; i++)
			if (!Mathf.Approximately(ramp[i].value, 1f)) return false;
		return true;
	}
}
