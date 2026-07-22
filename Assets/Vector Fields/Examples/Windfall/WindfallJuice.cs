using UnityEngine;

namespace Windfall {
    /// <summary>
    /// Grey-box game feel (GAME_DESIGN.md §7a) in the magnetic theme (§13). A per-glider component that
    /// subscribes to the <see cref="WindGlider"/>'s events and drives audio + a cyan "field-line" catch
    /// pulse — nothing here touches physics, so feedback stays decoupled. Attached at spawn by
    /// <see cref="WindfallGame"/>.
    ///
    /// Audio is SYNTHESISED procedurally at runtime (there are no imported clips yet), but every clip is an
    /// optional serialized ref: drop a real .wav in and it's used instead, no code change (§7a "keep clips
    /// swappable"). The magnet hum is a continuous looped source modulated each frame by catch state + speed;
    /// launch/settle are one-shots. <see cref="PlayClack"/> is the collision hook — left un-wired until a
    /// player-collision manager exists (§3b / build step 6).
    /// </summary>
    [RequireComponent(typeof(WindGlider))]
    public class WindfallJuice : MonoBehaviour {
        [Header("SFX (optional — procedural fallback if empty)")]
        [Tooltip("Coil thwip on fire; pitched up by launch power.")] public AudioClip launchClip;
        [Tooltip("Looping electromagnet hum while catching; rises with speed.")] public AudioClip catchHumClip;
        [Tooltip("Soft metallic clunk when the ball settles.")] public AudioClip settleClip;
        [Tooltip("Metallic clack on collision (see PlayClack — not yet wired).")] public AudioClip clackClip;

        [Header("Levels")]
        [Range(0f, 1f)] public float humVolume = 0.35f;
        [Range(0f, 1f)] public float oneShotVolume = 0.7f;

        [Header("Catch pulse (field-line ring)")]
        [Tooltip("Instrument-glow cyan (§8) so the grab reads on the dark panel.")]
        public Color pulseColor = new Color(0.3f, 0.9f, 1f);
        public float pulseRadius = 1.6f;
        public float pulseLife = 0.45f;

        WindGlider _glider;
        AudioSource _hum;       // continuous, looped, modulated in Update
        AudioSource _oneShots;  // launch / settle / clack
        float _humLevel;        // eased 0..1 so the hum swells/fades instead of clicking

        // Synthesised clips are identical across players — build once, share.
        static AudioClip _sHum, _sThwip, _sClunk, _sClack;

        void Awake() {
            _glider = GetComponent<WindGlider>();

            _hum = gameObject.AddComponent<AudioSource>();
            _hum.loop = true; _hum.playOnAwake = false; _hum.spatialBlend = 0f; _hum.volume = 0f;

            _oneShots = gameObject.AddComponent<AudioSource>();
            _oneShots.playOnAwake = false; _oneShots.spatialBlend = 0f;

            EnsureClips();
            _hum.clip = catchHumClip != null ? catchHumClip : _sHum;
        }

        void OnEnable() {
            _glider.OnLaunch += HandleLaunch;
            _glider.OnCatchStart += HandleCatchStart;
            _glider.OnSettle += HandleSettle;
        }

        void OnDisable() {
            _glider.OnLaunch -= HandleLaunch;
            _glider.OnCatchStart -= HandleCatchStart;
            _glider.OnSettle -= HandleSettle;
            if (_hum != null) _hum.Stop();
        }

        void Update() {
            // Swell the magnet hum toward on/off, and modulate pitch/volume by speed while it's on.
            float target = _glider.IsCatching ? 1f : 0f;
            _humLevel = Mathf.MoveTowards(_humLevel, target, Time.deltaTime / 0.12f);
            if (_humLevel > 0.001f) {
                if (!_hum.isPlaying) _hum.Play();
                float spd = _glider.Velocity.magnitude;
                _hum.volume = _humLevel * humVolume;
                _hum.pitch = 0.8f + Mathf.Clamp01(spd / 12f) * 0.6f;   // higher speed → higher whine
            } else if (_hum.isPlaying) {
                _hum.Stop();
            }
        }

        void HandleLaunch(Vector2 v) {
            float power01 = Mathf.Clamp01(v.magnitude / 12f);
            PlayOne(launchClip != null ? launchClip : _sThwip, oneShotVolume, 0.9f + power01 * 0.4f);
        }

        void HandleCatchStart() {
            SpawnPulse();   // hum itself is handled continuously in Update
        }

        void HandleSettle(Vector2 p) {
            PlayOne(settleClip != null ? settleClip : _sClunk, oneShotVolume, 1f);
        }

        /// <summary>Collision hook (GAME_DESIGN §3b/§7a). Call on a knock; volume/pitch scale with impact
        /// speed. Not yet invoked — there is no player-collision manager (build step 6).</summary>
        public void PlayClack(float impactSpeed) {
            float k = Mathf.Clamp01(impactSpeed / 10f);
            PlayOne(clackClip != null ? clackClip : _sClack, 0.2f + k * 0.8f, 0.9f + k * 0.5f);
        }

