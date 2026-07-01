using System.Collections.Generic;
using UnityEngine;

// A continuous, smooth paint stroke into a DrawableVectorFieldComponent. Feed it world positions with To(); it sweeps
// a soft capsule along a centripetal-Catmull-Rom-smoothed path (no dabbing) and marks the touched region dirty.
//
// Created via field.BeginStroke(brush). Hold the returned instance and call To() each frame; call End() when done.
//
// Path smoothing:
//  - Centripetal Catmull-Rom (alpha = 0.5): passes through the points with no overshoot/cusps on sharp turns or
//    uneven point spacing (unlike the uniform variant).
//  - TipMode.Smoothed (default): draws up to the second-newest point, using the newest only as look-ahead for the
//    tangent (~1 point of lag, smoothest). End() flushes the final tail so nothing is lost.
//  - TipMode.Leading: draws all the way to the newest point with an extrapolated forward tangent (zero lag).
//
// Exact overlap (frame-rate independent, seam-free — every op):
//   A thick swept stroke re-covers the cells just behind its moving head on consecutive frames. Applying an op live
//   on each re-cover is frame-rate dependent for EVERY op — compounding ops (Add/Burn/…) double-apply, and even
//   set-style ops (Draw/Repel/…) drift toward their target because they blend by falloff weight. So instead of
//   applying live, each cell is applied EXACTLY ONCE, at the maximum coverage the stroke gave it, from the field
//   value captured just before the head first reached it. The painted result then depends only on the stroke
//   geometry, not on how many frames delivered it (identical at 30/60/144 fps) and not on how long the head lingers.
//
//   Rather than snapshotting the whole stroke bounds up front (unbounded for a long trail, and it would fight a
//   decay/sim draining the field), cells are held in a small "active" set only while within a brush radius of the
//   head: snapshot on first touch, re-applied at the running max coverage while active, then evicted once the head
//   moves past (their value is already final). So finalized cells are free to decay while the head stays fresh, and
//   the cost is bounded by the brush footprint regardless of stroke length.
//
// Future (see Brush/RUNTIME_PAINTING_SPEC.md): pool the per-stroke neighbour snapshot (only allocated for
// neighbour-reading ops like Smudge) across strokes.
public sealed class VectorFieldStroke {
    const float SubStepCells = 0.5f;   // spline sampling resolution along the path, in grid cells

    readonly DrawableVectorFieldComponent _field;
    readonly VectorFieldBrush _brush;
    readonly float _gridRadius;

    // Ring of the last 4 path points in GRID space (4 is enough for one centripetal Catmull-Rom span). _head indexes
    // the newest; Pt(k) reads the point k steps before the newest, clamped to the oldest we still hold.
    readonly Vector2[] _ring = new Vector2[4];
    int _head = -1, _n;

    // Per-span scratch (reused so painting doesn't allocate after the first stroke; single-threaded use).
    readonly List<VectorFieldBrushCell> _cells = new List<VectorFieldBrushCell>();
    readonly Dictionary<Point, int> _index = new Dictionary<Point, int>();   // cell -> index in _cells (per span)

    // Exact-overlap state. One entry per cell currently within a brush radius of the head; carries the max coverage
    // seen so far, the brush-sample direction (radial: the tangent; map: the emitter/cookie direction) and the stroke
    // direction at that max, the radial-op centre, and the field value captured just before the head arrived.
    struct Active {
        public float maxCoverage; public Vector2 brushDir; public Vector2 strokeDir; public Vector2 center;
        public Vector2 snapshot;
    }
    readonly Dictionary<Point, Active> _active = new Dictionary<Point, Active>();
    readonly List<Point> _evict = new List<Point>();

    // Pre-stroke field clone, for neighbour-reading ops (Smudge) so their upstream samples are stable and
    // order-independent. Only allocated when the op needs it.
    Vector2Map _source;
    bool _sourceReady;

    // End point (grid space) of the span just rendered — the next span starts here, so it's the frontier eviction
    // measures from (NOT the newest fed point, which is one span ahead in Smoothed mode).
    Vector2 _spanEnd;

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

    // Draw whatever tail hasn't been rendered yet, then reset. In Smoothed mode the newest span is still pending
    // look-ahead, so we flush it here (this is also what makes a 2-point PaintLine draw its single span). Active cells
    // already hold their final value (applied when last touched), so finishing just clears state.
    public void End() {
        if (_brush.IsValid && _brush.tipMode == TipMode.Smoothed && _n >= 2)
            RenderSpanToNewest();
        _active.Clear();
        _source = null;
        _sourceReady = false;
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

    // Sweep the centripetal Catmull-Rom segment p1->p2 (p0/p3 set the tangents) as a chain of soft capsules, then
    // fold the resulting coverage into the exact-overlap active set and apply.
    void RenderSpan(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
        _cells.Clear();
        _index.Clear();
        _spanEnd = p2;   // the next span starts here; eviction keeps cells near it

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SubStepCells));
        Vector2 prev = p1;
        for (int k = 1; k <= steps; k++) {
            Vector2 cur = CentripetalCatmullRom(p0, p1, p2, p3, k / (float)steps);
            RasterizeCapsule(prev, cur);
            prev = cur;
        }

