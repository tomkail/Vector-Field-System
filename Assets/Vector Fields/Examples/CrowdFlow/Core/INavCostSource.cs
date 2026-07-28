namespace CrowdFlow {
    /// <summary>
    /// The seam the pathfinding core sits behind. A cost source answers, per grid cell, "how high is the ground
    /// here, how expensive is it to walk on, and is it walkable at all". The core (<see cref="NavCostField"/>,
    /// <see cref="FlowFieldSolver"/>) knows nothing about Unity Terrain, colliders or vector fields — only this
    /// interface — so it stays engine-agnostic and unit-testable. <c>TerrainNavSource</c> is the production
    /// implementation; tests use a hand-built one.
    /// </summary>
    public interface INavCostSource {
        /// <summary>
        /// Sample cell (x, y). Returns the ground <paramref name="height"/> (world units, for the directional climb
        /// penalty), the flat-ground traversal cost <paramref name="baseCost"/> (≥1; higher = slower terrain), and
        /// whether the cell is <paramref name="blocked"/> (under water, a hole, or overlapped by a blocking collider).
        /// Coordinates are always in range; the method never fails, so it returns void.
        /// </summary>
        void Sample(int x, int y, out float height, out float baseCost, out bool blocked);
    }
}
