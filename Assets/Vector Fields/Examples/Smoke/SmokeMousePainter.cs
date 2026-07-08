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
    PaintBrush<Color> _brush;
    Vector3 _prevPoint;

    void Awake() {
        _smoke = GetComponent<SmokeSimulationComponent>();
        if (cam == null) cam = Camera.main;
    }

    void Update() {
        if (_smoke == null || cam == null) return;

        if (Input.GetMouseButtonDown(0) && TryGetPoint(out Vector3 downPoint)) {
            var paintColor = randomHuePerStroke ? RandomHue(color.a) : color;
            // Leading (zero lag): the head follows the cursor with no one-sample delay. The max deposit (not the tip
            // mode) controls injection "stepping", so there's no reason to trade away responsiveness here.
            _brush = new PaintBrush<Color>(BrushShape.Radial(softness),
                                           new SmokeDrawOp(paintColor), worldRadius, strength, TipMode.Leading);
            _stroke = _smoke.BeginStroke(_brush);
            _stroke.To(downPoint);
            _smoke.Stamp(_brush, downPoint);
            _prevPoint = downPoint;
        } else if (Input.GetMouseButton(0) && _stroke != null && TryGetPoint(out Vector3 dragPoint)) {
            // The stroke sweeps a smooth trail between mouse samples. Only stamp when the cursor is (near) stationary —
            // that's the case the arc-length stroke can't emit for (no drag = no coverage). While moving, the stroke
            // already covers the area, so skipping the extra full-radius stamp pass saves real work at high res.
            _stroke.To(dragPoint);
            float moveThreshold = worldRadius * 0.5f;
            if ((dragPoint - _prevPoint).sqrMagnitude < moveThreshold * moveThreshold)
                _smoke.Stamp(_brush, dragPoint);
            _prevPoint = dragPoint;
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
        if (_smoke.grid.FloorPlane.Raycast(ray, out float enter)) {
            point = ray.GetPoint(enter);
            return true;
        }
        point = default;
        return false;
    }

    static Color RandomHue(float alpha) {
        var c = Color.HSVToRGB(Random.value, 0.85f, 1f);
        c.a = alpha;
        return c;
    }
}
