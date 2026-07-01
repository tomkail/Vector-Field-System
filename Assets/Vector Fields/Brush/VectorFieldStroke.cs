using System.Collections.Generic;
using UnityEngine;

// A continuous, smooth paint stroke into a DrawableVectorFieldComponent. Feed it world positions with To(); it sweeps
// a soft capsule along a centripetal-Catmull-Rom-smoothed path (no dabbing) and marks the touched region dirty.
// Frame-rate independent for set-style ops: each point-to-point span is rendered exactly once, so the painted band
// depends on the geometry between points, not on how many frames delivered them.
//
// Created via field.BeginStroke(brush). Hold the returned instance and call To() each frame; call End() when done.
//
// Path smoothing:
//  - Centripetal Catmull-Rom (alpha = 0.5): passes through the points with no overshoot/cusps on sharp turns or
//    uneven spacing (unlike the uniform variant).
//  - TipMode.Smoothed (default): draws up to the second-newest point, using the newest only as look-ahead for the
//    tangent (~1 point of lag, smoothest). End() flushes the final tail so nothing is lost.
//  - TipMode.Leading: draws all the way to the newest point with an extrapolated forward tangent (zero lag).
//
// Remaining first-cut simplification (flagged for follow-up, see Brush/RUNTIME_PAINTING_SPEC.md):
//  - "Cheap" overlap handling: each span is applied live. Correct for set-style ops (Draw/Clamp/Normalize/Repel/...);
//    for compounding ops (Add/Smudge/Burn/Dodge/Erase) joins between spans may double-apply. The exact
//    snapshot+coverage path (op.CompoundsOnReapply / NeedsSnapshot) is still TODO.
public sealed class VectorFieldStroke {
    const float SubStepCells = 0.5f;   // spline sampling resolution along the path, in grid cells

    readonly DrawableVectorFieldComponent _field;
    readonly VectorFieldBrush _brush;
    readonly float _gridRadius;

    // Ring of the last 4 path points in GRID space (4 is enough for one centripetal Catmull-Rom span). _head indexes
    // the newest; Pt(k) reads the point k steps before the newest, clamped to the oldest we still hold.
    readonly Vector2[] _ring = new Vector2[4];
    int _head = -1, _n;

    // Reused per-span so painting doesn't allocate after the first stroke. (Single-threaded use.)
    readonly List<VectorFieldBrushCell> _cells = new List<VectorFieldBrushCell>();
    readonly Dictionary<Point, int> _index = new Dictionary<Point, int>();   // cell -> index in _cells (per span)

