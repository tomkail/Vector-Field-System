using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using VectorFields;

namespace CrowdFlow {
    /// <summary>
    /// Orchestrates the crowd-navigation demo (the analogue of Windfall's game manager). It owns:
    /// <list type="bullet">
    /// <item>a shared <see cref="NavCostField"/> sampled from the <see cref="Terrain"/> via <see cref="TerrainNavSource"/>;</item>
    /// <item>one <see cref="FlowFieldSolver"/> per <see cref="Attraction"/>, each solved to that destination;</item>
    /// <item>an authoritative <see cref="GridTransform"/> (the "nav plane") that maps world XZ ↔ grid cells — the single
    ///       source of truth shared by terrain sampling, agent steering and the flow visualisation, so all three stay
    ///       aligned by construction;</item>
    /// <item>an optional per-destination <see cref="DrawableVectorFieldComponent"/> the flow is written into, purely so
    ///       the existing flow visualisers render the navigation currents on the ground.</item>
    /// </list>
    /// When the landscape changes it re-samples the affected cost cells and re-solves every destination, so the crowd
    /// re-routes live. Agents read the solved flow straight off the CPU (no GPU readback) through <see cref="FlowDir"/>.
    /// </summary>
    public class CrowdFlowManager : MonoBehaviour {
        [System.Serializable]
        public class Destination {
            public Attraction attraction;
            [Tooltip("Optional: a Drawable field the flow is written into for on-screen visualisation. Its transform " +
                     "is forced to match the nav plane at start, so it always overlays the terrain.")]
            public DrawableVectorFieldComponent visualField;
        }

        [Header("World")]
        [Tooltip("The terrain the crowd walks on. Slopes cost more uphill; below the waterline is impassable.")]
        public Terrain terrain;
        [Tooltip("Nav-grid resolution (cells per axis). 128 is a good moderate-scale default; re-solve stays sub-ms.")]
        public int resolution = 128;
        [Tooltip("World Y of the water surface; ground below this is impassable.")]
        public float waterLevel = 2f;
        [Tooltip("Slope (degrees) above which ground is an unwalkable cliff.")]
        public float maxWalkableSlopeDeg = 45f;
        [Tooltip("Generic base-cost bump on steep-but-walkable ground. Leave 0 — slope should cost through speed " +
                 "(below), not a separate penalty, so the pathfinding cost stays equal to real travel time.")]
        public float slopeCostScale = 0f;

        [Header("Slope ↔ speed (one model drives both the route cost and the agents' pace)")]
        [Tooltip("How hard walking uphill slows down. The path cost is travel time (distance / this speed), and the " +
                 "agents move at the same speed — so uphill is dear BECAUSE it is slow, and timings match the field.")]
        public float uphillSlow = 3f;
        [Tooltip("How much walking downhill speeds up (0 = no downhill bonus).")]
        public float downhillSpeedup = 0.6f;
        [Tooltip("Slowest a steep uphill step may get, as a fraction of flat speed (never a dead stop).")]
        public float minSpeedMul = 0.15f;
        [Tooltip("Fastest a downhill step may get, as a multiple of flat speed (no runaway sprint).")]
        public float maxSpeedMul = 1.6f;
        [Tooltip("Colliders on these layers block cells (buildings, walls, placed props).")]
        public LayerMask blockedMask;

        [Header("Destinations (auto-collected from child Attractions if left empty)")]
        public List<Destination> destinations = new List<Destination>();

        [Header("Visualisation")]
        [Tooltip("World Y of the flat flow-visualisation plane laid over the terrain.")]
        public float fieldVisualizationHeight = 12f;

        // --- runtime state ---
        GridTransform _navGrid;                 // authoritative world<->cell mapping (shared by all consumers)
        Transform _navPlane;                    // owner transform _navGrid is bound to
        TerrainNavSource _source;
        NavCostField _cost;
        readonly List<FlowFieldSolver> _solvers = new List<FlowFieldSolver>();
        readonly List<Vector2Int> _goalScratch = new List<Vector2Int>(1);

        // Deferred re-solve: landscape edits fire in bursts (once per brush stamp), so we coalesce to one re-solve
        // per frame. `_pendingCostRect` unions the cost cells to re-sample; `_fullCostRebuild` forces a whole re-sample
        // (water/obstacle changes, which aren't localised by the heightmap callback).
        RectInt? _pendingCostRect;
        bool _fullCostRebuild;
        bool _needResolve;

        public float LastSolveMs { get; private set; }
        public int DestinationCount => _solvers.Count;
        public GridTransform NavGrid => _navGrid;
        public Vector2Int GridSize => new Vector2Int(resolution, resolution);
        public Attraction GetAttraction(int i) => destinations[i].attraction;