        CommitSpan();
    }

    // Merge this span's coverage into the active set and apply each touched cell exactly once, at its running max
    // coverage, from its pre-touch snapshot. Then evict cells the head has moved beyond so the set stays bounded.
    void CommitSpan() {
        if (_cells.Count == 0) return;
        var field = _field.PaintField;
        var op = _brush.op;

        // Neighbour-reading ops need a stable pre-stroke snapshot to sample; capture it once, before any painting.
        if (op.NeedsSnapshot && !_sourceReady) {
            _source = new Vector2Map(field.size, (Vector2[])field.values.Clone());
            _sourceReady = true;
        }
        Vector2Map source = op.NeedsSnapshot ? _source : field;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < _cells.Count; i++) {
            var c = _cells[i];
            Point p = c.gridPoint;
            float w = c.brushForce.magnitude;      // this span's coverage at the cell
            Vector2 brushDir = w > 1e-6f ? c.brushForce / w : c.strokeForce;   // emitter/cookie dir (unit)
            Vector2 strokeDir = c.strokeForce;
            Vector2 center = c.brushCenter;

            if (_active.TryGetValue(p, out Active a)) {
                if (w > a.maxCoverage) {
                    a.maxCoverage = w; a.brushDir = brushDir; a.strokeDir = strokeDir; a.center = center; _active[p] = a;
                }
            } else {
                a = new Active { maxCoverage = w, brushDir = brushDir, strokeDir = strokeDir, center = center,
                                 snapshot = field.GetValueAtGridPoint(p) };   // value just before the head arrived
                _active[p] = a;
            }

            // brushForce/finalForce carry the max coverage as magnitude (ctx.Weight); the op runs once from snapshot.
            var ctx = new BrushApplyContext(a.snapshot, a.brushDir * a.maxCoverage, a.brushDir * a.maxCoverage,
                                            a.strokeDir, _brush.pressure, p, a.center, source);
            field.SetValueAtGridPoint(p, op.Apply(ctx));

            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        _field.MarkRegionDirty(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));

        EvictBehindHead();
    }

    // Drop active cells more than a brush radius from the head: the head won't re-cover them, so their value is final
    // (already written). Keeps the active set ~brush-footprint sized regardless of stroke length. If the path later
    // curves back over an evicted cell it's re-snapshotted as a genuinely separate touch.
    void EvictBehindHead() {
        float er = _gridRadius + 1f;   // one cell of margin so a cell isn't dropped right as the next span reaches it
        float erSqr = er * er;
        _evict.Clear();
        foreach (var kv in _active) {
            if (_index.ContainsKey(kv.Key)) continue;   // touched this span — still under the head
            float dx = kv.Key.x - _spanEnd.x, dy = kv.Key.y - _spanEnd.y;
            if (dx * dx + dy * dy > erSqr) _evict.Add(kv.Key);
        }
        for (int i = 0; i < _evict.Count; i++) _active.Remove(_evict[i]);
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
        bool isMap = _brush.shape.IsMap;
        // Brush local frame a map shape is sampled in. FollowStroke: +Y (forward) = the stroke tangent, so the
        // emitter direction rotates with the stroke. FixedAngle: the world frame, so the map's baked emitter
        // direction is used as-is. +X (right) = forward rotated -90.
        Vector2 forward = _brush.directionMode == VectorFieldDirectionMode.FixedAngle ? Vector2.up : tangent;
        Vector2 right = new Vector2(forward.y, -forward.x);

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                Vector2 p = new Vector2(x, y);
                float t = abLenSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr) : 0f;
                Vector2 nearest = a + ab * t;
                Vector2 offset = p - nearest;

                if (isMap) {
                    Vector2 local = new Vector2(Vector2.Dot(offset, right), Vector2.Dot(offset, forward)) * invR;
                    Vector2 sample = _brush.shape.Sample2D(local);   // local-frame emitter vector (mag = weight)
                    float mag = sample.magnitude;
                    if (mag <= 0f) continue;
                    Vector2 world = sample.x * right + sample.y * forward;   // emitter dir in world
                    Vector2 brushDir = world / mag;
                    // FollowStroke paints along the drag (Draw follows the tangent); FixedAngle paints the emitter dir.
                    Vector2 strokeDir = _brush.directionMode == VectorFieldDirectionMode.FixedAngle ? brushDir : tangent;
                    Accumulate(new Point(x, y), mag, brushDir, strokeDir, nearest);
                } else {
                    float dist = offset.magnitude;
                    if (dist > _gridRadius) continue;
                    float w = _brush.shape.Weight(dist * invR);
                    if (w <= 0f) continue;
                    Accumulate(new Point(x, y), w, tangent, tangent, nearest);
                }
            }
        }
    }

    void Accumulate(Point gridPoint, float weight, Vector2 brushDir, Vector2 strokeDir, Vector2 center) {
        var cell = new VectorFieldBrushCell {
            gridPoint = gridPoint,
            brushForce = brushDir * weight,         // magnitude = weight (the op's ctx.Weight)
            finalForce = brushDir * weight,
            strokeForce = strokeDir,                // direction the stroke is heading
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
