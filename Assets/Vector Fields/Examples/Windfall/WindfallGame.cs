using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Windfall {
    /// <summary>
    /// One serialized player slot. If the manager's list is left empty it auto-builds one slot per child of
    /// <see cref="WindfallGame.spawnParent"/>, cycling colours and a default input (P1 Space, P2 Enter, P3/P4 pads).
    /// Fill the list explicitly to control colours / bindings (keyboard OR gamepad, per player).
    /// </summary>
    [System.Serializable]
    public class WindfallPlayerConfig {
        public string name = "P1";
        public Color color = Color.white;
        [Tooltip("Where this player launches from. Slightly different per player.")]
        public Transform spawnPoint;
        public WindfallInput input = new WindfallInput();
    }

    /// <summary>
    /// Simultaneous local-multiplayer round manager (GAME_DESIGN.md §9 simultaneous mode + §12 game loop),
    /// wrapped in a multi-round flow. Each round runs through phases:
    /// <c>FadeIn → Pan (camera sweeps the map, skippable) → RoundName → Playing → Results → FadeOut</c>, then
    /// the next round. After the last round a Game-Over standings screen waits for reset.
    ///
    /// During Playing, one player is spawned per spawn point with its own field consumer + input + colour, and
    /// scores by SETTLING inside the <see cref="TargetRing"/> (zone points + a rank bonus by settle ORDER) and
    /// by collecting scattered <see cref="Collectible"/>s. Out of bounds ends a player's run with no zone score;
    /// the round ends once every player has finished (or a safety time limit elapses). Score is CUMULATIVE across
    /// rounds. Backspace resets the whole game. Presentation is a runtime <see cref="WindfallHUD"/> (created on
    /// start): the live top bar, the fade overlay, the round-name banner, the "+N" popups, and the results panel.
    /// </summary>
    public class WindfallGame : MonoBehaviour {
        [Header("Wiring")]
        [Tooltip("The player prefab (a WindGlider with a trail + aim line).")]
        public WindGlider playerPrefab;
        [Tooltip("One player is spawned per child transform of this object.")]
        public Transform spawnParent;
        [Tooltip("The level's wind field (any VectorFieldComponent, laid flat in XY).")]
        public VectorFieldComponent field;
        [Tooltip("Feel constants shared by every player.")]
        public WindfallSettings settings;
        [Tooltip("The scoring target.")]
        public TargetRing targetRing;

        [Header("Scoring")]
        [Tooltip("Bonus points by settle order into the ring: [0]=first in, [1]=second, ... last entry repeats.")]
        public int[] rankBonuses = { 50, 30, 20, 10 };
        [Tooltip("A flying player past this distance from the origin is out of bounds (run ends, no zone score).")]
        public float outOfBoundsRadius = 13f;

        [Header("Rounds")]
        [Tooltip("How many rounds a game runs before the final standings.")]
        public int roundCount = 3;
        [Tooltip("Optional per-round names shown on the intro banner. Falls back to \"Round N\" if empty/short.")]
        public string[] roundNames;
        [Tooltip("Safety: force-end a round after this many seconds so a player who never launches can't stall it. 0 = no limit.")]
        public float roundTimeLimit = 45f;

        [Header("Intro / presentation timing (seconds)")]
        public float fadeDuration = 0.8f;
        [Tooltip("Total time the camera spends panning over the map. Tap any button to skip.")]
        public float panDuration = 4f;
        [Tooltip("Orthographic size at the establishing (spawn / ring) pan waypoints.")]
        public float establishSize = 7f;
        public float roundNameDuration = 1.8f;
        public float resultsDuration = 4f;

        [Header("Players (auto-filled from spawn children if left empty)")]
        public List<WindfallPlayerConfig> players = new List<WindfallPlayerConfig>();

        enum Phase { FadeIn, Pan, RoundName, Playing, Results, FadeOut, GameOver }

        class Runner {
            public WindfallPlayerConfig cfg;
            public WindGlider glider;
            public int total;         // confirmed points from previous rounds (cumulative)
            public int score;         // points earned this round
            public bool finished;
            public bool scoredZone;
            public int zoneRank = -1;
        }

        /// <summary>Read-only view of a player for the HUD (GAME_DESIGN.md §7a).</summary>
        public struct PlayerView {
            public string name;
            public Color color;
            public string button;     // human-readable input button (e.g. "Space", "Pad 1")
            public int score;         // cumulative standing = total + this round's score
            public bool finished;
            public bool scoredZone;
            public int zoneRank;      // settle order into the ring, -1 if not scored there
            public Transform tracked; // the glider transform (for head-anchored popups); null once destroyed
        }

        /// <summary>Fired when a player gains points (coin pickup or ring settle). (tracked transform, amount, colour.)</summary>
        public event System.Action<Transform, int, Color> OnPointsGained;

        public int PlayerCount => _runners.Count;

        public PlayerView GetPlayer(int i) {
            var r = _runners[i];
            return new PlayerView {
                name = r.cfg.name, color = r.cfg.color, score = r.total + r.score,
                button = r.cfg.input != null ? r.cfg.input.Label : "",
                finished = r.finished, scoredZone = r.scoredZone, zoneRank = r.zoneRank,
                tracked = r.glider != null ? r.glider.transform : null,
            };
        }

        readonly List<Runner> _runners = new List<Runner>();
        readonly List<Collectible> _collectibles = new List<Collectible>();
        int _zoneScoredCount;
        WindfallHUD _hud;

        // --- round flow state ---
        Phase _phase;
        float _phaseT;
        int _round;               // 0-based index of the current round

        // --- camera pan ---
        struct CamPose { public Vector2 pos; public float size; public CamPose(Vector2 p, float s) { pos = p; size = s; } }
        Camera _cam;
        Vector3 _camHome;
        float _camHomeSize;
        readonly List<CamPose> _panPoses = new List<CamPose>();

        static readonly Color[] Palette = {
            new Color(1f, 0.35f, 0.35f), new Color(0.4f, 0.62f, 1f), new Color(0.45f, 0.9f, 0.45f),
            new Color(1f, 0.85f, 0.3f), new Color(0.9f, 0.45f, 1f), new Color(0.35f, 0.9f, 0.9f),
        };
        static readonly WindfallInput.Source[] DefaultSources = {
            WindfallInput.Source.KeyboardSpace, WindfallInput.Source.KeyboardEnter,
            WindfallInput.Source.GamepadSouth, WindfallInput.Source.GamepadSouth,
        };

        void Start() {
            EnsureHud();
            CacheCameraAndPan();
            _round = 0;
            StartRound();
            EnterFadeIn();
        }

        void EnsureHud() {
            if (_hud != null) return;
            var go = new GameObject("WindfallHUD");
            _hud = go.AddComponent<WindfallHUD>();
            _hud.Init(this);
        }

        // ------------------------------------------------------------------ round setup

        /// <summary>Set up the current round: (re)spawn players frozen at their pads and restore collectibles.
        /// Cumulative totals are preserved — only per-round state resets.</summary>
        public void StartRound() {
            EnsureConfigs();
            EnsureRunners();
            _zoneScoredCount = 0;

            _collectibles.Clear();
            _collectibles.AddRange(FindObjectsByType<Collectible>(FindObjectsInactive.Include));
            foreach (var c in _collectibles) c.ResetCollectible();

            if (playerPrefab == null) { Debug.LogWarning("WindfallGame: no playerPrefab assigned.", this); return; }

            foreach (var r in _runners) {
                if (r.glider != null) Destroy(r.glider.gameObject);
                r.score = 0; r.finished = false; r.scoredZone = false; r.zoneRank = -1;

                var cfg = r.cfg;
                Vector3 pos = cfg.spawnPoint != null ? cfg.spawnPoint.position : Vector3.zero;
                var g = Instantiate(playerPrefab, pos, Quaternion.identity);
                // Configure while inactive so WindGlider.OnEnable registers its field consumer with the field
                // already assigned. (The prefab can't hold a scene-field reference.)
                g.gameObject.SetActive(false);
                g.name = "Player_" + cfg.name;
                g.field = field;
                g.settings = settings;
                g.input = cfg.input;
                g.Frozen = true;   // held until the round's Playing phase begins
                ApplyColor(g, cfg.color);
                g.gameObject.AddComponent<WindfallJuice>();   // §7a feedback; subscribes to the glider's events on enable
                var runner = r;   // capture for the closure
                g.OnSettle += _ => OnSettle(runner, g.transform.position);
                g.gameObject.SetActive(true);
                r.glider = g;
            }
        }

        void EnsureRunners() {
            if (_runners.Count == players.Count) return;
            _runners.Clear();
            foreach (var cfg in players) _runners.Add(new Runner { cfg = cfg });
        }

        void EnsureConfigs() {
            if (players != null && players.Count > 0) {
                foreach (var c in players) if (c.input == null) c.input = new WindfallInput();
                return;
            }
            players = new List<WindfallPlayerConfig>();
            if (spawnParent == null) { Debug.LogWarning("WindfallGame: no spawnParent and no players configured.", this); return; }
            int n = spawnParent.childCount;
            for (int i = 0; i < n; i++) {
                var cfg = new WindfallPlayerConfig {
                    name = "P" + (i + 1),
                    color = Palette[i % Palette.Length],
                    spawnPoint = spawnParent.GetChild(i),
                    input = new WindfallInput { source = DefaultSources[Mathf.Min(i, DefaultSources.Length - 1)] },
                };
                if (cfg.input.source == WindfallInput.Source.GamepadSouth) cfg.input.gamepadIndex = Mathf.Max(0, i - 2);
                players.Add(cfg);
            }
        }

        static void ApplyColor(WindGlider g, Color col) {
            var mr = g.GetComponentInChildren<MeshRenderer>();
            if (mr != null) {
                var m = mr.material;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                if (m.HasProperty("_Color")) m.color = col;
            }
            // Assign fresh line materials at spawn — the prefab can't serialize the runtime-created ones
            // (they'd render as the magenta error shader), so build them here and let vertex colours tint.
            var lineShader = Shader.Find("Sprites/Default");
            if (g.trail != null) {
                g.trail.material = new Material(lineShader);
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(col, 0f), new GradientColorKey(col, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                g.trail.colorGradient = grad;
            }
            if (g.aimLine != null) {
                g.aimLine.material = new Material(lineShader);
                g.aimLine.startColor = col;
                g.aimLine.endColor = new Color(col.r, col.g, col.b, 0.5f);
            }
        }

        // ------------------------------------------------------------------ scoring

        void OnSettle(Runner r, Vector2 pos) {
            if (r.finished) return;
            if (targetRing != null && targetRing.Contains(pos)) {
                int basePts = targetRing.ScoreAt(pos);
                int rank = _zoneScoredCount++;
                int bonus = (rankBonuses != null && rankBonuses.Length > 0)
                    ? rankBonuses[Mathf.Min(rank, rankBonuses.Length - 1)] : 0;
                r.score += basePts + bonus;
                r.scoredZone = true;
                r.zoneRank = rank;
                if (r.glider != null) OnPointsGained?.Invoke(r.glider.transform, basePts + bonus, r.cfg.color);
            }
            Finish(r);
        }

        void Finish(Runner r) {
            r.finished = true;
            if (r.glider != null) r.glider.enabled = false;   // freeze — no relaunch; run is over
        }

        bool AllFinished() {
            if (_runners.Count == 0) return false;
            foreach (var r in _runners) if (!r.finished) return false;
            return true;
        }

        void TickPlaying() {
            float pr = settings != null ? settings.radius : 0.5f;
            foreach (var r in _runners) {
                if (r.finished || r.glider == null) continue;
                Vector2 p = r.glider.transform.position;

                if (p.magnitude > outOfBoundsRadius) { Finish(r); continue; }

                foreach (var col in _collectibles) {
                    if (col == null || col.Collected) continue;
                    float rad = pr + col.radius;
                    if (((Vector2)col.transform.position - p).sqrMagnitude <= rad * rad) {
                        r.score += col.points;
                        OnPointsGained?.Invoke(r.glider.transform, col.points, r.cfg.color);
                        col.Collect();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ round-flow state machine

        void Update() {
            if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame) { ResetGame(); return; }

            _phaseT += Time.deltaTime;
            switch (_phase) {
                case Phase.FadeIn:
                    _hud.SetFade(1f - Mathf.Clamp01(_phaseT / Mathf.Max(0.01f, fadeDuration)));
                    if (_phaseT >= fadeDuration) EnterPan();
                    break;

                case Phase.Pan:
                    ApplyPose(SamplePan(Mathf.Clamp01(_phaseT / Mathf.Max(0.01f, panDuration))));
                    if (AnyPress() || _phaseT >= panDuration) EnterRoundName();
                    break;

                case Phase.RoundName:
                    _hud.SetBanner(CurrentRoundName(), BannerAlpha(_phaseT, roundNameDuration));
                    if (_phaseT >= roundNameDuration) EnterPlaying();
                    break;

                case Phase.Playing:
                    TickPlaying();
                    if (roundTimeLimit > 0f && _phaseT >= roundTimeLimit)
                        foreach (var r in _runners) if (!r.finished) Finish(r);
                    if (AllFinished()) EnterResults();
                    break;

                case Phase.Results:
                    if (_phaseT >= resultsDuration || AnyPress()) {
                        if (_round >= roundCount - 1) EnterGameOver();
                        else EnterFadeOut();
                    }
                    break;

                case Phase.FadeOut:
                    _hud.SetFade(Mathf.Clamp01(_phaseT / Mathf.Max(0.01f, fadeDuration)));
                    if (_phaseT >= fadeDuration) NextRound();
                    break;

                case Phase.GameOver:
                    // Final standings held on screen; Backspace (handled above) starts a new game.
                    break;
            }
        }

        void SetPhase(Phase p) { _phase = p; _phaseT = 0f; }

        void EnterFadeIn() {
            FreezeAll(true);
            _hud.SetBarVisible(false);
            _hud.SetResults(false, "");
            _hud.SetBanner("", 0f);
            ApplyPose(_panPoses.Count > 0 ? _panPoses[0] : Home());   // establishing shot behind the black
            _hud.SetFade(1f);
            SetPhase(Phase.FadeIn);
        }

        void EnterPan() { SetPhase(Phase.Pan); }

        void EnterRoundName() {
            ApplyPose(Home());
            FreezeAll(true);
            _hud.SetBanner(CurrentRoundName(), 0f);
            SetPhase(Phase.RoundName);
        }

        void EnterPlaying() {
            ApplyPose(Home());
            _hud.SetBanner("", 0f);
            _hud.SetBarVisible(true);
            FreezeAll(false);
            SetPhase(Phase.Playing);
        }

        void EnterResults() {
            foreach (var r in _runners) { r.total += r.score; r.score = 0; }   // bank this round into the cumulative total
            FreezeAll(true);
            _hud.SetBarVisible(false);
            _hud.SetResults(true, "Round " + (_round + 1) + " — Totals");
            SetPhase(Phase.Results);
        }

        void EnterFadeOut() { SetPhase(Phase.FadeOut); }

        void EnterGameOver() {
            _hud.SetFade(0f);
            _hud.SetBarVisible(false);
            _hud.SetResults(true, "Final Standings");
            SetPhase(Phase.GameOver);
        }

        void NextRound() {
            _round++;
            if (_round >= roundCount) { EnterGameOver(); return; }
            StartRound();
            EnterFadeIn();
        }

        /// <summary>Full reset to round 1 with scores zeroed (Backspace).</summary>
        public void ResetGame() {
            foreach (var r in _runners) { r.total = 0; r.score = 0; }
            _round = 0;
            StartRound();
            EnterFadeIn();
        }

        void FreezeAll(bool frozen) {
            foreach (var r in _runners) if (r.glider != null) r.glider.Frozen = frozen;
        }

        string CurrentRoundName() {
            if (roundNames != null && _round < roundNames.Length && !string.IsNullOrEmpty(roundNames[_round]))
                return roundNames[_round];
            return "Round " + (_round + 1);
        }

        // Fade the banner in, hold, fade out across its lifetime.
        static float BannerAlpha(float t, float dur) {
            const float edge = 0.35f;
            if (t < edge) return t / edge;
            if (t > dur - edge) return Mathf.Max(0f, (dur - t) / edge);
            return 1f;
        }

        // ------------------------------------------------------------------ camera

        void CacheCameraAndPan() {
            _cam = Camera.main;
            if (_cam != null) {
                _camHome = _cam.transform.position;
                _camHomeSize = _cam.orthographic ? _cam.orthographicSize : 10f;
            } else {
                _camHome = new Vector3(0f, 0f, -10f);
                _camHomeSize = 11f;
            }

            // Pan waypoints follow the intended flight: over the launch pads → over the goal → settle to play view.
            Vector2 spawnC = Vector2.zero; int n = 0;
            if (players != null)
                foreach (var cfg in players) if (cfg.spawnPoint != null) { spawnC += (Vector2)cfg.spawnPoint.position; n++; }
            if (n > 0) spawnC /= n; else spawnC = (Vector2)_camHome;
            Vector2 ringC = targetRing != null ? targetRing.Center : (Vector2)_camHome;

            _panPoses.Clear();
            _panPoses.Add(new CamPose(spawnC, establishSize));
            _panPoses.Add(new CamPose(ringC, establishSize));
            _panPoses.Add(Home());
        }

        CamPose Home() => new CamPose(new Vector2(_camHome.x, _camHome.y), _camHomeSize);

        CamPose SamplePan(float u) {
            int legs = _panPoses.Count - 1;
            if (legs <= 0) return Home();
            float f = Mathf.Clamp01(u) * legs;
            int i = Mathf.Min((int)f, legs - 1);
            float s = Mathf.SmoothStep(0f, 1f, f - i);
            var a = _panPoses[i]; var b = _panPoses[i + 1];
            return new CamPose(Vector2.Lerp(a.pos, b.pos, s), Mathf.Lerp(a.size, b.size, s));
        }

        void ApplyPose(CamPose pose) {
            if (_cam == null) return;
            _cam.transform.position = new Vector3(pose.pos.x, pose.pos.y, _camHome.z);
            if (_cam.orthographic) _cam.orthographicSize = pose.size;
        }

        static bool AnyPress() {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            foreach (var pad in Gamepad.all)
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) return true;
            return false;
        }
    }
}
