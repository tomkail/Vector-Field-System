using UnityEngine;

namespace CrowdFlow {
    /// <summary>
    /// One theme-park visitor. It walks toward its current <see cref="Attraction"/> by sampling that destination's
    /// flow field (<see cref="CrowdFlowManager.FlowDir"/>), nudged apart from its neighbours for a crowd look, then
    /// dwells on arrival and picks somewhere new to go. Kinematic (no Rigidbody) like Windfall's glider, so motion is
    /// predictable and framerate-independent. Ticked by <see cref="CrowdDirector"/>, which owns the neighbour queries.
    /// </summary>
    public class CrowdAgent : MonoBehaviour {
        public enum State { Traveling, Dwelling }

        [System.NonSerialized] public int index;      // slot in the director's arrays (for the spatial hash)
        [System.NonSerialized] public int dest = -1;   // current destination / flow-field index
        [System.NonSerialized] public State state = State.Traveling;

        CrowdFlowManager _mgr;
        CrowdDirector _dir;
        float _dwellTimer;
        Vector3 _velocity;
        float _speedMul = 1f;    // per-agent random speed scale, so the crowd doesn't move in lockstep
        float _groundOffset;     // half the visual height, so the body sits ON the ground instead of half-buried

        /// <summary>Current XZ velocity — read by the director's flocking (neighbour alignment).</summary>
        public Vector3 Velocity => _velocity;

        public void Init(CrowdFlowManager mgr, CrowdDirector dir, int index, int dest) {
            _mgr = mgr; _dir = dir; this.index = index;
            _speedMul = Random.Range(0.78f, 1.24f);   // slight per-visitor pace variation
            var r = GetComponentInChildren<Renderer>();
            _groundOffset = r != null ? r.bounds.extents.y : 0.5f;   // stand on the surface, not sunk into it
            SetDestination(dest);
        }

        public void SetDestination(int d) {
            dest = d;
            state = State.Traveling;
            TintTo(d);
        }

        public void Tick(float dt) {
            if (_mgr == null) return;
            Vector3 pos = transform.position;

            // Terrain changed under us (sculpted into a cliff, flooded, obstacle dropped) — we're now on blocked
            // ground. Walk straight to the nearest walkable cell, ignoring the normal step-guard, so we never freeze.
            if (!_mgr.IsWalkable(pos)) { Escape(dt, pos); return; }

            if (state == State.Dwelling) {
                _dwellTimer -= dt;
                _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, _dir.moveSpeed * dt * 2f);
                if (_dwellTimer <= 0f) _dir.RetargetAgent(this);
            } else {
                var attraction = _mgr.GetAttraction(dest);
                if (attraction != null) {
                    Vector3 flat = pos; flat.y = 0f;
                    Vector3 goal = attraction.transform.position; goal.y = 0f;
                    if ((flat - goal).sqrMagnitude <= attraction.arriveRadius * attraction.arriveRadius) {
                        Arrive(attraction);
                        return;
                    }
                }

                Vector3 flow = _mgr.FlowDir(dest, pos);
                if (flow == Vector3.zero) {
                    // Unreachable from here (e.g. water rose across the route) — pick somewhere reachable instead.
                    _dir.RetargetAgent(this);
                    return;
                }
                Vector3 flock = _dir.Flock(index, pos);
                // Slow going uphill / speed up downhill, using the SAME slope→speed model the flow field was costed
                // with — so the crowd physically moves at the pace the field's timings assume.
                float slopeMul = _mgr.SpeedMultiplierAt(pos, flow);
                Vector3 desired = (flow * (_dir.moveSpeed * _speedMul * slopeMul)) + flock;

                // Smooth, framerate-independent approach to the desired velocity.
                float k = 1f - Mathf.Exp(-_dir.steerResponse * dt);
                _velocity = Vector3.Lerp(_velocity, desired, k);
            }

            // Integrate on the XZ plane, then reject a step that would walk into water/cliff/obstacle.
            Vector3 step = _velocity * dt; step.y = 0f;
            Vector3 next = pos + step;
            if (step.sqrMagnitude > 1e-6f && !_mgr.IsWalkable(next)) {
                _velocity *= 0.5f;
                next = pos;   // hold at the edge; flow will steer away next frame
            }
            next.y = _mgr.SurfaceY(next.x, next.z) + _groundOffset;
            transform.position = next;

            if (_velocity.sqrMagnitude > 0.01f) {
                Vector3 face = _velocity; face.y = 0f;
                if (face.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 0.2f);
            }
        }

        void Arrive(Attraction a) {
            state = State.Dwelling;
            _dwellTimer = a.RandomDwell();
        }

        // Un-stick: steer straight toward the nearest walkable ground and move there directly (no walkable step-guard,
        // so we can climb out of a region that just became blocked). Retargets to travelling once clear.
        void Escape(float dt, Vector3 pos) {
            state = State.Traveling;
            if (!_mgr.TryNearestWalkableWorld(pos, out Vector3 target)) return;
            Vector3 d = target - pos; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            float k = 1f - Mathf.Exp(-_dir.steerResponse * dt);
            _velocity = Vector3.Lerp(_velocity, d.normalized * _dir.moveSpeed, k);
            Vector3 step = _velocity * dt; step.y = 0f;
            Vector3 next = pos + step;
            next.y = _mgr.SurfaceY(next.x, next.z) + _groundOffset;
            transform.position = next;
        }

        void TintTo(int d) {
            var a = _mgr.GetAttraction(d);
            if (a == null) return;
            var mr = GetComponentInChildren<MeshRenderer>();
            if (mr == null) return;
            var m = mr.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", a.color);
            if (m.HasProperty("_Color")) m.color = a.color;
            // Emissive glow so each visitor reads clearly (and colour-codes its current destination).
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", a.color * 1.5f);
        }
    }
}
