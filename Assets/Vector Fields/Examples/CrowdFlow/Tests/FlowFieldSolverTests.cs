using NUnit.Framework;
using UnityEngine;
using CrowdFlow;

namespace CrowdFlow.Tests {
    /// <summary>
    /// Unit tests for the engine-agnostic pathfinding core (<see cref="NavCostField"/> + <see cref="FlowFieldSolver"/>).
    /// A hand-built <see cref="ArrayNavSource"/> stands in for Terrain so the algorithm is tested with no scene,
    /// no play mode and fully deterministic input.
    /// </summary>
    public class FlowFieldSolverTests {
        /// <summary>An <see cref="INavCostSource"/> backed by plain arrays, so tests author the landscape directly.</summary>
        class ArrayNavSource : INavCostSource {
            public int w, h;
            public float[] height;
            public float[] baseCost;
            public bool[] blocked;
            public void Sample(int x, int y, out float ht, out float bc, out bool bl) {
                int i = y * w + x;
                ht = height != null ? height[i] : 0f;
                bc = baseCost != null ? baseCost[i] : 1f;
                bl = blocked != null && blocked[i];
            }
        }

        static (NavCostField, FlowFieldSolver, ArrayNavSource) Build(int w, int h, float uphillSlow = 3f) {
            var src = new ArrayNavSource {
                w = w, h = h,
                height = new float[w * h],
                baseCost = new float[w * h],
                blocked = new bool[w * h],
            };
            for (int i = 0; i < w * h; i++) src.baseCost[i] = 1f;
            // cellWorldSize = 1 so a one-cell step spans one world unit and grade = rise directly (keeps the maths
            // in these tests hand-checkable). Downhill speed-up off and the speed clamps opened out so flat/uphill
            // relationships are exact rather than clipped.
            var field = new NavCostField(new Vector2Int(w, h)) {
                cellWorldSize = 1f, uphillSlow = uphillSlow, downhillSpeedup = 0f, minSpeedMul = 0f, maxSpeedMul = 1f,
            };
            var solver = new FlowFieldSolver(field);
            return (field, solver, src);
        }

        [Test]
        public void OpenField_EveryCellReachable_AndFlowsTowardGoal() {
            var (field, solver, src) = Build(16, 16);
            field.RebuildAll(src);
            var goal = new Vector2Int(15, 15);
            solver.Solve(new[] { goal });

            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++) {
                    int i = field.Index(x, y);
                    Assert.Less(solver.integration[i], FlowFieldSolver.Unreachable, $"cell {x},{y} should be reachable");
                }

            // The goal cell has zero cost; a corner cell far away should have the largest.
            Assert.AreEqual(0f, solver.integration[field.Index(15, 15)], 1e-4f);
            Assert.Greater(solver.integration[field.Index(0, 0)], 0f);
        }

