using System;
using UnityEngine;
using UnityEngine.Serialization;
using VectorFields;

namespace Windfall {
    /// <summary>
    /// The player (GAME_DESIGN.md §2–§3). A one-button glider: a golf-style oscillating launch
    /// (direction sweep → power bar), then pure field-driven flight where holding the button
    /// "catches" the wind (velocity snaps toward the local field vector, impulse-like) and releasing
    /// coasts under light drag until it settles. 2D kinematic integrator on the XY plane — deliberately
    /// NOT a Rigidbody2D, so the catch/coast feel is exact. All feel constants live in a live-tuned
    /// <see cref="WindfallSettings"/> asset. Reads the field via the CPU consumer path.
    /// </summary>
    [DisallowMultipleComponent]
    public class WindGlider : MonoBehaviour {
        public enum State { AimingDirection, AimingPower, Flying, Settled }

        [Header("Wiring")]
        [Tooltip("The level's wind field. Any VectorFieldComponent subtype; laid flat in the XY plane.")]
        [SerializeField, FormerlySerializedAs("field")] VectorFieldComponent _field;
        public VectorFieldComponent field {
            get => _field;
            set {
                if (_field == value) return;
                _field = value;
                if (isActiveAndEnabled) RegisterField(); // reconciles: drops the old field, registers the new one
            }
        }
        // The field we currently hold a CPU-consumer registration on — the single source of truth. It can diverge
        // from _field when the inspector writes the serialized field directly (that bypasses the property setter);
        // RegisterField()/OnValidate reconcile the two. Not serialized: a registration can't survive a domain reload,
        // so OnEnable re-establishes it from scratch.
        [NonSerialized] VectorFieldComponent _registeredField;
        [Tooltip("Feel constants (tune live in play mode).")]
        public WindfallSettings settings;
        public WindfallInput input = new WindfallInput();

        [Header("Aim line")]
        [Tooltip("Drawn along the aim during launch; length reads power in the power phase. Auto-created at play if left empty.")]
        public LineRenderer aimLine;
        [Tooltip("Aim line width, world units.")]
        public float aimLineWidth = 0.12f;
        [Tooltip("Fixed pointer length while choosing direction, world units.")]
        public float aimPointerLength = 2f;
        [Tooltip("Aim line colour at zero power.")]
        public Color aimColorLow = new Color(0.4f, 1f, 0.5f);
        [Tooltip("Aim line colour at full power.")]
        public Color aimColorHigh = new Color(1f, 0.5f, 0.2f);

        [Header("Optional visuals (grey-box)")]
        [Tooltip("Emits only while flying.")]
        public TrailRenderer trail;

        /// <summary>When true the glider ignores input, physics and aim visuals — used by
        /// <see cref="WindfallGame"/> to hold players still during the round intro/outro.</summary>
        [System.NonSerialized] public bool Frozen;

        // --- events (juice/scoring subscribe; GAME_DESIGN.md §7a) ---
        public event Action<Vector2> OnLaunch;      // initial launch velocity
        public event Action OnCatchStart;
        public event Action OnCatchEnd;
        public event Action<Vector2> OnSettle;      // resting world position (xy)

        // --- public state (visualisers / game loop read these) ---
        public State CurrentState { get; private set; } = State.AimingDirection;
        public Vector2 Velocity { get; private set; }
        public bool IsCatching { get; private set; }
        public float AimAngleDeg { get; private set; }
        public float AimPower01 { get; private set; }

        float _planeZ;
        float _aimTime;      // drives the direction pendulum
        float _powerTime;    // drives the power bar
        float _lockedAngleDeg;
        float _slowTimer;    // time spent below stopThreshold
        bool _wasCatching;
        bool _catchArmed;    // catching is blocked until the button is released once after a launch (see FixedUpdate)

        void OnEnable() {
            _planeZ = transform.position.z;
            RegisterField();
            EnsureAimLine();
            EnterAimingDirection();
        }

