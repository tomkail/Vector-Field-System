using UnityEngine;

// Stamps a single procedural force emitter (directional or spot/vortex) into the field. The actual generation
// lives in the code-callable VectorFieldBrushTextureCreator.Dispatch, so this component is just the editor-facing
// wrapper: hold the settings, detect changes, and dispatch into the base component's render texture.
//
// The cookie/falloff lives on the base component and is applied uniformly to every field type (see
// VectorFieldComponent.Render), so the brush itself is dispatched unmasked here.
[ExecuteAlways]
public class StampVectorFieldComponent : VectorFieldComponent {

	public VectorFieldBrushSettings brushSettingsParams = new VectorFieldBrushSettings();

	protected override void RenderInternal() {
		EnsureHasValidRenderTexture();
		var gridSize = new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y);
		VectorFieldBrushTextureCreator.Dispatch(renderTexture, gridSize, magnitude, brushSettingsParams, null);
	}

	// Re-render when the brush settings change (the base handles magnitude/grid/cookie). Comparing the serialized
	// JSON catches any field change without enumerating them, and reflects in-place edits to the settings object.
	string lastBrushSettingsJson;
	protected override bool ParametersChanged() {
		bool changed = base.ParametersChanged();
		string brushJson = brushSettingsParams != null ? JsonUtility.ToJson(brushSettingsParams) : null;
		if (lastBrushSettingsJson != brushJson) { lastBrushSettingsJson = brushJson; changed = true; }
		return changed;
	}
}
