using UnityEngine;

// Stamps a single procedural force emitter (directional or spot/vortex) into the field. The actual generation
// lives in the code-callable VectorFieldBrushTextureCreator.Dispatch, so this component is just the editor-facing
// wrapper: hold the settings, detect changes, and dispatch into the base component's render texture.
//
// The cookie/falloff lives on the base component and is applied uniformly to every field type (see
// VectorFieldComponent.Render), so the brush itself is dispatched unmasked here.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Stamp Vector Field")]
public class StampVectorFieldComponent : VectorFieldComponent {

	public VectorFieldBrushSettings brushSettingsParams = new VectorFieldBrushSettings();

	// A stamped emitter reads best with a soft edge, so a new one defaults to a radial falloff mask (the base
	// component defaults to no mask). Reset runs when the component is first added or explicitly reset.
	void Reset() {
		cookie.mode = VectorFieldCookieSource.Mode.Falloff;
	}

	protected override void RenderInternal() {
		EnsureHasValidRenderTexture();
		// Unit strength: the base applies `magnitude` (and cookie) as an output transform in Render(), so passing
		// `magnitude` here would double-apply it.
		VectorFieldBrushTextureCreator.Dispatch(renderTexture, GridSize, 1f, brushSettingsParams, null);
	}

	// Re-render when the brush settings change (the base handles magnitude/grid/cookie). A content hash catches any
	// field change without enumerating them or allocating a JSON string every tick, and reflects in-place edits.
	protected override void CollectParameters(ref System.HashCode hash) {
		base.CollectParameters(ref hash);
		hash.Add(brushSettingsParams != null ? brushSettingsParams.GetContentHash() : 0);
	}
}