        void OnDisable() {
            UnregisterField();
        }

        // Reconcile our CPU-consumer registration so it's on `_field` and nothing else, then nothing more. Idempotent:
        // safe to call after an inspector edit swapped the serialized field out from under us, and registering twice
        // is a no-op on the field side.
        void RegisterField() {
            if (_registeredField != _field) UnregisterField(); // drop the stale field (no-op if none)
            if (_field != null && _registeredField == null) {
                // immediate:true guarantees the CPU mirror is fresh the frame we sample; the async
                // (immediate:false) path is the perf option once feel is dialled in.
                _field.RegisterCpuConsumer(this, immediate: true);
                _registeredField = _field;
            }
        }

        void UnregisterField() {
            if (_registeredField == null) return;
            _registeredField.UnregisterCpuConsumer(this);
            _registeredField = null;
        }

#if UNITY_EDITOR
        // Inspector edits write _field directly, bypassing the property setter, so reconcile the registration here.
        void OnValidate() {
            if (isActiveAndEnabled) RegisterField();
        }
#endif

        // Discrete, edge-driven transitions run in Update (one input poll per rendered frame).
        void Update() {
            if (settings == null) {
                Debug.LogWarning("WindGlider has no WindfallSettings assigned.", this);
                return;
            }
            input.Poll();   // poll even while frozen so edge state stays fresh (no stale press on unfreeze)

            if (Frozen) {
                if (aimLine != null) aimLine.enabled = false;
                return;
            }

            switch (CurrentState) {
                case State.AimingDirection:
                    _aimTime += Time.deltaTime;
                    AimAngleDeg = settings.aimCentreDeg +
                                  settings.aimHalfRangeDeg * Mathf.Sin(_aimTime * settings.aimSweepHz * Mathf.PI * 2f);
                    if (input.PressedThisFrame) {
                        _lockedAngleDeg = AimAngleDeg;
                        _powerTime = 0f;
                        CurrentState = State.AimingPower;
                    }
                    break;

                case State.AimingPower:
                    _powerTime += Time.deltaTime;
                    // PingPong period is 2, so scale by 2*Hz for a full up-down cycle per 1/Hz seconds.
                    AimPower01 = Mathf.PingPong(_powerTime * settings.powerHz * 2f, 1f);
                    if (input.PressedThisFrame) Fire();
                    break;

                case State.Settled:
                    // Chain shots (island re-launch / quick feel testing): tap to launch again from rest.
                    if (input.PressedThisFrame) EnterAimingDirection();
                    break;
            }

            UpdateVisuals();
        }

        // Flight physics runs in FixedUpdate for a stable, framerate-independent integrator.
        void FixedUpdate() {
            if (Frozen || settings == null || CurrentState != State.Flying) return;
            float dt = Time.fixedDeltaTime;

            Vector2 windVel = Vector2.zero;
            if (field != null) {
                field.EnsureUpToDate();
                windVel = (Vector2)field.EvaluateWorldVector(transform.position) * settings.windScale;
            }

            // The fire tap almost always leaves the button still held as flight begins; if we caught on that held
            // frame the launch impulse would be instantly overridden by a catch (and "ruins the golf swing"). So
            // arm catching only once the button has been released after the launch — the first deliberate hold catches.
            if (!input.Held) _catchArmed = true;
            IsCatching = _catchArmed && input.Held;
            Vector2 v = Velocity;

            if (IsCatching) {
                if (!_wasCatching) {
                    // Press edge: an extra one-frame punch toward the wind, then steer.
                    v += (windVel - v) * settings.pressKick;
                    OnCatchStart?.Invoke();
                }
                // Framerate-independent exponential approach — snappy but never overshoots.
                float t = 1f - Mathf.Exp(-settings.response * dt);
                v = Vector2.Lerp(v, windVel, t);
            } else {
                if (_wasCatching) OnCatchEnd?.Invoke();
                v *= Mathf.Exp(-settings.coastDrag * dt);   // low drag → lingering coast
            }
            _wasCatching = IsCatching;

            if (settings.maxSpeed > 0f) v = Vector2.ClampMagnitude(v, settings.maxSpeed);

            Velocity = v;
            Vector3 p = transform.position + (Vector3)(v * dt);
            p.z = _planeZ;
            transform.position = p;

            // Settle detection — patient, so the lingering coast (§3) doesn't false-trigger mid-drift.
            if (v.magnitude < settings.stopThreshold) {
                _slowTimer += dt;
                if (_slowTimer >= settings.settleTime) Settle();
            } else {
                _slowTimer = 0f;
            }
        }

