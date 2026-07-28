using System.Collections.Generic;
using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// Spawns and drives the crowd. It seeds N <see cref="CrowdAgent"/>s on walkable ground, rebuilds a
    /// <see cref="CrowdSpatialHash"/> each frame, and ticks every agent (steer along its destination's flow +
    /// separate from neighbours). Retargeting (arrival → dwell → new attraction) is centralised here so agents don't
    /// each scan the scene. Agent visuals are simple primitives tinted by destination, created at runtime — the scene
    /// only needs the terrain, the manager, the attractions and this director.
    /// </summary>
    [RequireComponent(typeof(CrowdFlowManager))]
    public class CrowdDirector : MonoBehaviour {
        [Header("Crowd")]
        [Tooltip("How many visitors to spawn.")]
        public int agentCount = 250;
        [Tooltip("Optional agent prefab (needs a MeshRenderer). If empty, a small capsule is generated.")]
        public GameObject agentPrefab;
        [Tooltip("Agent height/scale in world units when auto-generated.")]
        public float agentSize = 3f;

        [Header("Movement / feel")]
        public float moveSpeed = 5f;
        [Tooltip("How snappily velocity approaches the desired direction (higher = snappier).")]
        public float steerResponse = 6f;

        [Header("Flocking")]
        [Tooltip("Personal space: neighbours closer than this push the agent away (avoid each other / spread out).")]
        public float separationRadius = 1.8f;
        [Tooltip("Strength of the separation push relative to the flow steering.")]
        public float separationWeight = 5f;
        [Tooltip("Sensing radius for alignment & cohesion (should be >= separation radius).")]
        public float neighborRadius = 3.5f;
        [Tooltip("How strongly an agent matches its neighbours' heading (gives natural streams/lanes).")]
        public float alignmentWeight = 0.5f;
        [Tooltip("Pull toward the neighbours' centre. Left at 0 by default — the flow field already groups the " +
                 "crowd, so cohesion would clump them against the goal of spreading out. Raise for tighter packs.")]
        public float cohesionWeight = 0f;

        CrowdFlowManager _mgr;
        readonly List<CrowdAgent> _agents = new List<CrowdAgent>();
        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<Vector3> _velocities = new List<Vector3>();
        readonly List<int> _neighborScratch = new List<int>(32);
        readonly CrowdSpatialHash _hash = new CrowdSpatialHash();
        bool _spawned;

        /// <summary>Number of visitors actually spawned and walking (for HUD readouts).</summary>
        public int SpawnedCount => _agents.Count;

        void Awake() => _mgr = GetComponent<CrowdFlowManager>();

        void Update() {
            // The manager solves in its own Start(); wait until it's ready before spawning (Start order is undefined).
            if (!_spawned) {
                if (_mgr.DestinationCount == 0) return;
                SpawnCrowd();
                _spawned = true;
            }

            float dt = Time.deltaTime;
            _positions.Clear();
            _velocities.Clear();
            for (int i = 0; i < _agents.Count; i++) {
                _positions.Add(_agents[i].transform.position);
                _velocities.Add(_agents[i].Velocity);
            }
            _hash.Rebuild(_positions, Mathf.Max(separationRadius, neighborRadius));
            for (int i = 0; i < _agents.Count; i++) _agents[i].Tick(dt);
        }

        void SpawnCrowd() {
            var parent = new GameObject("Crowd").transform;
            parent.SetParent(transform, false);
            for (int i = 0; i < agentCount; i++) {
                if (!TryRandomWalkableWorld(out Vector3 pos)) continue;
                var go = agentPrefab != null ? Instantiate(agentPrefab) : MakeCapsule();
                go.transform.SetParent(parent, false);
                go.transform.position = pos;
                go.name = "Visitor_" + i;
                var agent = go.GetComponent<CrowdAgent>();
                if (agent == null) agent = go.AddComponent<CrowdAgent>();
                int dest = Random.Range(0, _mgr.DestinationCount);
                agent.Init(_mgr, this, _agents.Count, dest);
                _agents.Add(agent);
            }
        }

        GameObject MakeCapsule() {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // agents don't collide physically; separation is handled in steering
            go.transform.localScale = new Vector3(agentSize * 0.5f, agentSize * 0.5f, agentSize * 0.5f);
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null) mr.material = new Material(sh);
            return go;
        }

        bool TryRandomWalkableWorld(out Vector3 world) {
            var size = _mgr.GridSize;
            for (int attempt = 0; attempt < 40; attempt++) {
                int x = Random.Range(0, size.x), y = Random.Range(0, size.y);
                Vector3 w = _mgr.CellToWorld(x, y);
                if (_mgr.IsWalkable(w)) { world = w; return true; }
            }
            world = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Boids-style flocking steer for one agent: separation (push out of others' personal space) + alignment
        /// (match neighbours' heading) + optional cohesion (pull to the neighbours' centre). Returned as a world-space
        /// XZ acceleration to add to the flow steering. One neighbour query feeds all three rules.
        /// </summary>
        public Vector3 Flock(int index, Vector3 pos) {
            float radius = Mathf.Max(separationRadius, neighborRadius);
            _hash.Query(pos, radius, index, _neighborScratch);
            int count = _neighborScratch.Count;
            if (count == 0) return Vector3.zero;

            Vector3 separation = Vector3.zero;
            Vector3 avgVel = Vector3.zero;
            Vector3 avgPos = Vector3.zero;
            for (int i = 0; i < count; i++) {
                int idx = _neighborScratch[i];
                Vector3 other = _positions[idx];
                Vector3 d = pos - other; d.y = 0f;
                float dist = d.magnitude;
                // Separation: only within personal space, stronger the closer they are.
                if (dist > 1e-4f && dist < separationRadius) separation += (d / dist) * (1f - dist / separationRadius);
                avgVel += _velocities[idx];
                avgPos += other;
            }

            Vector3 steer = separation * separationWeight;

            // Alignment: nudge toward the average neighbour heading (smooths the crowd into streams/lanes).
            Vector3 alignDir = avgVel / count; alignDir.y = 0f;
            if (alignDir.sqrMagnitude > 1e-4f) steer += alignDir.normalized * (moveSpeed * alignmentWeight);

            // Cohesion (off by default): steer toward the neighbours' centre of mass.
            if (cohesionWeight > 0f) {
                Vector3 toCentre = (avgPos / count) - pos; toCentre.y = 0f;
                steer += toCentre * cohesionWeight;
            }
            return steer;
        }

        /// <summary>Send an agent to a fresh, reachable destination (called on arrival-dwell-end or when its goal became unreachable).</summary>
        public void RetargetAgent(CrowdAgent agent) {
            int count = _mgr.DestinationCount;
            if (count == 0) return;
            Vector3 pos = agent.transform.position;
            // Prefer a different, reachable destination; fall back to any reachable one, then to any at all.
            int start = Random.Range(0, count);
            int fallback = -1;
            for (int k = 0; k < count; k++) {
                int d = (start + k) % count;
                if (!_mgr.IsReachable(d, pos)) continue;
                if (fallback < 0) fallback = d;
                if (d != agent.dest) { agent.SetDestination(d); return; }
            }
            agent.SetDestination(fallback >= 0 ? fallback : start);
        }
    }
}