        void Start() {
            if (terrain == null) { Debug.LogError("CrowdFlowManager: no Terrain assigned.", this); enabled = false; return; }
            resolution = Mathf.Max(4, resolution);
            AutoCollectDestinations();
            BuildNavPlane();

            _source = new TerrainNavSource(terrain, _navGrid) {
                waterLevel = waterLevel,
                maxWalkableSlopeDeg = maxWalkableSlopeDeg,
                slopeCostScale = slopeCostScale,
                blockedMask = blockedMask,
            };
            // One orthogonal cell spans this much world distance — needed to turn cell steps into a real grade.
            float cellWorldSize = terrain.terrainData.size.x / Mathf.Max(1, resolution);
            _cost = new NavCostField(GridSize) {
                cellWorldSize = cellWorldSize,
                uphillSlow = uphillSlow,
                downhillSpeedup = downhillSpeedup,
                minSpeedMul = minSpeedMul,
                maxSpeedMul = maxSpeedMul,
            };
            _cost.RebuildAll(_source);

            for (int i = 0; i < destinations.Count; i++) {
                _solvers.Add(new FlowFieldSolver(_cost));
                if (destinations[i].attraction != null) destinations[i].attraction.fieldIndex = i;
                ConfigureVisualField(i);
            }
            SolveAll();

            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
        }

        void OnDestroy() {
            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
        }

        void AutoCollectDestinations() {
            if (destinations != null && destinations.Count > 0) return;
            destinations = new List<Destination>();
            foreach (var a in GetComponentsInChildren<Attraction>()) destinations.Add(new Destination { attraction = a });
        }

        // Build the flat nav plane (rot X −90 so its normal points up, the flow-vis convention) sized to the terrain
        // footprint, and bind the authoritative GridTransform to it.
        void BuildNavPlane() {
            var td = terrain.terrainData;
            Vector3 p = terrain.transform.position;
            var center = new Vector3(p.x + td.size.x * 0.5f, fieldVisualizationHeight, p.z + td.size.z * 0.5f);

            var go = new GameObject("NavPlane");
            _navPlane = go.transform;
            _navPlane.SetParent(transform, false);
            _navPlane.position = center;
            _navPlane.rotation = Quaternion.Euler(-90f, 0f, 0f);
            _navPlane.localScale = new Vector3(td.size.x, td.size.z, 1f);

            _navGrid = new GridTransform(GridSize);
            _navGrid.Bind(_navPlane);
        }

        // Match a destination's visual Drawable to the nav plane so its grid aligns with ours, then it's ready to
        // receive flow. Skips gracefully when no visual field is wired (agents still work — viz is optional).
        void ConfigureVisualField(int i) {
            var vf = destinations[i].visualField;
            if (vf == null) return;
            vf.transform.SetPositionAndRotation(_navPlane.position, _navPlane.rotation);
            vf.transform.localScale = _navPlane.localScale;
            vf.grid.Size = GridSize;
        }

        // ---------------------------------------------------------------- solving

        public void SolveAll() {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < _solvers.Count; i++) {
                BuildGoalCells(i);
                _solvers[i].Solve(_goalScratch);
                WriteFlowToVisual(i);
            }
            sw.Stop();
            LastSolveMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void BuildGoalCells(int i) {
            _goalScratch.Clear();
            var a = destinations[i].attraction;
            if (a == null) return;
            Vector2Int cell = WorldToCellClamped(a.transform.position);
            if (_cost.blocked[_cost.Index(cell.x, cell.y)]) {
                if (TryFindNearestPassable(cell, out Vector2Int near)) cell = near;
                else return;   // no passable goal — this destination is currently unreachable everywhere
            }
            _goalScratch.Add(cell);
        }

        // Copy the solved flow into the destination's Drawable (grid-local Vector2 layout matches the solver's), then
        // upload the whole field. The visualiser picks it up automatically.
        void WriteFlowToVisual(int i) {
            var vf = destinations[i].visualField;
            if (vf == null) return;
            var map = vf.PaintField;                     // sized to grid on demand
            var flow = _solvers[i].flow;
            if (map.values.Length == flow.Length) System.Array.Copy(flow, map.values, flow.Length);
            vf.MarkRegionDirty(new RectInt(0, 0, resolution, resolution));
            vf.EnsureUpToDate();   // force the GPU render now so the flow visualiser refreshes this frame
        }

        // ---------------------------------------------------------------- landscape change

