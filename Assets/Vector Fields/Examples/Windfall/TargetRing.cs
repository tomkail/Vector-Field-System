using UnityEngine;

namespace Windfall {
    /// <summary>
    /// The scoring target (GAME_DESIGN.md §5): concentric zones, bullseye style. A player scores when it
    /// settles inside — inner zones are worth more. Lies flat in the XY plane like the field; the zone radii
    /// are world units from this object's position. Draws visible in-game rings (LineRenderers built at runtime)
    /// plus Scene-view gizmos. Add scattered <see cref="Collectible"/>s for the in-flight bonus points.
    /// </summary>
    public class TargetRing : MonoBehaviour {
        [System.Serializable]
        public struct Zone {
            [Tooltip("Radius of this zone, world units from the ring centre.")] public float radius;
            [Tooltip("Points awarded for settling inside this zone (and no smaller one).")] public int points;
            public Color color;
        }

        [Tooltip("Concentric zones. Order doesn't matter — the smallest zone containing the point wins.")]
        public Zone[] zones = {
            new Zone { radius = 1.0f, points = 100, color = new Color(1f, 0.9f, 0.25f) },
            new Zone { radius = 2.2f, points = 50,  color = new Color(1f, 0.6f, 0.2f) },
            new Zone { radius = 3.5f, points = 25,  color = new Color(0.9f, 0.35f, 0.35f) },
        };

        public Vector2 Center => (Vector2)transform.position;

        public float OuterRadius {
            get { float m = 0f; if (zones != null) foreach (var z in zones) m = Mathf.Max(m, z.radius); return m; }
        }

        /// <summary>True if the world position is within the outermost zone.</summary>
        public bool Contains(Vector2 worldPos) => (worldPos - Center).sqrMagnitude <= OuterRadius * OuterRadius;

        /// <summary>Points for the smallest zone that contains the position (0 if outside all zones).</summary>
        public int ScoreAt(Vector2 worldPos) {
            float d = (worldPos - Center).magnitude;
            int best = 0; float bestR = float.MaxValue;
            if (zones != null) foreach (var z in zones)
                if (d <= z.radius && z.radius < bestR) { bestR = z.radius; best = z.points; }
            return best;
        }

        void Awake() => BuildVisuals();

        // Runtime LineRenderer circles so the target is visible in the Game view (gizmos are Scene-view only).
        void BuildVisuals() {
            if (zones == null) return;
            var mat = new Material(Shader.Find("Sprites/Default"));
            foreach (var z in zones) {
                var go = new GameObject("Ring_" + z.points);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.widthMultiplier = 0.08f;
                lr.material = mat;
                lr.startColor = lr.endColor = z.color;
                const int seg = 64;
                lr.positionCount = seg;
                for (int i = 0; i < seg; i++) {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * z.radius, Mathf.Sin(a) * z.radius, 0f));
                }
            }
        }

        void OnDrawGizmos() {
            if (zones == null) return;
            foreach (var z in zones) { Gizmos.color = z.color; DrawCircle(transform.position, z.radius); }
        }

        static void DrawCircle(Vector3 c, float r) {
            const int seg = 48;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= seg; i++) {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
