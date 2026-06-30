using System.Collections.Generic;
using UnityEngine;

// A continuous, smooth paint stroke into a DrawableVectorFieldComponent. Feed it world positions with To(); it sweeps
// a soft capsule along a Catmull-Rom-smoothed path (no dabbing) and marks the touched region dirty. Frame-rate
// independent: the painted band depends on the geometry between points, not on how many frames delivered them.
//
// Created via field.BeginStroke(brush). Hold the returned instance and call To() each frame; call End() when done.
//
// First-cut simplifications (vs Brush/RUNTIME_PAINTING_SPEC.md), all flagged for follow-up:
//  - Uniform Catmull-Rom (not centripetal): can overshoot slightly on very uneven point spacing.
//  - Zero-lag tip only (draws up to the newest point with an extrapolated forward tangent); no Smoothed/Leading toggle.
//  - "Cheap" overlap handling: each span is applied live. Correct for set-style ops (Draw/Clamp/Normalize/Repel/...);
//    for compounding ops (Add/Smudge/Burn/Dodge/Erase) joins between frames may double-apply. The exact
//    snapshot+coverage path (op.CompoundsOnReapply / NeedsSnapshot) is still TODO.
public sealed class VectorFieldStroke {
    const float SubStepCells = 0.5f;   // spline sampling resolution along the path, in grid cells

    readonly DrawableVectorFieldComponent _field;
    readonly VectorFieldBrush _brush;
    readonly float _gridRadius;

    // Last few path points in GRID space (newest last). 3 is enough for the uniform Catmull-Rom span we draw.
    readonly Vector2[] _pts = new Vector2[3];
    int _count;

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
        if (_count < 2) return;

        Vector2 pNew = _pts[_count - 1];
        Vector2 pPrev = _pts[_count - 2];
        Vector2 pPrevPrev = _count >= 3 ? _pts[_count - 3] : pPrev;
        Vector2 pNext = pNew + (pNew - pPrev);   // extrapolated forward tangent (zero-lag tip)

        RenderSpan(pPrevPrev, pPrev, pNew, pNext);
    }

    public void End() { _count = 0; }   // (buffers are reusable; pooling them is a future optimization)

    // --- internals --------------------------------------------------------------------------------------------------

    void Push(Vector2 gridPoint) {
        if (_count < _pts.Length) { _pts[_count++] = gridPoint; return; }
        _pts[0] = _pts[1];
        _pts[1] = _pts[2];
        _pts[2] = gridPoint;
    }

    // Sweep the Catmull-Rom segment p1->p2 (p0/p3 set the tangents) as a chain of soft capsules.
    void RenderSpan(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
        _cells.Clear();
        _index.Clear();

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SubStepCells));
        Vector2 prev = p1;
        for (int k = 1; k <= steps; k++) {
            Vector2 cur = CatmullRom(p0, p1, p2, p3, k / (float)steps);
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

    // Uniform Catmull-Rom, evaluating the p1->p2 segment at t in [0,1]. Robust (no divisions); mild overshoot on
    // uneven spacing is acceptable for brush paths.
    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1)
                       + (-p0 + p2) * t
                       + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                       + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