        // Terrain sculpting fires this (in editor and at runtime) with the edited heightmap rect. We convert it to
        // cells and defer the actual re-sample + re-solve to LateUpdate so a burst of brush stamps costs one re-solve.
        void OnHeightmapChanged(Terrain t, RectInt heightRegion, bool synched) {
            if (t != terrain) return;
            RectInt cells = HeightmapRectToCells(heightRegion);
            _pendingCostRect = _pendingCostRect.HasValue ? Union(_pendingCostRect.Value, cells) : cells;
            _needResolve = true;
        }

        /// <summary>Force a re-sample of a world-space region (used for collider-only edits the heightmap callback misses).</summary>
        public void MarkDirtyWorldBounds(Bounds worldBounds) {
            Vector2Int a = WorldToCellClamped(worldBounds.min);
            Vector2Int b = WorldToCellClamped(worldBounds.max);
            var rect = FromCorners(a, b);
            _pendingCostRect = _pendingCostRect.HasValue ? Union(_pendingCostRect.Value, rect) : rect;
            _needResolve = true;
        }

        /// <summary>Change the water level and re-solve (whole cost rebuild — water isn't localised).</summary>
        public void SetWaterLevel(float level) {
            waterLevel = level;
            if (_source != null) _source.waterLevel = level;
            _fullCostRebuild = true;
            _needResolve = true;
        }

        void LateUpdate() {
            if (!_needResolve) return;
            _needResolve = false;

            if (_fullCostRebuild || !_pendingCostRect.HasValue) {
                _cost.RebuildAll(_source);
            } else {
                // CHUNKING: only the cost cells inside the edited rect are re-sampled; the flow re-solve below is still
                // global (moderate scale). Tier-1 dirty-chunk skipping would bound the re-solve to this rect's horizon.
                var r = _pendingCostRect.Value;
                r = new RectInt(r.xMin - 1, r.yMin - 1, r.width + 2, r.height + 2);   // margin for the climb penalty
                _cost.RebuildRegion(r, _source);
            }
            _fullCostRebuild = false;
            _pendingCostRect = null;
            SolveAll();
        }

        // ---------------------------------------------------------------- agent-facing API

        /// <summary>
        /// World-space, ground-projected unit steering direction toward destination <paramref name="dest"/> at
        /// <paramref name="worldPos"/> (bilinearly interpolated across cells for smoothness). Returns
        /// <see cref="Vector3.zero"/> where the position is unreachable / off-grid — the agent should hold or re-target.
        /// </summary>
        public Vector3 FlowDir(int dest, Vector3 worldPos) {
            if (dest < 0 || dest >= _solvers.Count) return Vector3.zero;
            Vector2 g = SampleFlowGrid(dest, worldPos);
            if (g == Vector2.zero) return Vector3.zero;
            Vector3 world = _navGrid.GridToWorldVector(g);
            world.y = 0f;
            return world.sqrMagnitude > 1e-6f ? world.normalized : Vector3.zero;
        }

        // Bilinear blend of the four surrounding cells' grid-space flow vectors.
        Vector2 SampleFlowGrid(int dest, Vector3 worldPos) {
            Vector2 gp = _navGrid.WorldToGridPosition(worldPos);   // cell centres sit at integer coords
            int x0 = Mathf.FloorToInt(gp.x), y0 = Mathf.FloorToInt(gp.y);
            float tx = gp.x - x0, ty = gp.y - y0;
            var flow = _solvers[dest].flow;

            Vector2 F(int x, int y) {
                x = Mathf.Clamp(x, 0, resolution - 1);
                y = Mathf.Clamp(y, 0, resolution - 1);
                return flow[y * resolution + x];
            }
            Vector2 bottom = Vector2.Lerp(F(x0, y0), F(x0 + 1, y0), tx);
            Vector2 top = Vector2.Lerp(F(x0, y0 + 1), F(x0 + 1, y0 + 1), tx);
            return Vector2.Lerp(bottom, top, ty);
        }

        /// <summary>Is <paramref name="worldPos"/> currently able to reach destination <paramref name="dest"/>?</summary>
        public bool IsReachable(int dest, Vector3 worldPos) {
            if (dest < 0 || dest >= _solvers.Count) return false;
            Vector2Int c = WorldToCellClamped(worldPos);
            return _solvers[dest].integration[_cost.Index(c.x, c.y)] < FlowFieldSolver.Unreachable;
        }

        /// <summary>Snap a world position's Y to the terrain surface (agents/spawners walk on the ground).</summary>
        public float SurfaceY(float worldX, float worldZ) => _source != null ? _source.SurfaceY(worldX, worldZ) : 0f;

