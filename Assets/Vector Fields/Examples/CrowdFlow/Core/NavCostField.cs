using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// The shared, landscape-derived cost grid every destination's flow field is solved over. Holds, per cell:
    /// passability, flat-ground base cost, and ground height. From those it produces the <b>directional</b> step
    /// cost used by <see cref="FlowFieldSolver"/>.
    ///
    /// The step cost is literally <b>travel time</b>: <c>distance / walking-speed</c>, where the walking speed is a
    /// function of the slope taken in the direction of travel (<see cref="SpeedMultiplier"/>). Uphill is slow, so it
    /// is dear; downhill is fast, so it is cheap — the cost and the agents' actual speed come from the <i>same</i>
    /// function (<see cref="CrowdAgent"/> scales its speed by <see cref="SpeedMultiplier"/> along the flow), so the
    /// route the field chooses is the genuinely fastest one and the agents' timings match it by construction. That is
    /// what makes crowds prefer to skirt hills rather than climb them.
    ///
    /// It is populated from an <see cref="INavCostSource"/> (Unity Terrain in the demo) and can be rebuilt a
    /// sub-rect at a time (<see cref="RebuildRegion"/>) so a local landscape edit only re-samples the cells it
    /// actually touched. The grid is engine-agnostic — a plain array structure, no MonoBehaviour, no Terrain.
    /// </summary>
    public class NavCostField {
        public readonly Vector2Int size;

        // One entry per cell (index = y * size.x + x).
        public readonly bool[] blocked;
        public readonly float[] baseCost;   // flat-ground traversal cost multiplier, ≥ 1 (terrain type; 1 = plain grass)
        public readonly float[] height;      // world-space ground height, for the slope-speed model

        /// <summary>Horizontal world distance spanned by one orthogonal cell step (used to turn cell steps into a real grade).</summary>
        public float cellWorldSize = 1f;

        // --- slope → walking-speed model (shared by the cost and the agents) --------------------------------------
        // Speed is expressed as a multiplier of flat-ground speed, as a function of the signed grade (rise/run) taken
        // in the direction of travel. Uphill (grade > 0) slows down; downhill (grade < 0) speeds up. Both clamped so
        // steep ground never crawls to a full stop and downhill never turns into a runaway sprint.
        /// <summary>How hard uphill slows the walk (0 = slope free; higher = crowds avoid climbing harder).</summary>
        public float uphillSlow = 3f;
        /// <summary>How much downhill speeds the walk up (0 = no downhill bonus).</summary>
        public float downhillSpeedup = 0.6f;
        /// <summary>Slowest a steep-but-walkable uphill step may get (as a fraction of flat speed).</summary>
        public float minSpeedMul = 0.15f;
        /// <summary>Fastest a downhill step may get (as a multiple of flat speed).</summary>
        public float maxSpeedMul = 1.6f;

        public const float Sqrt2 = 1.41421356f;

        public NavCostField(Vector2Int size) {
            this.size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
            int n = this.size.x * this.size.y;
            blocked = new bool[n];
            baseCost = new float[n];
            height = new float[n];
        }

        public int Index(int x, int y) => y * size.x + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < size.x && y < size.y;

        /// <summary>Rebuild every cell from the source (a full re-sample of the landscape).</summary>
        public void RebuildAll(INavCostSource source) =>
            RebuildRegion(new RectInt(0, 0, size.x, size.y), source);

        /// <summary>
        /// Re-sample only the cells inside <paramref name="region"/> (clamped to the grid) from the source. Used
        /// when the landscape changes locally: Terrain's <c>heightmapChanged</c> hands us the edited rect, we
        /// convert it to cells and refresh just those. Everything outside is left untouched.
        /// </summary>
        public void RebuildRegion(RectInt region, INavCostSource source) {
            int x0 = Mathf.Max(0, region.xMin), y0 = Mathf.Max(0, region.yMin);
            int x1 = Mathf.Min(size.x, region.xMax), y1 = Mathf.Min(size.y, region.yMax);
            for (int y = y0; y < y1; y++) {
                for (int x = x0; x < x1; x++) {
                    source.Sample(x, y, out float h, out float c, out bool b);
                    int i = Index(x, y);
                    height[i] = h;
                    baseCost[i] = Mathf.Max(1f, c);
                    blocked[i] = b;
                }
            }
        }

        /// <summary>
        /// Walking-speed multiplier (vs flat ground) for a step whose slope in the direction of travel is
        /// <paramref name="grade"/> (signed rise/run; positive = uphill). Uphill slows asymptotically toward
        /// <see cref="minSpeedMul"/>, never to a stop; downhill speeds up linearly, capped at <see cref="maxSpeedMul"/>.
        /// This is the single source of truth for "how fast is this slope" — the cost below divides by it, and the
        /// agents multiply their speed by it, so the field and the crowd stay in lockstep.
        /// </summary>
        public float SpeedMultiplier(float grade) {
            float m = grade >= 0f
                ? 1f / (1f + uphillSlow * grade)          // uphill: bounded (0,1], monotic slowdown
                : 1f + downhillSpeedup * (-grade);        // downhill: linear speed-up
            return Mathf.Clamp(m, minSpeedMul, maxSpeedMul);
        }

        /// <summary>
        /// Cost for an agent to step <b>from</b> cell <paramref name="from"/> <b>to</b> cell <paramref name="to"/>
        /// (both passable, adjacent). This is the <b>time</b> to make the step: the world distance travelled divided
        /// by the walking speed on that slope (<see cref="SpeedMultiplier"/>), times the cell's terrain-type base
        /// cost. Because the speed drops going uphill, the cost rises there — and it rises for exactly the reason the
        /// agent actually slows, so "expensive" and "slow" are the same thing. Deliberately asymmetric: the reverse
        /// step back up the same slope is dearer.
        /// </summary>
        public float StepCost(int from, int to, float stepLength) {
            float run = stepLength * cellWorldSize;                       // horizontal world distance of the step
            float grade = (height[to] - height[from]) / Mathf.Max(1e-4f, run);
            return baseCost[to] * run / SpeedMultiplier(grade);           // time = distance / speed
        }
    }
}
