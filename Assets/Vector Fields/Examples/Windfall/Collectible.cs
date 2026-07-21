using UnityEngine;

namespace Windfall {
    /// <summary>
    /// A scattered pickup worth points, collected when a player flies through it (GAME_DESIGN.md §9a hints).
    /// Overlap is resolved by <see cref="WindfallGame"/> (circle-vs-circle) so no physics colliders are needed.
    /// Collecting just hides the renderers; <see cref="ResetCollectible"/> restores it on level reset.
    /// </summary>
    public class Collectible : MonoBehaviour {
        [Tooltip("Points awarded to the player that collects this.")]
        public int points = 10;
        [Tooltip("Pickup radius, world units (added to the player's radius for the overlap test).")]
        public float radius = 0.6f;

        public bool Collected { get; private set; }

        Renderer[] _renderers;

        void Awake() => _renderers = GetComponentsInChildren<Renderer>(true);

        public void Collect() {
            if (Collected) return;
            Collected = true;
            SetVisible(false);
        }

        public void ResetCollectible() {
            Collected = false;
            SetVisible(true);
        }

        void SetVisible(bool v) {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _renderers) if (r != null) r.enabled = v;
        }

        void OnDrawGizmos() {
            Gizmos.color = Collected ? Color.gray : new Color(1f, 0.9f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
