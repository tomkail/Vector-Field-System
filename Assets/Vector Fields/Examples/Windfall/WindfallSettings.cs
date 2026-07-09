using UnityEngine;

namespace Windfall {
    /// <summary>
    /// All of Windfall's feel constants in one asset so they can be tuned LIVE in play mode
    /// (the player reads these every frame rather than caching them). See GAME_DESIGN.md §3a.
    /// Create via Assets ▸ Create ▸ Windfall ▸ Settings. Ship a couple of presets to A/B feel.
    /// </summary>
    [CreateAssetMenu(menuName = "Windfall/Settings", fileName = "WindfallSettings")]
    public class WindfallSettings : ScriptableObject {
        [Header("Launch — direction sweep (§2)")]
        [Tooltip("Centre of the direction pendulum, degrees CCW from +X (90 = up).")]
        public float aimCentreDeg = 90f;
        [Tooltip("How far either side of centre the aim arrow swings. 180 = every direction.")]
        [Range(0f, 180f)] public float aimHalfRangeDeg = 180f;
        [Tooltip("Speed of the direction pendulum, cycles per second.")]
        public float aimSweepHz = 0.5f;

        [Header("Launch — power meter (§2)")]
        [Tooltip("Speed of the power bar oscillation, cycles per second.")]
        public float powerHz = 0.9f;
        [Tooltip("World speed at power = 0.")]
        public float minLaunchSpeed = 3f;
        [Tooltip("World speed at power = 1.")]
        public float maxLaunchSpeed = 14f;

        [Header("Flight / catch (§3)")]
        [Tooltip("Field magnitude → world speed. How fast the wind can carry you.")]
        public float windScale = 8f;
        [Tooltip("Catch steer sharpness. High = impulse-like snap toward the wind.")]
        public float response = 16f;
        [Tooltip("Extra one-frame punch on the press edge (0 = off).")]
        [Range(0f, 1f)] public float pressKick = 0.3f;
        [Tooltip("Speed bleed while coasting. Kept LOW for the lingering roulette-wheel settle.")]
        public float coastDrag = 0.4f;
        [Tooltip("Hard cap on speed so a gust can't fling you off-map (0 = uncapped).")]
        public float maxSpeed = 20f;

        [Header("Settling / scoring (§5)")]
        [Tooltip("Speed below which the settle timer runs.")]
        public float stopThreshold = 0.35f;
        [Tooltip("How long you must stay below stopThreshold before the shot settles.")]
        public float settleTime = 0.6f;

        [Header("Collision (§3b)")]
        [Tooltip("Player collision circle radius, world units.")]
        public float radius = 0.5f;
        [Tooltip("Bounciness of player-player hits: 0 = dead, 1 = fully elastic.")]
        [Range(0f, 1f)] public float restitution = 0.85f;
        [Tooltip("Mass for momentum transfer (usually equal across players).")]
        public float mass = 1f;
    }
}