    public VectorFieldStroke(DrawableVectorFieldComponent field, in VectorFieldBrush brush) {
        _field = field;
        _brush = brush;
        var cc = field.gridRenderer.cellCenter;
        _gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(brush.size, 0f, 0f)).magnitude);
    }

    public void To(Vector3 worldPosition) {
        if (!_brush.IsValid) return;
        Push(_field.gridRenderer.cellCenter.WorldToGridPosition(worldPosition));
        if (_n < 2) return;

        if (_brush.tipMode == TipMode.Leading)
            RenderSpanToNewest();                 // draw the freshest span [prev -> newest], zero lag
        else if (_n >= 3)
            RenderSmoothedSpan();                 // draw [prev2 -> prev]; newest is look-ahead only
    }

    // Draw whatever tail hasn't been rendered yet. In Smoothed mode the newest span is still pending look-ahead, so we
    // flush it here (this is also what makes a 2-point PaintLine draw its single span). Leading has nothing left.
    public void End() {
        if (_brush.IsValid && _brush.tipMode == TipMode.Smoothed && _n >= 2)
            RenderSpanToNewest();
        _head = -1;
        _n = 0;
    }

    // --- internals --------------------------------------------------------------------------------------------------

    void Push(Vector2 gridPoint) {
        _head = (_head + 1) & 3;
        _ring[_head] = gridPoint;
        if (_n < 4) _n++;
    }

    // The path point k steps before the newest (0 = newest), clamped to the oldest point we still hold. The ring is
    // sized to a power of two, so two's-complement AND with 3 gives the correct non-negative index even when
    // (_head - k) is negative.
    Vector2 Pt(int k) {
        if (k >= _n) k = _n - 1;
        return _ring[(_head - k) & 3];
    }

    // Zero-lag span: sweep [prev -> newest], extrapolating the forward tangent past the newest point.
    void RenderSpanToNewest() {
        Vector2 p1 = Pt(1), p2 = Pt(0);
        Vector2 p0 = Pt(2);
        Vector2 p3 = p2 + (p2 - p1);   // extrapolated look-ahead
        RenderSpan(p0, p1, p2, p3);
    }

    // Lagging span: sweep [prev2 -> prev], using the newest point as the real look-ahead tangent.
    void RenderSmoothedSpan() {
        RenderSpan(Pt(3), Pt(2), Pt(1), Pt(0));
    }

    // Sweep the centripetal Catmull-Rom segment p1->p2 (p0/p3 set the tangents) as a chain of soft capsules.
    void RenderSpan(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
        _cells.Clear();
        _index.Clear();

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SubStepCells));
        Vector2 prev = p1;
        for (int k = 1; k <= steps; k++) {
            Vector2 cur = CentripetalCatmullRom(p0, p1, p2, p3, k / (float)steps);
            RasterizeCapsule(prev, cur);
            prev = cur;
        }

        if (_cells.Count > 0 &&
            VectorFieldBrushKernel.Apply(_field.PaintField, _cells, _brush.pressure, _brush.op, out RectInt dirty))
            _field.MarkRegionDirty(dirty);
    }

    // Accumulate every cell within _gridRadius of segment a->b, keeping the max weight per cell across this span.
    void RasterizeCapsule(Vector2 a, Vector2 b) {
        var size = _field.PaintField.size;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - _gridRadius));
        int maxX = Mathf.Min(size.x - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + _gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - _gridRadius));
        int maxY = Mathf.Min(size.y - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + _gridRadius));
        float invR = 1f / _gridRadius;

        Vector2 ab = b - a;
        float abLenSqr = ab.sqrMagnitude;
        Vector2 tangent = abLenSqr > 1e-8f ? ab.normalized : Vector2.up;

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                Vector2 p = new Vector2(x, y);
                float t = abLenSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr) : 0f;
                Vector2 nearest = a + ab * t;
                float dist = Vector2.Distance(p, nearest);
                if (dist > _gridRadius) continue;
                float w = _brush.shape.Weight(dist * invR);
                if (w <= 0f) continue;
                Accumulate(new Point(x, y), w, tangent, nearest);
            }
        }
    }

    void Accumulate(Point gridPoint, float weight, Vector2 tangent, Vector2 center) {
        var cell = new VectorFieldBrushCell {
            gridPoint = gridPoint,
            brushForce = tangent * weight,          // magnitude = weight (the op's ctx.Weight)
            finalForce = tangent * weight,
            strokeForce = tangent,                  // direction the stroke is heading
            brushCenter = center,
        };
        if (_index.TryGetValue(gridPoint, out int i)) {
            if (weight > _cells[i].brushForce.magnitude) _cells[i] = cell;   // keep the strongest touch this span
        } else {
            _index[gridPoint] = _cells.Count;
            _cells.Add(cell);
        }
    }

    // Centripetal Catmull-Rom (alpha = 0.5), evaluating the p1->p2 segment at t in [0,1] via the Barry-Goldman
    // pyramid. Knot spacing uses sqrt(chord length) so the curve passes through the points without the overshoot the
    // uniform variant shows on uneven spacing / sharp turns. Each interval is floored to eps so coincident or
    // duplicated control points (stroke start/end) can't divide by zero.
    static Vector2 CentripetalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
        const float eps = 1e-4f;
        float t0 = 0f;
        float t1 = t0 + Mathf.Max(eps, Mathf.Sqrt(Vector2.Distance(p0, p1)));
        float t2 = t1 + Mathf.Max(eps, Mathf.Sqrt(Vector2.Distance(p1, p2)));
        float t3 = t2 + Mathf.Max(eps, Mathf.Sqrt(Vector2.Distance(p2, p3)));

        float tt = Mathf.Lerp(t1, t2, t);
        Vector2 a1 = Vector2.LerpUnclamped(p0, p1, (tt - t0) / (t1 - t0));
        Vector2 a2 = Vector2.LerpUnclamped(p1, p2, (tt - t1) / (t2 - t1));
        Vector2 a3 = Vector2.LerpUnclamped(p2, p3, (tt - t2) / (t3 - t2));
        Vector2 b1 = Vector2.LerpUnclamped(a1, a2, (tt - t0) / (t2 - t0));
        Vector2 b2 = Vector2.LerpUnclamped(a2, a3, (tt - t1) / (t3 - t1));
        return Vector2.LerpUnclamped(b1, b2, (tt - t1) / (t2 - t1));
    }
}
