using System.Collections.Generic;
using UnityEngine;

// A continuous, smooth paint stroke into an IPaintTarget<T>, generic over the field's value type. Feed it world
// positions with To(); it sweeps a soft capsule along a centripetal-Catmull-Rom-smoothed path (no dabbing) and marks
// the touched region dirty. The path smoothing, arc-length coverage, and exact-overlap machinery are value-agnostic —
// only the snapshot value, the field, and op.Apply are typed — so vector-field painting (VectorFieldStroke, below) and
// smoke/colour painting share this exact code, and any fix here applies to both.
//
// Created via a facade's BeginStroke (which pools and Rents an instance). Hold the returned instance and call To() each
// frame; call End() when done (End returns it to the pool — begin a new one after).
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
//   Cells are held in a small "active" set only while within a brush radius of the head: snapshot on first touch,
//   re-applied as coverage accumulates while active, then evicted once the head moves past (their value is final). So
//   finalized cells are free to decay while the head stays fresh, and the cost is bounded by the brush footprint
//   regardless of stroke length.
public class PaintStroke<T> {
    protected const float SubStepCells = 0.5f;   // finest spline sampling along the path, in grid cells (small brushes)
    // For a brush of radius r cells, sampling every 0.5 cells re-rasterizes the full radius-r capsule ~(2r/0.5) times
    // over the same cells — cost that explodes as resolution grows the brush footprint. The arc-length coverage model
    // is a Riemann sum of ∫w·ds, so its per-cell total is invariant to the step size (only the w-weighting integrates a
    // little coarser). So we can step proportionally to the radius: sub-step ≈ r * this fraction, floored at SubStepCells
    // so small brushes keep their fine sampling and paint strength/feel are unchanged; large brushes stop oversampling.
    protected const float SubStepRadiusFraction = 0.5f;
    // A new point closer than this to the last one is coalesced (not rendered yet): a span shorter than this has no
    // reliable direction, so rendering it would paint the start cells in an arbitrary (fallback) direction.
    protected const float MinStepCells = 0.5f;

    IPaintTarget<T> _field;
    BrushShape _shape;
    IBrushOp<T> _op;
    float _pressure;
    TipMode _tipMode;
    BrushDirectionMode _dirMode;

    float _gridRadius;
    // Coverage added per unit arc-length swept at full weight (~1/radius), so effect builds up with drag distance and
    // a cell reaches full coverage after the brush sweeps roughly its own width across it — geometric (identical at
    // any frame rate).
    float _accPerArc;
    // Path sampling step in cells, scaled to the brush radius (see SubStepRadiusFraction) so large brushes don't
    // re-rasterize the same cells dozens of times per span.
    float _subStepCells;

    // Ring of the last 4 path points in GRID space (4 is enough for one centripetal Catmull-Rom span). _head indexes
    // the newest; Pt(k) reads the point k steps before the newest, clamped to the oldest we still hold.
    readonly Vector2[] _ring = new Vector2[4];
    int _head = -1, _n;

    // Cells are keyed by a packed int (y * _gridWidth + x) rather than the Point struct: an int key hashes faster and,
    // more importantly, lets the active set live in a plain array we mutate in place (arr[slot].field = …, no copy) —
    // the big Active struct was previously copied twice per cell through Dictionary<Point, Active> and once more per
    // cell just to enumerate it, which dominated CommitSpan/EvictBehindHead on large brushes.
    int _gridWidth;

    struct SpanCell {
        public Point point; public float coverage; public float maxWeight;
        public Vector2 brushDir; public Vector2 strokeDir; public Vector2 center;
    }
    readonly List<SpanCell> _cells = new List<SpanCell>();
    readonly Dictionary<int, int> _index = new Dictionary<int, int>();   // cell key -> index in _cells (per span)

    struct Active {
        public float coverage; public float maxWeight; public Vector2 brushDir; public Vector2 strokeDir;
        public Vector2 center; public T snapshot;
    }
    // Open-addressed by _activeIndex (cell key -> slot); the value lives in the parallel _active/_activeKey arrays so it
    // can be mutated in place. Removal is a swap-with-last (one copy, only for evicted cells).
    Active[] _active = new Active[256];
    int[] _activeKey = new int[256];       // slot -> cell key, for eviction + swap-remove bookkeeping
    int _activeCount;
    readonly Dictionary<int, int> _activeIndex = new Dictionary<int, int>();   // cell key -> slot in _active
    readonly List<int> _evict = new List<int>();

    // Pre-stroke field clone, for neighbour-reading ops (Smudge) so their upstream samples are stable and
    // order-independent. Only allocated when the op needs it; kept (subtyped) across strokes and refilled by copy.
    TypeMap<T> _source;
    bool _sourceReady;

