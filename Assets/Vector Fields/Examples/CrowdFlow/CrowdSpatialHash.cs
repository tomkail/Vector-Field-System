using System.Collections.Generic;
using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// A uniform spatial hash over agent XZ positions, rebuilt each frame, used for cheap neighbour queries (crowd
    /// separation and arrival tests). Buckets are keyed by integer cell so a query only scans the 3×3 cells around a
    /// point rather than every agent — keeping hundreds–low-thousands of agents cheap. Deliberately simple: no RVO,
    /// just "who's near me".
    /// </summary>
    public class CrowdSpatialHash {
        readonly Dictionary<long, List<int>> _buckets = new Dictionary<long, List<int>>();
        readonly Stack<List<int>> _pool = new Stack<List<int>>();
        IReadOnlyList<Vector3> _positions;
        float _cellSize = 2f;

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        int CellCoord(float v) => Mathf.FloorToInt(v / _cellSize);

        /// <summary>Rebuild the hash from the current agent positions. <paramref name="cellSize"/> should be ~the query radius.</summary>
        public void Rebuild(IReadOnlyList<Vector3> positions, float cellSize) {
            _positions = positions;
            _cellSize = Mathf.Max(0.1f, cellSize);
            foreach (var kv in _buckets) { kv.Value.Clear(); _pool.Push(kv.Value); }
            _buckets.Clear();
            for (int i = 0; i < positions.Count; i++) {
                Vector3 p = positions[i];
                long k = Key(CellCoord(p.x), CellCoord(p.z));
                if (!_buckets.TryGetValue(k, out var list)) {
                    list = _pool.Count > 0 ? _pool.Pop() : new List<int>();
                    _buckets[k] = list;
                }
                list.Add(i);
            }
        }

        /// <summary>Fill <paramref name="result"/> with the indices of agents within <paramref name="radius"/> of <paramref name="pos"/> (excluding <paramref name="self"/>).</summary>
        public void Query(Vector3 pos, float radius, int self, List<int> result) {
            result.Clear();
            if (_positions == null) return;
            int cx = CellCoord(pos.x), cz = CellCoord(pos.z);
            float r2 = radius * radius;
            int span = Mathf.CeilToInt(radius / _cellSize);
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++) {
                    if (!_buckets.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                    for (int j = 0; j < list.Count; j++) {
                        int idx = list[j];
                        if (idx == self) continue;
                        Vector3 d = _positions[idx] - pos; d.y = 0f;
                        if (d.sqrMagnitude <= r2) result.Add(idx);
                    }
                }
        }
    }
}
