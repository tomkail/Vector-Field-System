using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdFlow {
    /// <summary>
    /// A simple RTS-style camera for the demo: pan across the ground, zoom, and orbit. Kinematic, driven off the
    /// Input System, and clamped so you stay over the island. It orbits a ground focus point:
    /// <list type="bullet">
    /// <item><b>Pan</b> — WASD / arrow keys (hold <b>Shift</b> to move faster).</item>
    /// <item><b>Zoom</b> — mouse scroll wheel.</item>
    /// <item><b>Rotate</b> — Q / E, or hold <b>middle mouse</b> and drag (also tilts).</item>
    /// <item><b>Tilt</b> — R / F.</item>
    /// </list>
    /// </summary>
    public class DemoCameraController : MonoBehaviour {
        [Header("Speeds")]
        public float panSpeed = 55f;
        public float fastMultiplier = 2.5f;
        public float zoomStep = 12f;
        public float rotateSpeed = 90f;
        public float tiltSpeed = 60f;

        [Header("Limits")]
        public Vector2 distanceRange = new Vector2(25f, 260f);
        public Vector2 pitchRange = new Vector2(18f, 82f);
        public float groundY = 6f;
        [Tooltip("Half-extent (world units) the focus point can pan from the origin.")]
        public Vector2 panBounds = new Vector2(150f, 150f);

        float _yaw, _pitch, _dist;
        Vector3 _focus;

        void Start() => InitFromTransform();

        // Seed the orbit rig from wherever the camera currently sits, so the authored framing is the starting shot.
        void InitFromTransform() {
            var t = transform;
            _yaw = t.eulerAngles.y;
            _pitch = Mathf.Clamp(NormalizePitch(t.eulerAngles.x), pitchRange.x, pitchRange.y);
            var ray = new Ray(t.position, t.forward);
            var ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            if (ground.Raycast(ray, out float hit)) { _focus = ray.GetPoint(hit); _dist = Mathf.Clamp(hit, distanceRange.x, distanceRange.y); }
            else { _focus = t.position + t.forward * 120f; _dist = Mathf.Clamp(120f, distanceRange.x, distanceRange.y); }
            Apply();
        }

        static float NormalizePitch(float x) => x > 180f ? x - 360f : x;

        void Update() {
            float dt = Time.unscaledDeltaTime;
            var k = Keyboard.current;
            var m = Mouse.current;

            Vector2 mv = Vector2.zero;
            if (k != null) {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) mv.y += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) mv.y -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) mv.x += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) mv.x -= 1f;
                if (k.qKey.isPressed) _yaw -= rotateSpeed * dt;
                if (k.eKey.isPressed) _yaw += rotateSpeed * dt;
                if (k.rKey.isPressed) _pitch = Mathf.Clamp(_pitch + tiltSpeed * dt, pitchRange.x, pitchRange.y);
                if (k.fKey.isPressed) _pitch = Mathf.Clamp(_pitch - tiltSpeed * dt, pitchRange.x, pitchRange.y);
            }

            float speed = panSpeed * (k != null && k.leftShiftKey.isPressed ? fastMultiplier : 1f);
            // Pan on the ground plane relative to the current yaw (scaled a little by zoom so it feels consistent).
            Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            float zoomScale = Mathf.Lerp(0.5f, 1.6f, Mathf.InverseLerp(distanceRange.x, distanceRange.y, _dist));
            _focus += (right * mv.x + fwd * mv.y) * (speed * zoomScale * dt);

            if (m != null) {
                if (m.middleButton.isPressed) {
                    Vector2 d = m.delta.ReadValue();
                    _yaw += d.x * 0.18f;
                    _pitch = Mathf.Clamp(_pitch - d.y * 0.13f, pitchRange.x, pitchRange.y);
                }
                float sc = m.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) _dist = Mathf.Clamp(_dist - Mathf.Sign(sc) * zoomStep, distanceRange.x, distanceRange.y);
            }

            Apply();
        }

        void Apply() {
            _focus.x = Mathf.Clamp(_focus.x, -panBounds.x, panBounds.x);
            _focus.z = Mathf.Clamp(_focus.z, -panBounds.y, panBounds.y);
            _focus.y = groundY;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pos = _focus - (rot * Vector3.forward) * _dist;
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
