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
// Coverage & overlap (frame-rate independent, seam-free — every op):
//   Coverage accumulates by ARC LENGTH: a cell builds up as ∫ weight · ds while the brush sweeps across it, so the
//   effect ramps with drag DISTANCE (a cell reaches full after the brush sweeps ~its own width across it) — not
//   instantly, and not with dwell time. Because that integral is geometric it's identical at any frame rate and
//   holding still adds nothing (no arc travelled). Each cell's op is applied ONCE from the field value captured just
//   before the head first reached it, at its accumulated coverage — so re-covering the band behind the head on
//   consecutive frames can't double-apply or drift, and joins between spans are seam-free.
//
//   Rather than snapshotting the whole stroke bounds up front (unbounded for a long trail, and it would fight a
//   decay/sim draining the field), cells are held in a small "active" set only while within a brush radius of the
//   head: snapshot on first touch, re-applied as coverage accumulates while active, then evicted once the head moves
//   past (their value is final). So finalized cells are free to decay while the head stays fresh, and the cost is
//   bounded by the brush footprint regardless of stroke length.
//
// Future (see Brush/RUNTIME_PAINTING_SPEC.md): pool the per-stroke neighbour snapshot (only allocated for
// neighbour-reading ops like Smudge) across strokes.
public sealed class VectorFieldStroke {
    const float SubStepCells = 0.5f;   // spline sampling resolution along the path, in grid cells
    // A new point closer than this to the last one is coalesced (not rendered yet): a span shorter than this has no
    // reliable direction, so rendering it would paint the start cells in an arbitrary (fallback) direction before the
    // drag direction is known. Waiting for real movement is also what the old tool did (it stepped only after ~a cell).
    const float MinStepCells = 0.5f;

    // Idle strokes are pooled and reused (rent in BeginStroke, return in End) so many short-lived strokes — e.g. a
    // per-frame PaintLine — don't churn GC: the collections below are allocated once per instance and reused. Capped
    // so a burst of simultaneous End()s can't retain instances forever.
    static readonly Stack<VectorFieldStroke> s_pool = new Stack<VectorFieldStroke>();
    const int PoolCapacity = 32;

    DrawableVectorFieldComponent _field;
    VectorFieldBrush _brush;
    float _gridRadius;
    // Coverage added per unit arc-length swept at full weight (~1/radius), so effect builds up with drag distance and
    // a cell reaches full coverage after the brush sweeps roughly its own width across it — the old tool's ramp feel,
    // but geometric (identical at any frame rate). Raise/lower to make the ramp faster/slower.
    float _accPerArc;

    // Ring of the last 4 path points in GRID space (4 is enough for one centripetal Catmull-Rom span). _head indexes
    // the newest; Pt(k) reads the point k steps before the newest, clamped to the oldest we still hold.
    readonly Vector2[] _ring = new Vector2[4];
    int _head = -1, _n;

    // Per-span scratch (reused so painting doesn't allocate after the first stroke; single-threaded use). One entry
    // per cell touched this span: coverage summed over the span's sub-steps (weight x arc-length), plus the
    // direction/centre from its strongest sub-step (which fixes the painted direction).
    struct SpanCell {
        public Point point; public float coverage; public float maxWeight;
        public Vector2 brushDir; public Vector2 strokeDir; public Vector2 center;
    }
    readonly List<SpanCell> _cells = new List<SpanCell>();
    readonly Dictionary<Point, int> _index = new Dictionary<Point, int>();   // cell -> index in _cells (per span)

    // Exact-overlap state. One entry per cell currently within a brush radius of the head; carries the coverage
    // ACCUMULATED along the swept path (so effect builds up with drag distance, capped at full), the strongest weight
    // seen (which fixes the painted direction), the brush/stroke direction and radial-op centre at that strongest
    // touch, and the field value captured just before the head arrived.
    struct Active {
        public float coverage; public float maxWeight; public Vector2 brushDir; public Vector2 strokeDir;
        public Vector2 center; public Vector2 snapshot;
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

    VectorFieldStroke() {}   // pooled — created via Rent (see BeginStroke)

