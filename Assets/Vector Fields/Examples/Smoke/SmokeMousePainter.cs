using UnityEngine;

// DEMO input: paint coloured smoke with the mouse at runtime. Raycasts the cursor onto the smoke field's plane and
// drives a ColorStroke — the SAME smooth, frame-rate-independent stroke core the editor's vector-field tool uses, just
// fed from game-view mouse input instead of scene-view input. Hold and drag to lay a trail; the sim then advects it
// along the velocity field.
//
// Setup: put this on (or point it at) a SmokeSimulationComponent. Press play and drag in the view.
[RequireComponent(typeof(SmokeSimulationComponent))]
public class SmokeMousePainter : MonoBehaviour {
    public Camera cam;
    [ColorUsage(true, true)] public Color color = new Color(0.3f, 0.8f, 1f, 1f);
    [Tooltip("Pick a fresh random hue at the start of each stroke.")] public bool randomHuePerStroke = true;
    [Tooltip("Brush radius in world units.")] public float worldRadius = 1.5f;
    [Range(0f, 1f)] public float softness = 0.6f;
    [Range(0f, 1f)] public float strength = 1f;

    SmokeSimulationComponent _smoke;
    ColorStroke _stroke;

    void Awake() {
        _smoke = GetComponent<SmokeSimulationComponent>();
        if (cam == null) cam = Camera.main;
    }

    void Update() {
        if (_smoke == null || cam == null) return;

        if (Input.GetMouseButtonDown(0) && TryGetPoint(out Vector3 downPoint)) {
            var paintColor = randomHuePerStroke ? RandomHue(color.a) : color;
            var brush = new PaintBrush<Color>(VectorFieldBrushShape.Radial(softness),
                                              new SmokeDrawOp(paintColor), worldRadius, strength, TipMode.Leading);
            _stroke = _smoke.BeginStroke(brush);
            _stroke.To(downPoint);
        } else if (Input.GetMouseButton(0) && _stroke != null && TryGetPoint(out Vector3 dragPoint)) {
            _stroke.To(dragPoint);
        } else if (Input.GetMouseButtonUp(0) && _stroke != null) {
            _stroke.End();
            _stroke = null;
        }
    }

    void OnDisable() {
        _stroke?.End();
        _stroke = null;
    }

    bool TryGetPoint(out Vector3 point) {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        return _smoke.gridRenderer.floorPlane.TryGetHitPoint(ray, out point);
    }

    static Color RandomHue(float alpha) {
        var c = Color.HSVToRGB(Random.value, 0.85f, 1f);
        c.a = alpha;
        return c;
    }
}
