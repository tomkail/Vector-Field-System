using UnityEngine;
using UnityEngine.Rendering;

// Runtime arrow visualisation of a vector field. Wraps the same VectorFieldDebugRenderer core that the Scene-view debug
// overlay uses (which is already pure Graphics.RenderMeshIndirect — no editor APIs), but drives it from a live
// component instead of an editor hook, so the arrow view works in play mode (and, being [ExecuteAlways], in the Game
// view in edit mode too). It exposes the same settings the Scene-view renderer has: the full VectorFieldDebugAppearance
// (glyph, colour mode, colours, max magnitude, opacity) and the variable-resolution density controls.
//
// It draws only to Game cameras, so it never double-draws with the editor's Scene-view overlay (which owns the Scene
// view and draws the selected field). One indirect draw is issued per rendering camera, at the correct point in both
// the Built-in pipeline (Camera.onPreCull) and any SRP/URP (RenderPipelineManager.beginCameraRendering).
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Arrow Renderer")]
public class VectorFieldArrowRenderer : MonoBehaviour {
	[SerializeField] VectorFieldComponent _vectorFieldComponent;
	public VectorFieldComponent vectorFieldComponent {
		get => _vectorFieldComponent;
		set => _vectorFieldComponent = value;
	}

	// When on (the default) arrows are drawn at the field's own placement, exactly like the Scene-view renderer. Turn
	// it off to draw them relative to THIS object's transform instead — the grid layout is preserved but mapped into
	// this GameObject's space, so you can offset / rotate / scale the arrow overlay independently of the field (e.g. a
	// HUD-style readout, or the same field visualised in two places). The arrow shader is unlit + ZTest-Always, so
	// following the field never z-fights whatever else is drawn there.
	[SerializeField] bool matchFieldTransform = true;

	// Arrow look — the same data the Scene-view renderer reads (edited there under Project Settings > Vector Fields).
	// Leave arrowTexture empty to fall back to the built-in glyph; note that glyph currently lives under an Editor/
	// folder, so at runtime you'll want to assign your own arrow texture here (see the renderer's Resources.Load note).
	[SerializeField] VectorFieldDebugAppearance appearance = new VectorFieldDebugAppearance();

	// Density — the same controls the Scene-view debug overlay exposes (there they're per-user EditorPrefs; here they're
	// serialized on the component so they travel with the scene).
	[Tooltip("Decimate the arrow grid so on-screen spacing stays roughly constant as the camera moves. Off = one arrow per field cell.")]
	[SerializeField] bool variableResolution = true;
	[Tooltip("Desired screen-space gap between arrows, in pixels (variable resolution only).")]
	[Range(8f, 128f)] [SerializeField] float targetSpacingPixels = 36f;
	[Tooltip("Upper bound on the number of arrows along the long axis (variable resolution only).")]
	[Range(8, 256)] [SerializeField] int maxArrows = 64;

	VectorFieldDebugRenderer debugRenderer;

	void OnEnable() {
		Camera.onPreCull += DrawForCamera;                                     // Built-in RP (no-op under an SRP)
		RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;  // URP/HDRP (no-op under Built-in)
	}

	void OnDisable() {
		Camera.onPreCull -= DrawForCamera;
		RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
		debugRenderer?.Dispose();
		debugRenderer = null;
	}

	void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) => DrawForCamera(camera);

	// Issue the arrow draw for one camera. Skip everything but Game cameras: reflection/preview cameras don't need it,
	// and the Scene view is already served by the editor overlay — drawing there too would double up.
	void DrawForCamera(Camera camera) {
		if (_vectorFieldComponent == null || camera == null || camera.cameraType != CameraType.Game) return;
		debugRenderer ??= new VectorFieldDebugRenderer();
		debugRenderer.Draw(_vectorFieldComponent, camera, appearance, variableResolution, targetSpacingPixels, maxArrows,
			matchFieldTransform ? (Matrix4x4?)null : GridToThisTransform());
	}

	// Map the field's grid layout into this object's transform instead of the field's: strip the field's own world
	// transform off its grid->world matrix (leaving grid->field-local), then re-anchor that to our transform. Setting
	// our transform equal to the field's reproduces the field's placement exactly; from there you can offset it.
	Matrix4x4 GridToThisTransform() {
		var gridToFieldLocal = _vectorFieldComponent.transform.worldToLocalMatrix * _vectorFieldComponent.GridToWorldMatrix;
		return transform.localToWorldMatrix * gridToFieldLocal;
	}
}