        void Fire() {
            float rad = _lockedAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Mathf.Lerp(settings.minLaunchSpeed, settings.maxLaunchSpeed, AimPower01);
            Velocity = dir * speed;
            _wasCatching = false;
            _catchArmed = false;   // must release the fire button before the first catch (see FixedUpdate)
            _slowTimer = 0f;
            CurrentState = State.Flying;
            if (trail != null) { trail.Clear(); trail.emitting = true; }
            OnLaunch?.Invoke(Velocity);
        }

        void Settle() {
            Velocity = Vector2.zero;
            IsCatching = false;
            CurrentState = State.Settled;
            if (trail != null) trail.emitting = false;
            OnSettle?.Invoke(transform.position);
        }

        void EnterAimingDirection() {
            _aimTime = 0f;
            Velocity = Vector2.zero;
            IsCatching = false;
            _wasCatching = false;
            AimPower01 = 0f;
            CurrentState = State.AimingDirection;
            if (trail != null) trail.emitting = false;
        }

        /// <summary>Force a fresh shot from the current position (e.g. island re-launch).</summary>
        public void Relaunch() => EnterAimingDirection();

        // Builds a self-contained aim line so nothing has to be wired in the scene. Runs at play only
        // (OnEnable), so it never pollutes the saved scene.
        void EnsureAimLine() {
            if (aimLine == null) {
                var go = new GameObject("AimLine");
                go.transform.SetParent(transform, false);
                aimLine = go.AddComponent<LineRenderer>();
                aimLine.useWorldSpace = true;
                aimLine.numCapVertices = 4;
                aimLine.textureMode = LineTextureMode.Stretch;
                aimLine.sortingOrder = 10;
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) aimLine.material = new Material(shader);
            }
            aimLine.positionCount = 2;
            aimLine.startWidth = aimLine.endWidth = aimLineWidth;
            aimLine.enabled = false;
        }

        void UpdateVisuals() {
            if (aimLine == null) return;
            bool aiming = CurrentState == State.AimingDirection || CurrentState == State.AimingPower;
            aimLine.enabled = aiming;
            if (!aiming) return;

            bool powerPhase = CurrentState == State.AimingPower;
            float angle = powerPhase ? _lockedAngleDeg : AimAngleDeg;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            // In the power phase the length reads the current power; in the direction phase it's a fixed pointer.
            float len = powerPhase
                ? Mathf.Lerp(settings.minLaunchSpeed, settings.maxLaunchSpeed, AimPower01) * 0.2f
                : aimPointerLength;

            aimLine.startWidth = aimLine.endWidth = aimLineWidth;
            Color c = Color.Lerp(aimColorLow, aimColorHigh, powerPhase ? AimPower01 : 0f);
            aimLine.startColor = aimLine.endColor = c;
            aimLine.SetPosition(0, transform.position);
            aimLine.SetPosition(1, transform.position + dir * len);
        }

        void OnDrawGizmos() {
            var s = settings;
            float r = s != null ? s.radius : 0.5f;
            Gizmos.color = CurrentState == State.Flying
                ? (IsCatching ? Color.cyan : Color.yellow)
                : Color.gray;
            DrawCircle(transform.position, r);
            // velocity
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)Velocity * 0.2f);
        }

        static void DrawCircle(Vector3 c, float r) {
            const int seg = 24;
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