    // End point (grid space) of the span just rendered — the next span starts here, so it's the frontier eviction
    // measures from (NOT the newest fed point, which is one span ahead in Smoothed mode).
    Vector2 _spanEnd;

    protected bool IsValid => _shape != null && _op != null;

    // Initialise (or re-initialise a pooled instance) for a new stroke. Keeps any pooled _source buffer for reuse.
    protected void Init(IPaintTarget<T> field, BrushShape shape, IBrushOp<T> op, float size,
                        float pressure, TipMode tipMode, BrushDirectionMode dirMode) {
        _field = field;
        _shape = shape;
        _op = op;
        _pressure = pressure;
        _tipMode = tipMode;
        _dirMode = dirMode;
        var cc = field.grid;
        _gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(size, 0f, 0f)).magnitude);
        _accPerArc = 1f / _gridRadius;
        _subStepCells = Mathf.Max(SubStepCells, _gridRadius * SubStepRadiusFraction);
        _gridWidth = Mathf.Max(1, field.PaintField.size.x);   // packs cell (x,y) -> y*_gridWidth+x for the int keys
        _head = -1;
        _n = 0;
        _sourceReady = false;
        _cells.Clear();
        _index.Clear();
        _activeIndex.Clear();
        _activeCount = 0;
        _evict.Clear();
    }

    // Pack a grid cell into the int key used by _index / _activeIndex.
    int Key(Point p) => p.y * _gridWidth + p.x;

    public void To(Vector3 worldPosition) {
        if (_field == null || !IsValid) return;   // _field == null => already ended (and possibly pooled)
        Vector2 g = _field.grid.WorldToGridPosition(worldPosition);
        // Coalesce moves too small to define a direction, so the first rendered span carries the real drag direction.
        if (_n > 0 && (g - Pt(0)).sqrMagnitude < MinStepCells * MinStepCells) return;
        Push(g);
        if (_n < 2) return;

        if (_tipMode == TipMode.Leading)
            RenderSpanToNewest();                 // draw the freshest span [prev -> newest], zero lag
        else if (_n >= 3)
            RenderSmoothedSpan();                 // draw [prev2 -> prev]; newest is look-ahead only
    }

    // Draw whatever tail hasn't been rendered yet, then reset and return to the pool (via ReturnToPool). In Smoothed
    // mode the newest span is still pending look-ahead, so we flush it here (this is also what makes a 2-point line
    // draw its single span).
    public void End() {
        if (_field == null) return;   // already ended
        if (IsValid && _tipMode == TipMode.Smoothed && _n >= 2)
            RenderSpanToNewest();
        _activeIndex.Clear();
        _activeCount = 0;
        _cells.Clear();
        _index.Clear();
        _evict.Clear();
        _sourceReady = false;   // keep the _source buffer on the pooled instance for reuse
        _head = -1;
        _n = 0;
        _field = null;   // mark ended; guards To()/End() against use-after-return once pooled
        ReturnToPool();
    }

    // Return this instance to its (type-specific) pool. Base default keeps nothing; typed subclasses (e.g.
    // VectorFieldStroke) override to push themselves onto their pool so BeginStroke can reuse them.
    protected virtual void ReturnToPool() { }

    // --- internals --------------------------------------------------------------------------------------------------

    void Push(Vector2 gridPoint) {
        _head = (_head + 1) & 3;
        _ring[_head] = gridPoint;
        if (_n < 4) _n++;
    }

    Vector2 Pt(int k) {
        if (k >= _n) k = _n - 1;
        return _ring[(_head - k) & 3];
    }

    void RenderSpanToNewest() {
        Vector2 p1 = Pt(1), p2 = Pt(0);
        Vector2 p0 = Pt(2);
        Vector2 p3 = p2 + (p2 - p1);   // extrapolated look-ahead
        RenderSpan(p0, p1, p2, p3);
    }

    void RenderSmoothedSpan() {
        RenderSpan(Pt(3), Pt(2), Pt(1), Pt(0));
    }

    void RenderSpan(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) {
        _cells.Clear();
        _index.Clear();
        _spanEnd = p2;   // the next span starts here; eviction keeps cells near it

        // Overall span direction — the fallback tangent for degenerate sub-steps (e.g. the near-zero first sub-step at
        // a stroke's start, where the leading control point is duplicated).
        Vector2 spanDir = (p2 - p1).sqrMagnitude > 1e-8f ? (p2 - p1).normalized : Vector2.up;

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / _subStepCells));
        Vector2 prev = p1;
        for (int k = 1; k <= steps; k++) {
            Vector2 cur = CentripetalCatmullRom(p0, p1, p2, p3, k / (float)steps);
            RasterizeCapsule(prev, cur, Vector2.Distance(prev, cur), spanDir);   // ds = this sub-step's arc length
            prev = cur;
        }

        CommitSpan();
    }

    void CommitSpan() {
        if (_cells.Count == 0) return;
        TypeMap<T> field = _field.PaintField;
        var op = _op;

        // Neighbour-reading ops need a stable pre-stroke snapshot to sample; capture it once, before any painting.
        // Reuse the pooled buffer when the grid size matches (just copy the values), reallocating a correctly-typed
        // map only on a size change — so repeated Smudge strokes don't each clone the whole field.
        if (op.NeedsSnapshot && !_sourceReady) {
            if (_source == null || _source.size.x != field.size.x || _source.size.y != field.size.y)
                _source = _field.CreateMap(field.size);
            System.Array.Copy(field.values, _source.values, field.values.Length);
            _sourceReady = true;
        }
        TypeMap<T> source = op.NeedsSnapshot ? _source : field;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < _cells.Count; i++) {
            var c = _cells[i];
            Point p = c.point;
            int key = Key(p);

            int slot;
            if (_activeIndex.TryGetValue(key, out slot)) {
                // Mutate the struct in place in the array — no copy in/out (arr[slot].field = … is a direct write).
                _active[slot].coverage = Mathf.Min(1f, _active[slot].coverage + c.coverage);   // build up along the sweep
                if (c.maxWeight > _active[slot].maxWeight) {                                    // strongest touch fixes dir
                    _active[slot].maxWeight = c.maxWeight; _active[slot].brushDir = c.brushDir;
                    _active[slot].strokeDir = c.strokeDir; _active[slot].center = c.center;
                }
            } else {
                slot = _activeCount++;
                if (slot >= _active.Length) GrowActive();
                _active[slot] = new Active {
                    coverage = Mathf.Min(1f, c.coverage), maxWeight = c.maxWeight, brushDir = c.brushDir,
                    strokeDir = c.strokeDir, center = c.center, snapshot = field.GetValueAtGridPoint(p),
                };
                _activeKey[slot] = key;
                _activeIndex[key] = slot;
            }

            // brushForce/finalForce carry the accumulated coverage as magnitude (ctx.Weight); the op runs once from
            // the pre-touch snapshot, so the result depends only on the swept geometry (frame-rate independent).
            ref Active a = ref _active[slot];
            var ctx = new BrushApplyContext<T>(a.snapshot, a.brushDir * a.coverage, a.brushDir * a.coverage,
                                               a.strokeDir, _pressure, p, a.center, source);
            field.SetValueAtGridPoint(p, op.Apply(ctx));

            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        _field.MarkRegionDirty(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));

        EvictBehindHead();
    }

    void EvictBehindHead() {
        float er = _gridRadius + 1f;   // one cell of margin so a cell isn't dropped right as the next span reaches it
        float erSqr = er * er;
        _evict.Clear();
        // Walk the slot array reading only the int key (no Active-struct copy per cell — the old dictionary enumeration
        // copied the whole value struct every iteration).
        for (int slot = 0; slot < _activeCount; slot++) {
            int key = _activeKey[slot];
            if (_index.ContainsKey(key)) continue;   // touched this span — still under the head
            int x = key % _gridWidth, y = key / _gridWidth;
            float dx = x - _spanEnd.x, dy = y - _spanEnd.y;
            if (dx * dx + dy * dy > erSqr) _evict.Add(key);
        }
        for (int i = 0; i < _evict.Count; i++) RemoveActive(_evict[i]);
    }

    // Swap-with-last removal: one struct copy, and only for the (few) evicted cells.
    void RemoveActive(int key) {
        if (!_activeIndex.TryGetValue(key, out int slot)) return;
        int last = --_activeCount;
        if (slot != last) {
            _active[slot] = _active[last];
            _activeKey[slot] = _activeKey[last];
            _activeIndex[_activeKey[slot]] = slot;
        }
        _activeIndex.Remove(key);
    }

    void GrowActive() {
        int n = _active.Length * 2;
        System.Array.Resize(ref _active, n);
        System.Array.Resize(ref _activeKey, n);
    }

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
        bool isMap = _shape.IsMap;
        // Brush local frame a map shape is sampled in. FollowStroke: +Y (forward) = the stroke tangent; FixedAngle:
        // the world frame (map's baked emitter direction used as-is). +X (right) = forward rotated -90.
        Vector2 forward = _dirMode == BrushDirectionMode.FixedAngle ? Vector2.up : tangent;
        Vector2 right = new Vector2(forward.y, -forward.x);
        bool fixedAngle = _dirMode == BrushDirectionMode.FixedAngle;

        // Hoisted scalars for the inner loop — this runs over the whole brush bbox every sub-step (tens of thousands of
        // cells/frame at high res), so we do the point→segment math in raw floats instead of Vector2 operators (each of
        // which is a non-inlined method call: Dot, magnitude, +, -, *). Same math, far less per-cell overhead.
        float ax = a.x, ay = a.y, abx = ab.x, aby = ab.y;
        float invAbLenSqr = abLenSqr > 1e-8f ? 1f / abLenSqr : 0f;
        float rx = right.x, ry = right.y, fx = forward.x, fy = forward.y;
        float radiusSqr = _gridRadius * _gridRadius;
        float dsAcc = ds * _accPerArc;

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                // Nearest point on segment ab to (x,y), clamped to the segment.
                float t = ((x - ax) * abx + (y - ay) * aby) * invAbLenSqr;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                float nx = ax + abx * t, ny = ay + aby * t;
                float ox = x - nx, oy = y - ny;

                if (isMap) {
                    float lx = (ox * rx + oy * ry) * invR, ly = (ox * fx + oy * fy) * invR;
                    Vector2 sample = _shape.Sample2D(new Vector2(lx, ly));   // local-frame emitter vector (mag = weight)
                    float mag = sample.magnitude;
                    if (mag <= 0f) continue;
                    float invMag = 1f / mag;
                    var brushDir = new Vector2((sample.x * rx + sample.y * fx) * invMag,
                                               (sample.x * ry + sample.y * fy) * invMag);   // emitter dir in world
                    // FollowStroke paints along the drag; FixedAngle paints the emitter dir.
                    Vector2 strokeDir = fixedAngle ? brushDir : tangent;
                    Accumulate(new Point(x, y), mag * dsAcc, mag, brushDir, strokeDir, new Vector2(nx, ny));
                } else {
                    float distSqr = ox * ox + oy * oy;
                    if (distSqr > radiusSqr) continue;               // out of the disc — skip the sqrt + weight
                    float w = _shape.Weight(Mathf.Sqrt(distSqr) * invR);
                    if (w <= 0f) continue;
                    Accumulate(new Point(x, y), w * dsAcc, w, tangent, tangent, new Vector2(nx, ny));
                }
            }
        }
    }

    void Accumulate(Point gridPoint, float coverage, float weight, Vector2 brushDir, Vector2 strokeDir, Vector2 center) {
        int key = Key(gridPoint);
        if (_index.TryGetValue(key, out int i)) {
            var e = _cells[i];
            e.coverage += coverage;                            // sum along the sweep
            if (weight > e.maxWeight) {                        // strongest sub-step fixes the direction/centre
                e.maxWeight = weight; e.brushDir = brushDir; e.strokeDir = strokeDir; e.center = center;
            }
            _cells[i] = e;
        } else {
            _index[key] = _cells.Count;
            _cells.Add(new SpanCell { point = gridPoint, coverage = coverage, maxWeight = weight,
                                      brushDir = brushDir, strokeDir = strokeDir, center = center });
        }
    }

    // Centripetal Catmull-Rom (alpha = 0.5), evaluating the p1->p2 segment at t in [0,1] via the Barry-Goldman
    // pyramid. Knot spacing uses sqrt(chord length) so the curve passes through the points without the overshoot the
    // uniform variant shows on uneven spacing / sharp turns. Each interval is floored to eps so coincident control
    // points (stroke start/end) can't divide by zero.
    protected static Vector2 CentripetalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
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

// The Vector2 stroke: the vector-field painting front-end. Preserves the public name/type the editor tool, runtime
// facades (VectorFieldPainting), and demos already use, while all the logic lives in the generic PaintStroke<Vector2>.
// Owns the Vector2 pool (rented in BeginStroke, returned in End) so many short-lived strokes don't churn GC.
public sealed class VectorFieldStroke : PaintStroke<Vector2> {
    static readonly Stack<VectorFieldStroke> s_pool = new Stack<VectorFieldStroke>();
    const int PoolCapacity = 32;

    VectorFieldStroke() {}   // pooled — created via Rent (see VectorFieldPainting.BeginStroke)

    // Rent an idle stroke (or make one) and initialise it for a new stroke.
    internal static VectorFieldStroke Rent(IPaintTarget<Vector2> field, in VectorFieldBrush brush) {
        var s = s_pool.Count > 0 ? s_pool.Pop() : new VectorFieldStroke();
        s.Init(field, brush.shape, brush.op, brush.size, brush.pressure, brush.tipMode, brush.directionMode);
        return s;
    }

    protected override void ReturnToPool() {
        if (s_pool.Count < PoolCapacity) s_pool.Push(this);
    }
}
