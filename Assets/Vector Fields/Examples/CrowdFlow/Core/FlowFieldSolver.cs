using System.Collections.Generic;
using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// Solves one destination's flow field over a shared <see cref="NavCostField"/>. Two stages:
    ///
    /// 1. <b>Integration field</b> — the cost-to-reach-the-goal for every passable cell, built by a multi-source
    ///    Dijkstra expanding outward from the goal cell(s). Because <see cref="NavCostField.StepCost"/> is
    ///    directional (uphill is dearer), this is a genuine geodesic over the landscape, not just distance.
    /// 2. <b>Flow field</b> — per cell, the (grid-local) unit direction toward the lowest-integration neighbour, i.e.
    ///    steepest descent toward the goal. This is what gets written into the destination's Drawable vector field
    ///    and what agents sample to steer.
    ///
    /// One solver instance per destination; they share the cost field but keep their own integration/flow arrays.
    /// Engine-agnostic (produces a <c>Vector2[]</c>); the manager owns turning that into a rendered field.
    /// </summary>
    public class FlowFieldSolver {
        public readonly NavCostField cost;
        public readonly Vector2Int size;

        /// <summary>Cost-to-goal per cell. <see cref="Unreachable"/> for blocked or disconnected cells.</summary>
        public readonly float[] integration;
        /// <summary>Grid-local unit flow direction per cell (zero where there is nowhere better to go).</summary>
        public readonly Vector2[] flow;

        public const float Unreachable = float.PositiveInfinity;

        // Lazy-deletion binary min-heap: we push a fresh (cell, cost) entry on every relaxation rather than
        // decrease-key in place, and skip stale pops whose recorded cost is worse than the cell's settled value.
        readonly List<int> _heapCell = new List<int>();
        readonly List<float> _heapCost = new List<float>();

        // 8-neighbour offsets: 4 orthogonal (step 1) then 4 diagonal (step √2).
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly float[] DLEN = { 1f, 1f, 1f, 1f, NavCostField.Sqrt2, NavCostField.Sqrt2, NavCostField.Sqrt2, NavCostField.Sqrt2 };

        public FlowFieldSolver(NavCostField cost) {
            this.cost = cost;
            size = cost.size;
            int n = size.x * size.y;
            integration = new float[n];
            flow = new Vector2[n];
        }

        /// <summary>
        /// Rebuild the integration and flow fields for the given goal cells (blocked goals are ignored). Call
        /// whenever the cost field or the goal changes; a full solve is sub-millisecond at demo grid sizes.
        /// </summary>
        public void Solve(IReadOnlyList<Vector2Int> goalCells) {
            BuildIntegration(goalCells);
            BuildFlow();
        }

        void BuildIntegration(IReadOnlyList<Vector2Int> goalCells) {
            int n = integration.Length;
            for (int i = 0; i < n; i++) integration[i] = Unreachable;
            _heapCell.Clear();
            _heapCost.Clear();

            if (goalCells != null) {
                foreach (var g in goalCells) {
                    if (!cost.InBounds(g.x, g.y)) continue;
                    int gi = cost.Index(g.x, g.y);
                    if (cost.blocked[gi] || integration[gi] == 0f) continue;
                    integration[gi] = 0f;
                    HeapPush(gi, 0f);
                }
            }

            while (HeapPop(out int cur, out float curCost)) {
                // Stale entry (a better path to `cur` was settled after this was queued) — skip.
                if (curCost > integration[cur]) continue;

                int cx = cur % size.x, cy = cur / size.x;
                for (int d = 0; d < 8; d++) {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!cost.InBounds(nx, ny)) continue;
                    int ni = cost.Index(nx, ny);
                    if (cost.blocked[ni]) continue;
                    // No corner-cutting: a diagonal step needs both shared orthogonal cells open.
                    if (d >= 4 && (cost.blocked[cost.Index(cx + DX[d], cy)] || cost.blocked[cost.Index(cx, cy + DY[d])]))
                        continue;

                    // The neighbour `ni` is a cell an agent would stand on and step INTO `cur` from, so the edge
                    // cost is the (directional) cost of moving ni -> cur.
                    float tentative = curCost + cost.StepCost(ni, cur, DLEN[d]);
                    if (tentative < integration[ni]) {
                        integration[ni] = tentative;
                        HeapPush(ni, tentative);
                    }
                }
            }
        }

        void BuildFlow() {
            for (int y = 0; y < size.y; y++) {
                for (int x = 0; x < size.x; x++) {
                    int i = cost.Index(x, y);
                    flow[i] = Vector2.zero;
                    if (cost.blocked[i] || integration[i] == Unreachable) continue;

                    // Steepest descent: pick the neighbour with the lowest cost-to-goal and point at it.
                    float best = integration[i];
                    int bestDx = 0, bestDy = 0;
                    for (int d = 0; d < 8; d++) {
                        int nx = x + DX[d], ny = y + DY[d];
                        if (!cost.InBounds(nx, ny)) continue;
                        int ni = cost.Index(nx, ny);
                        if (cost.blocked[ni] || integration[ni] == Unreachable) continue;
                        if (d >= 4 && (cost.blocked[cost.Index(x + DX[d], y)] || cost.blocked[cost.Index(x, y + DY[d])]))
                            continue;
                        if (integration[ni] < best) {
                            best = integration[ni];
                            bestDx = DX[d]; bestDy = DY[d];
                        }
                    }
                    if (bestDx != 0 || bestDy != 0)
                        flow[i] = new Vector2(bestDx, bestDy).normalized;
                }
            }
        }

        // --- lazy-deletion binary min-heap over (cell, cost) --------------------------------------------------
        void HeapPush(int cell, float cost) {
            _heapCell.Add(cell);
            _heapCost.Add(cost);
            int c = _heapCell.Count - 1;
            while (c > 0) {
                int p = (c - 1) >> 1;
                if (_heapCost[p] <= _heapCost[c]) break;
                Swap(p, c);
                c = p;
            }
        }

        bool HeapPop(out int cell, out float cost) {
            int count = _heapCell.Count;
            if (count == 0) { cell = 0; cost = 0f; return false; }
            cell = _heapCell[0];
            cost = _heapCost[0];
            int last = count - 1;
            _heapCell[0] = _heapCell[last];
            _heapCost[0] = _heapCost[last];
            _heapCell.RemoveAt(last);
            _heapCost.RemoveAt(last);
            count--;
            int p = 0;
            while (true) {
                int l = 2 * p + 1, r = l + 1, s = p;
                if (l < count && _heapCost[l] < _heapCost[s]) s = l;
                if (r < count && _heapCost[r] < _heapCost[s]) s = r;
                if (s == p) break;
                Swap(p, s);
                p = s;
            }
            return true;
        }

        void Swap(int a, int b) {
            (_heapCell[a], _heapCell[b]) = (_heapCell[b], _heapCell[a]);
            (_heapCost[a], _heapCost[b]) = (_heapCost[b], _heapCost[a]);
        }
    }
}