    // Rent an idle stroke (or make one) and initialise it for a new stroke. Called by field.BeginStroke.
    internal static VectorFieldStroke Rent(DrawableVectorFieldComponent field, in VectorFieldBrush brush) {
        var s = s_pool.Count > 0 ? s_pool.Pop() : new VectorFieldStroke();
        s._field = field;
        s._brush = brush;
        var cc = field.gridRenderer.cellCenter;
        s._gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(brush.size, 0f, 0f)).magnitude);
        s._accPerArc = 1f / s._gridRadius;
        s._head = -1;
        s._n = 0;
        s._sourceReady = false;   // keep any pooled _source buffer for reuse; it's refilled on next capture
        s._cells.Clear();
        s._index.Clear();
        s._active.Clear();
        s._evict.Clear();
        return s;
    }

    public void To(Vector3 worldPosition) {
        if (_field == null || !_brush.IsValid) return;   // _field == null => already ended (and possibly pooled)
        Vector2 g = _field.gridRenderer.cellCenter.WorldToGridPosition(worldPosition);
        // Coalesce moves too small to define a direction, so the first rendered span carries the real drag direction
        // (otherwise the click / stroke-start cells get painted in a fallback direction before the drag is known).
        if (_n > 0 && (g - Pt(0)).sqrMagnitude < MinStepCells * MinStepCells) return;
        Push(g);
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
        if (_field == null) return;   // already ended
        if (_brush.IsValid && _brush.tipMode == TipMode.Smoothed && _n >= 2)
            RenderSpanToNewest();
        _active.Clear();
        _cells.Clear();
        _index.Clear();
        _evict.Clear();
        _sourceReady = false;   // keep the _source buffer on the pooled instance for reuse
        _head = -1;
        _n = 0;
        _field = null;   // mark ended; guards To()/End() against use-after-return once pooled
        if (s_pool.Count < PoolCapacity) s_pool.Push(this);
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

        // Overall span direction — the fallback tangent for degenerate sub-steps. At a stroke's START the leading
        // control point is duplicated, so the centripetal spline begins with near-zero velocity and the first sub-step
        // can be ~zero length; without this those click-spot cells would fall back to a hardcoded up.
        Vector2 spanDir = (p2 - p1).sqrMagnitude > 1e-8f ? (p2 - p1).normalized : Vector2.up;

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / SubStepCells));
        Vector2 prev = p1;
        for (int k = 1; k <= steps; k++) {
            Vector2 cur = CentripetalCatmullRom(p0, p1, p2, p3, k / (float)steps);
            RasterizeCapsule(prev, cur, Vector2.Distance(prev, cur), spanDir);   // ds = this sub-step's arc length
            prev = cur;
        }

        CommitSpan();
    }

    // Add this span's coverage into the active set (accumulating along the sweep) and re-apply each touched cell from
    // its pre-touch snapshot at the new coverage. Then evict cells the head has moved beyond so the set stays bounded.
    void CommitSpan() {
        if (_cells.Count == 0) return;
        var field = _field.PaintField;
        var op = _brush.op;

        // Neighbour-reading ops need a stable pre-stroke snapshot to sample; capture it once, before any painting.
        // Reuse the pooled buffer when the grid size matches (just copy the values), reallocating only on a size
        // change — so repeated Smudge strokes don't each clone the whole field.
        if (op.NeedsSnapshot && !_sourceReady) {
            if (_source == null || _source.size.x != field.size.x || _source.size.y != field.size.y)
                _source = new Vector2Map(field.size, (Vector2[])field.values.Clone());
            else
                System.Array.Copy(field.values, _source.values, field.values.Length);
            _sourceReady = true;
        }
        Vector2Map source = op.NeedsSnapshot ? _source : field;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < _cells.Count; i++) {
            var c = _cells[i];
            Point p = c.point;

            if (_active.TryGetValue(p, out Active a)) {
                a.coverage = Mathf.Min(1f, a.coverage + c.coverage);   // build up along the sweep, capped at full
                if (c.maxWeight > a.maxWeight) {                        // strongest touch fixes the painted direction
                    a.maxWeight = c.maxWeight; a.brushDir = c.brushDir; a.strokeDir = c.strokeDir; a.center = c.center;
                }
                _active[p] = a;
            } else {
                a = new Active {
                    coverage = Mathf.Min(1f, c.coverage), maxWeight = c.maxWeight, brushDir = c.brushDir,
                    strokeDir = c.strokeDir, center = c.center, snapshot = field.GetValueAtGridPoint(p),
                };
                _active[p] = a;
            }

            // brushForce/finalForce carry the accumulated coverage as magnitude (ctx.Weight); the op runs once from
            // the pre-touch snapshot, so the result depends only on the swept geometry (frame-rate independent).
            var ctx = new BrushApplyContext(a.snapshot, a.brushDir * a.coverage, a.brushDir * a.coverage,
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

    // Add every cell within _gridRadius of segment a->b to this span, contributing coverage = weight * ds (arc length)
    // so a cell builds up as the brush sweeps across it, and tracking its strongest weight for the painted direction.
    void RasterizeCapsule(Vector2 a, Vector2 b, float ds, Vector2 fallbackDir) {
        var size = _field.PaintField.size;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - _gridRadius));
        int maxX = Mathf.Min(size.x - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + _gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - _gridRadius));
        int maxY = Mathf.Min(size.y - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + _gridRadius));
        float invR = 1f / _gridRadius;

        Vector2 ab = b - a;
        float abLenSqr = ab.sqrMagnitude;
        Vector2 tangent = abLenSqr > 1e-8f ? ab.normalized : fallbackDir;
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
                    Accumulate(new Point(x, y), mag * ds * _accPerArc, mag, brushDir, strokeDir, nearest);
                } else {
                    float dist = offset.magnitude;
                    if (dist > _gridRadius) continue;
                    float w = _brush.shape.Weight(dist * invR);
                    if (w <= 0f) continue;
                    Accumulate(new Point(x, y), w * ds * _accPerArc, w, tangent, tangent, nearest);
                }
            }
        }
    }

    void Accumulate(Point gridPoint, float coverage, float weight, Vector2 brushDir, Vector2 strokeDir, Vector2 center) {
        if (_index.TryGetValue(gridPoint, out int i)) {
            var e = _cells[i];
            e.coverage += coverage;                            // sum along the sweep
            if (weight > e.maxWeight) {                        // strongest sub-step fixes the direction/centre
                e.maxWeight = weight; e.brushDir = brushDir; e.strokeDir = strokeDir; e.center = center;
            }
            _cells[i] = e;
        } else {
            _index[gridPoint] = _cells.Count;
            _cells.Add(new SpanCell { point = gridPoint, coverage = coverage, maxWeight = weight,
                                      brushDir = brushDir, strokeDir = strokeDir, center = center });
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