        /// <summary>
        /// Walking-speed multiplier (vs flat ground) an agent at <paramref name="worldPos"/> heading along
        /// <paramref name="dirXZ"/> should use — slower uphill, faster downhill. It samples the terrain grade in the
        /// travel direction and feeds it through the <i>same</i> <see cref="NavCostField.SpeedMultiplier"/> the route
        /// cost uses, so an agent's pace always matches the field it is following.
        /// </summary>
        public float SpeedMultiplierAt(Vector3 worldPos, Vector3 dirXZ) {
            if (_cost == null || _source == null) return 1f;
            Vector3 d = dirXZ; d.y = 0f;
            if (d.sqrMagnitude < 1e-8f) return 1f;
            d.Normalize();
            float run = _cost.cellWorldSize;
            float y0 = _source.SurfaceY(worldPos.x, worldPos.z);
            float y1 = _source.SurfaceY(worldPos.x + d.x * run, worldPos.z + d.z * run);
            return _cost.SpeedMultiplier((y1 - y0) / Mathf.Max(1e-4f, run));
        }

        public Vector3 CellToWorld(int x, int y) => _source.CellToWorld(x, y);

        public Vector2Int WorldToCellClamped(Vector3 worldPos) {
            Vector2 g = _navGrid.WorldToGridPosition(worldPos);
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(g.x), 0, resolution - 1),
                Mathf.Clamp(Mathf.RoundToInt(g.y), 0, resolution - 1));
        }

        /// <summary>Is a world cell walkable right now (not water/cliff/obstacle)?</summary>
        public bool IsWalkable(Vector3 worldPos) {
            Vector2Int c = WorldToCellClamped(worldPos);
            return !_cost.blocked[_cost.Index(c.x, c.y)];
        }

        /// <summary>
        /// World position of the nearest walkable ground to <paramref name="worldPos"/> (itself if already walkable).
        /// Used to un-stick agents when the terrain changes under them (sculpted into a cliff, flooded, obstacle dropped).
        /// </summary>
        public bool TryNearestWalkableWorld(Vector3 worldPos, out Vector3 walkableWorld) {
            Vector2Int c = WorldToCellClamped(worldPos);
            if (!_cost.blocked[_cost.Index(c.x, c.y)]) { walkableWorld = worldPos; return true; }
            if (TryFindNearestPassable(c, out Vector2Int near)) { walkableWorld = CellToWorld(near.x, near.y); return true; }
            walkableWorld = worldPos;
            return false;
        }

        // Spiral out from a blocked goal cell to the nearest walkable one, so an attraction placed just off the path
        // (or on a rock) still gets a valid goal.
        bool TryFindNearestPassable(Vector2Int from, out Vector2Int found) {
            for (int r = 1; r < resolution; r++) {
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;   // ring only
                        int x = from.x + dx, y = from.y + dy;
                        if (!_cost.InBounds(x, y) || _cost.blocked[_cost.Index(x, y)]) continue;
                        found = new Vector2Int(x, y);
                        return true;
                    }
            }
            found = default;
            return false;
        }

        // ---------------------------------------------------------------- helpers

        RectInt HeightmapRectToCells(RectInt heightRegion) {
            var td = terrain.terrainData;
            int hr = Mathf.Max(2, td.heightmapResolution);
            Vector3 p = terrain.transform.position;
            // Heightmap texel span -> world XZ span -> cell span. We take the bounding cell rect of the world corners
            // (the exact X/Z axis assignment doesn't matter: both corners are converted and min/max'd).
            float u0 = heightRegion.xMin / (float)(hr - 1), u1 = heightRegion.xMax / (float)(hr - 1);
            float v0 = heightRegion.yMin / (float)(hr - 1), v1 = heightRegion.yMax / (float)(hr - 1);
            var c0 = WorldToCellClamped(new Vector3(p.x + u0 * td.size.x, 0f, p.z + v0 * td.size.z));
            var c1 = WorldToCellClamped(new Vector3(p.x + u1 * td.size.x, 0f, p.z + v1 * td.size.z));
            return FromCorners(c0, c1);
        }

        static RectInt FromCorners(Vector2Int a, Vector2Int b) {
            int xMin = Mathf.Min(a.x, b.x), yMin = Mathf.Min(a.y, b.y);
            int xMax = Mathf.Max(a.x, b.x), yMax = Mathf.Max(a.y, b.y);
            return new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }

        static RectInt Union(RectInt a, RectInt b) {
            int xMin = Mathf.Min(a.xMin, b.xMin), yMin = Mathf.Min(a.yMin, b.yMin);
            int xMax = Mathf.Max(a.xMax, b.xMax), yMax = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}