        void PlayOne(AudioClip c, float vol, float pitch) {
            if (c == null) return;
            _oneShots.pitch = pitch;
            _oneShots.PlayOneShot(c, vol);
        }

        void SpawnPulse() {
            var go = new GameObject("CatchPulse");
            go.transform.SetParent(transform, false);
            go.AddComponent<RingPulse>().Init(pulseColor, pulseRadius, pulseLife);
        }

        // ---- procedural audio (built once, cached statically) ----
        static void EnsureClips() {
            if (_sHum == null) _sHum = MakeHum();
            if (_sThwip == null) _sThwip = MakeThwip();
            if (_sClunk == null) _sClunk = MakeClunk();
            if (_sClack == null) _sClack = MakeClack();
        }

        const int SR = 44100;

        // Two detuned integer-cycle partials (90/92 Hz) beat like mains hum; +180 Hz body. Exactly 1 s so it
        // loops seamlessly (all frequencies complete whole cycles in one second).
        static AudioClip MakeHum() {
            int n = SR;
            var d = new float[n];
            for (int i = 0; i < n; i++) {
                float t = i / (float)SR;
                d[i] = 0.5f * (0.6f * Mathf.Sin(2f * Mathf.PI * 90f * t)
                             + 0.3f * Mathf.Sin(2f * Mathf.PI * 92f * t)
                             + 0.15f * Mathf.Sin(2f * Mathf.PI * 180f * t));
            }
            var c = AudioClip.Create("MagHum", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        // Fast downward pitch sweep with a snappy decay — a coil launcher "thwip".
        static AudioClip MakeThwip() {
            int n = (int)(SR * 0.2f);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++) {
                float t = i / (float)SR;
                float f = Mathf.Lerp(720f, 200f, t / 0.2f);
                ph += 2f * Mathf.PI * f / SR;
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 18f) * 0.7f;
            }
            var c = AudioClip.Create("Thwip", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        // Low body + octave, quick exp decay — a soft metallic "clunk" on settle.
        static AudioClip MakeClunk() {
            int n = (int)(SR * 0.18f);
            var d = new float[n];
            for (int i = 0; i < n; i++) {
                float t = i / (float)SR;
                float env = Mathf.Exp(-t * 26f);
                d[i] = env * (0.7f * Mathf.Sin(2f * Mathf.PI * 140f * t)
                            + 0.25f * Mathf.Sin(2f * Mathf.PI * 280f * t)) * 0.8f;
            }
            var c = AudioClip.Create("Clunk", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        // Short bright inharmonic partials + a noise transient — a metallic ball-bearing "clack".
        static AudioClip MakeClack() {
            int n = (int)(SR * 0.09f);
            var d = new float[n];
            uint seed = 22801u;
            for (int i = 0; i < n; i++) {
                float t = i / (float)SR;
                float env = Mathf.Exp(-t * 55f);
                seed = seed * 1664525u + 1013904223u;
                float noise = (seed / 4294967295f) * 2f - 1f;
                float tone = 0.5f * Mathf.Sin(2f * Mathf.PI * 1200f * t)
                           + 0.3f * Mathf.Sin(2f * Mathf.PI * 2100f * t);
                d[i] = env * (tone + 0.35f * noise) * 0.8f;
            }
            var c = AudioClip.Create("Clack", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }
    }

    /// <summary>A one-shot expanding, fading ring drawn with a runtime LineRenderer — the field-line pulse
    /// around the ball on catch (GAME_DESIGN.md §7a/§8). Parented to the glider so it rides the ball; self-
    /// destructs at the end of its life. Kept in this file since it exists only to serve <see cref="WindfallJuice"/>.</summary>
    public class RingPulse : MonoBehaviour {
        LineRenderer _lr;
        Color _color;
        float _r0, _r1, _life, _age;

        public void Init(Color color, float maxRadius, float life) {
            _color = color;
            _r1 = maxRadius;
            _r0 = maxRadius * 0.25f;
            _life = Mathf.Max(0.01f, life);

            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.widthMultiplier = 0.06f;
            _lr.numCapVertices = 2;
            _lr.sortingOrder = 9;
            _lr.material = new Material(Shader.Find("Sprites/Default"));
            _lr.positionCount = 48;
            Redraw(_r0, 1f);
        }

        void Update() {
            _age += Time.deltaTime;
            float k = _age / _life;
            if (k >= 1f) { Destroy(gameObject); return; }
            Redraw(Mathf.Lerp(_r0, _r1, k), 1f - k);
        }

        void Redraw(float r, float alpha) {
            var c = _color; c.a = alpha;
            _lr.startColor = _lr.endColor = c;
            int n = _lr.positionCount;
            for (int i = 0; i < n; i++) {
                float a = i / (float)n * Mathf.PI * 2f;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }
    }
}
