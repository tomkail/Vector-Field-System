using UnityEngine;
using VectorFields;

namespace CrowdFlow {
    /// <summary>
    /// Samples a Unity <see cref="Terrain"/> into the pathfinding core's cost grid — the production
    /// <see cref="INavCostSource"/>. Per cell it reports:
    /// <list type="bullet">
    /// <item>ground <b>height</b> (world Y) so the solver's directional climb penalty knows the slope between cells;</item>
    /// <item><b>base cost</b> raised gently on steep ground (so crowds mildly prefer the flats);</item>
    /// <item><b>blocked</b> when the cell is under the waterline, on a slope too steep to walk, or overlapped by a
    ///       collider on the "blocked" layer (buildings, walls, props).</item>
    /// </list>
    /// Every cell's world position comes from the shared <see cref="GridTransform"/> that also places the
    /// visualisation field, so the sampled cost grid, the on-screen flow and the agents' sampling are aligned by
    /// construction. This is a plain object (not a component) owned by <see cref="CrowdFlowManager"/>, and it only
    /// reads the terrain — never mutates it.
    /// </summary>
    public class TerrainNavSource : INavCostSource {
        readonly Terrain _terrain;
        readonly TerrainData _data;
        readonly GridTransform _grid;
        readonly float _posX, _posY, _posZ, _sizeX, _sizeZ;

        /// <summary>World Y below which ground is impassable (a lake/sea surface).</summary>
        public float waterLevel;
        /// <summary>Slope (degrees from flat) above which a cell is an unwalkable cliff.</summary>
        public float maxWalkableSlopeDeg = 45f;
        /// <summary>Extra base cost at the max walkable slope (0 = slope only matters through the climb penalty).</summary>
        public float slopeCostScale = 1.5f;
        /// <summary>Colliders on these layers make a cell impassable.</summary>
        public LayerMask blockedMask;
        /// <summary>Vertical span of the box used to test for blocking colliders above each cell.</summary>
        public float obstacleProbeHeight = 6f;

        public TerrainNavSource(Terrain terrain, GridTransform grid) {
            _terrain = terrain;
            _data = terrain.terrainData;
            _grid = grid;
            Vector3 p = terrain.transform.position;
            _posX = p.x; _posY = p.y; _posZ = p.z;
            _sizeX = _data.size.x; _sizeZ = _data.size.z;
        }

        /// <summary>Grid cell centre → world position, Y snapped to the terrain surface. Used by spawners/agents.</summary>
        public Vector3 CellToWorld(int x, int y) {
            Vector3 flat = _grid.GridToWorldPosition(new Vector2(x, y));   // plane XZ (its Y is the flat plane)
            flat.y = SurfaceY(flat.x, flat.z);
            return flat;
        }

        /// <summary>World Y of the terrain surface at a world XZ.</summary>
        public float SurfaceY(float worldX, float worldZ) =>
            _posY + _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));

        public void Sample(int x, int y, out float height, out float baseCost, out bool blocked) {
            Vector3 world = _grid.GridToWorldPosition(new Vector2(x, y));   // authoritative cell centre (XZ)
            float wy = SurfaceY(world.x, world.z);
            height = wy;

            // Terrain-space UV for the normal lookup (u across X, v across Z).
            float u = Mathf.Clamp01((world.x - _posX) / Mathf.Max(1e-4f, _sizeX));
            float v = Mathf.Clamp01((world.z - _posZ) / Mathf.Max(1e-4f, _sizeZ));
            Vector3 normal = _data.GetInterpolatedNormal(u, v);
            float slopeDeg = Vector3.Angle(normal, Vector3.up);
            float slope01 = Mathf.Clamp01(slopeDeg / Mathf.Max(1f, maxWalkableSlopeDeg));
            baseCost = 1f + slopeCostScale * slope01;

            // Terrain holes could be added here (TerrainData.GetHoles) if the demo ever needs cutouts; water,
            // slope and colliders cover the current requirements.
            blocked = wy < waterLevel
                      || slopeDeg > maxWalkableSlopeDeg
                      || IsColliderBlocked(world.x, world.z, wy);
        }

        bool IsColliderBlocked(float worldX, float worldZ, float wy) {
            if (blockedMask == 0) return false;
            var half = new Vector3(_sizeX / _grid.Size.x * 0.5f, obstacleProbeHeight * 0.5f, _sizeZ / _grid.Size.y * 0.5f);
            var center = new Vector3(worldX, wy + obstacleProbeHeight * 0.5f, worldZ);
            return Physics.CheckBox(center, half, Quaternion.identity, blockedMask, QueryTriggerInteraction.Ignore);
        }
    }
}