        [Test]
        public void Flow_AlwaysPointsToAStrictlyLowerNeighbour() {
            var (field, solver, src) = Build(24, 24);
            field.RebuildAll(src);
            var goal = new Vector2Int(12, 12);
            solver.Solve(new[] { goal });

            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++) {
                    int i = field.Index(x, y);
                    if (solver.integration[i] == 0f) continue;                 // goal: no outgoing flow required
                    Vector2 f = solver.flow[i];
                    if (f == Vector2.zero) continue;
                    int nx = x + Mathf.RoundToInt(f.x), ny = y + Mathf.RoundToInt(f.y);
                    Assert.IsTrue(field.InBounds(nx, ny), $"flow at {x},{y} points off-grid");
                    int ni = field.Index(nx, ny);
                    Assert.Less(solver.integration[ni], solver.integration[i],
                        $"flow at {x},{y} must descend the cost gradient");
                }
        }

        [Test]
        public void Wall_WithGap_RoutesAroundAndKeepsAllCellsReachable() {
            // Vertical wall at x=5 spanning y=0..8, leaving a gap at the top (y=9).
            var (field, solver, src) = Build(10, 10);
            for (int y = 0; y <= 8; y++) src.blocked[y * 10 + 5] = true;
            field.RebuildAll(src);
            var goal = new Vector2Int(9, 0);   // right of the wall
            solver.Solve(new[] { goal });

            // A cell on the far (left) side, low down, must still reach the goal — only path is up and over the gap,
            // so its cost is well above the straight-line distance.
            int left = field.Index(0, 0);
            Assert.Less(solver.integration[left], FlowFieldSolver.Unreachable, "left side must remain reachable via the gap");
            Assert.Greater(solver.integration[left], 9f, "detour over the gap should cost more than the direct distance");

            // Blocked cells never enter the field.
            Assert.AreEqual(FlowFieldSolver.Unreachable, solver.integration[field.Index(5, 3)]);
            Assert.AreEqual(Vector2.zero, solver.flow[field.Index(5, 3)]);
        }

        [Test]
        public void SealedRegion_IsUnreachable() {
            // Fully wall off cell (0,0) from the rest with an L of blocked cells.
            var (field, solver, src) = Build(8, 8);
            src.blocked[field.Index(1, 0)] = true;
            src.blocked[field.Index(1, 1)] = true;
            src.blocked[field.Index(0, 1)] = true;
            field.RebuildAll(src);
            solver.Solve(new[] { new Vector2Int(7, 7) });

            Assert.AreEqual(FlowFieldSolver.Unreachable, solver.integration[field.Index(0, 0)],
                "diagonal corner-cutting must not leak a path through the sealed diagonal");
        }

        [Test]
        public void StepCost_IsDirectional_UphillDearerThanDownhill() {
            // The core asymmetry the whole "crowds avoid hills" behaviour rests on: the step cost is travel time
            // (distance / slope-speed), so entering a higher cell (slower) costs more than entering a lower one.
            // With cellWorldSize = 1 and downhill speed-up off, a one-unit uphill step of rise r costs 1 + uphillSlow*r.
            var (field, _, _) = Build(2, 1, uphillSlow: 4f);
            int low = field.Index(0, 0), high = field.Index(1, 0);
            field.baseCost[low] = 1f; field.baseCost[high] = 1f;   // flat-ground cost (no RebuildAll here)
            field.height[low] = 0f;
            field.height[high] = 3f;
            float uphill = field.StepCost(low, high, 1f);     // 0 -> 3
            float downhill = field.StepCost(high, low, 1f);   // 3 -> 0
            Assert.Greater(uphill, downhill, "uphill must cost more than downhill");
            Assert.AreEqual(1f, downhill, 1e-4f, "a downhill/flat step is the flat-ground baseline (no speed-up here)");
            Assert.AreEqual(1f + 4f * 3f, uphill, 1e-4f, "uphill time = distance / (1/(1 + uphillSlow*rise))");
        }

        [Test]
        public void UphillSlow_MakesTheFarSideOfAHillDearerToReach() {
            // A ridge column (x=5) walls the left half off behind high ground. Reaching the flat left edge from a
            // goal on the right must be far dearer WITH the uphill slowdown than without it, proving the slope-speed
            // model actually reshapes the geodesic (and would steer a crowd around the hill given any flat detour).
            const int w = 11, h = 3;
            int leftEdge;

            float FlatSolve(float uphillSlow) {
                var (field, solver, src) = Build(w, h, uphillSlow);
                for (int y = 0; y < h; y++) src.height[field.Index(5, y)] = 5f;   // a 5-unit ridge, full height
                field.RebuildAll(src);
                solver.Solve(new[] { new Vector2Int(10, 1) });
                leftEdge = field.Index(0, 1);
                return solver.integration[leftEdge];
            }

            float noPenalty = FlatSolve(0f);
            float withPenalty = FlatSolve(8f);
            Assert.Greater(withPenalty, noPenalty * 3f,
                "climbing the ridge should dominate the cost once the uphill slowdown is on");
            Assert.Less(withPenalty, FlowFieldSolver.Unreachable, "the left side is still reachable, just expensive");
        }
    }
}
