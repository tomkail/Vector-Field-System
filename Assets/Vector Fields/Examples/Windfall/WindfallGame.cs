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
        [Tooltip("The level's wind field (any VectorFieldComponent, laid flat in XY). Overwritten each round when Generate Levels is on.")]
        public VectorFieldComponent field;
        [Tooltip("Builds a fresh random field each round. Auto-created when Generate Levels is on and none is assigned.")]
        public WindfallLevelGenerator levelGenerator;
        [Tooltip("Generate a random level each round instead of using the serialized field above.")]
        public bool generateLevels = true;
        [Tooltip("Install a runtime bloom post-process so the plasma trails and flow lines glow (instrument-glow look).")]
        public bool bloom = true;
        [Tooltip("Feel constants shared by every player.")]
        public WindfallSettings settings;
        [Tooltip("The scoring target.")]
        public TargetRing targetRing;

        [Header("Scoring")]
        [Tooltip("Bonus points by settle order into the ring: [0]=first in, [1]=second, ... last entry repeats.")]
        public int[] rankBonuses = { 50, 30, 20, 10 };
        [Tooltip("A flying player past this distance from the origin is out of bounds (run ends, no zone score).")]
        public float outOfBoundsRadius = 13f;

        [Header("Level")]
        [Tooltip("Random world size (square, centred on origin) picked per level — the field, out-of-bounds and target/spawn distances scale off this.")]
        public Vector2 levelSizeRange = new Vector2(80f, 140f);

        [Header("Camera")]
        [Tooltip("The camera follows the active players and zooms to fit them; never tighter than min nor wider than max (ortho half-height).")]
        public float cameraMinZoom = 9f;
        public float cameraMaxZoom = 30f;
        [Tooltip("World-unit margin kept around the players when framing.")]
        public float cameraMargin = 5f;
        [Tooltip("Follow/zoom smoothing (higher = snappier).")]
        public float cameraFollowSpeed = 4f;
        [Tooltip("Zoom for the goal fly-by during the intro pan, as a fraction of the play radius.")]
        public float establishZoom = 0.4f;

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
        WindfallPostFx _postFx;

        // --- round flow state ---
        Phase _phase;
        float _phaseT;
        int _round;               // 0-based index of the current round

        // --- camera pan ---
        struct CamPose { public Vector2 pos; public float size; public CamPose(Vector2 p, float s) { pos = p; size = s; } }
        Camera _cam;
        float _camHomeZ = -10f;
        readonly List<CamPose> _panPoses = new List<CamPose>();

        // --- per-level state ---
        float _levelSize;
        float _playRadius;
        Vector2 _targetPos;
        readonly List<Vector3> _spawnPositions = new List<Vector3>();
        const float SpawnZ = -0.5f;

        static readonly Color[] Palette = {
            new Color(1f, 0.35f, 0.35f), new Color(0.4f, 0.62f, 1f), new Color(0.45f, 0.9f, 0.45f),
            new Color(1f, 0.85f, 0.3f), new Color(0.9f, 0.45f, 1f), new Color(0.35f, 0.9f, 0.9f),
        };
        // Keyboard keys for auto-built players, and the laptop fallback when a gamepad player has no pad connected.
        // Spread across the keyboard (A F on the left, J L on the right) so up to four can share one keyboard.
        static readonly Key[] FallbackKeys = { Key.A, Key.F, Key.J, Key.L, Key.Z, Key.M };

        void Start() {
            EnsureHud();
            EnsurePostFx();
            CacheCameraHome();
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

        void EnsurePostFx() {
            if (!bloom || _postFx != null) return;
            _postFx = new GameObject("WindfallPostFx").AddComponent<WindfallPostFx>();
        }

        // ------------------------------------------------------------------ round setup

        /// <summary>Set up the current round: (re)spawn players frozen at their pads and restore collectibles.
        /// Cumulative totals are preserved — only per-round state resets.</summary>
        public void StartRound() {
            EnsureConfigs();
            EnsureRunners();
            _zoneScoredCount = 0;

            PrepareLevel();          // pick this level's size; scale out-of-bounds + camera off it
            PlaceTargetAndSpawns();  // randomise the target and the non-overlapping launch ring

            // Procedural level (GAME_DESIGN §4/§6): rebuild a fresh random field each round, sized to this level,
            // populating the existing Group in place (if `field` is one) so the scene's field visualiser keeps working.
            if (generateLevels) {
                if (levelGenerator == null) levelGenerator = gameObject.AddComponent<WindfallLevelGenerator>();
                field = levelGenerator.Generate(Random.Range(int.MinValue, int.MaxValue),
                                                field as GroupVectorFieldComponent, _targetPos, _levelSize);
            }

            // Refresh + scatter collectibles across the (now larger) play area.
            _collectibles.Clear();
            _collectibles.AddRange(FindObjectsByType<Collectible>(FindObjectsInactive.Include));
            foreach (var c in _collectibles) {
                c.ResetCollectible();
                Vector2 cp = Random.insideUnitCircle * (_playRadius * 0.8f);
                c.transform.position = new Vector3(cp.x, cp.y, c.transform.position.z);
            }

            if (playerPrefab == null) { Debug.LogWarning("WindfallGame: no playerPrefab assigned.", this); return; }

            for (int i = 0; i < _runners.Count; i++) {
                var r = _runners[i];
                if (r.glider != null) Destroy(r.glider.gameObject);
                r.score = 0; r.finished = false; r.scoredZone = false; r.zoneRank = -1;

                var cfg = r.cfg;
                Vector3 pos = i < _spawnPositions.Count ? _spawnPositions[i]
                    : (cfg.spawnPoint != null ? cfg.spawnPoint.position : Vector3.zero);
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

            BuildPanWaypoints();   // frame the intro pan from this level's spawns + target
        }

        void PrepareLevel() {
            _levelSize = generateLevels
                ? Random.Range(levelSizeRange.x, levelSizeRange.y)
                : (field != null ? Mathf.Max(1f, Mathf.Abs(field.transform.lossyScale.x)) : levelSizeRange.x);
            _playRadius = _levelSize * 0.5f;
            outOfBoundsRadius = _playRadius * 1.12f;     // a little beyond the field edge
        }

        void PlaceTargetAndSpawns() {
            // Target: a random spot out from the centre.
            float tAng = Random.Range(0f, Mathf.PI * 2f);
            float tDist = _playRadius * Random.Range(0.45f, 0.7f);
            _targetPos = new Vector2(Mathf.Cos(tAng), Mathf.Sin(tAng)) * tDist;
            if (targetRing != null) {
                var p = targetRing.transform.position;
                targetRing.transform.position = new Vector3(_targetPos.x, _targetPos.y, p.z);
            }

            // Launch cluster: roughly opposite the target so there's a flight to make.
            float lAng = tAng + Mathf.PI + Random.Range(-0.6f, 0.6f);
            float lDist = _playRadius * Random.Range(0.45f, 0.7f);
            Vector2 launch = new Vector2(Mathf.Cos(lAng), Mathf.Sin(lAng)) * lDist;

            // Ring of non-overlapping starts around the cluster (balls can never touch at spawn).
            int n = _runners.Count;
            float ballR = settings != null ? settings.radius : 0.5f;
            float minSep = ballR * 2f * 1.35f;
            float ring = n >= 2 ? Mathf.Max(minSep, minSep / (2f * Mathf.Sin(Mathf.PI / n))) : 0f;
            float phase = Random.Range(0f, Mathf.PI * 2f);
            _spawnPositions.Clear();
            for (int i = 0; i < n; i++) {
                float a = phase + i * (Mathf.PI * 2f / Mathf.Max(1, n));
                Vector2 sp = launch + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                _spawnPositions.Add(new Vector3(sp.x, sp.y, SpawnZ));
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
            } else {
                players = new List<WindfallPlayerConfig>();
                if (spawnParent == null) { Debug.LogWarning("WindfallGame: no spawnParent and no players configured.", this); return; }
                int n = spawnParent.childCount;
                for (int i = 0; i < n; i++) {
                    players.Add(new WindfallPlayerConfig {
                        name = "P" + (i + 1),
                        color = Palette[i % Palette.Length],
                        spawnPoint = spawnParent.GetChild(i),
                        input = new WindfallInput {
                            source = WindfallInput.Source.KeyboardKey,
                            key = FallbackKeys[Mathf.Min(i, FallbackKeys.Length - 1)],
                        },
                    });
                }
            }

            // Laptop fallback: a gamepad-bound player with no matching pad connected uses a keyboard key instead,
            // so every player is controllable on a MacBook. A real gamepad, when present, still takes over.
            for (int i = 0; i < players.Count; i++) {
                var inp = players[i].input;
                if (inp.source == WindfallInput.Source.GamepadSouth && inp.gamepadIndex >= Gamepad.all.Count) {
                    inp.source = WindfallInput.Source.KeyboardKey;
                    inp.key = FallbackKeys[Mathf.Min(i, FallbackKeys.Length - 1)];
                }
            }
        }

        static void ApplyColor(WindGlider g, Color col) {
            // Ball: tinted polished metal (a ball bearing). Build a fresh URP/Lit material so it's genuinely
            // metallic regardless of the prefab's (empty) material slot; a faint self-glow keeps it readable
            // on the dark instrument panel.
            var mr = g.GetComponentInChildren<MeshRenderer>();
            if (mr != null) {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) {
                    var ball = new Material(lit);
                    ball.SetColor("_BaseColor", col);
                    ball.SetFloat("_Metallic", 0.95f);
                    ball.SetFloat("_Smoothness", 0.8f);
                    ball.EnableKeyword("_EMISSION");
                    ball.SetColor("_EmissionColor", col * 0.25f);
                    mr.material = ball;
                } else {
                    var m = mr.material;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                    if (m.HasProperty("_Color")) m.color = col;
                }
            }

            // Trail: a molten/plasma path — white-hot where it's freshly laid at the ball, cooling to the
            // player's colour along the path. Additive so it glows on the dark panel (there's no bloom volume).
            if (g.trail != null) {
                g.trail.material = MakeAdditiveMaterial();
                var hot = Color.Lerp(col, Color.white, 0.8f);
                var warm = Color.Lerp(col, Color.white, 0.3f);
                var grad = new Gradient { mode = GradientMode.Blend };
                grad.SetKeys(
                    new[] {
                        new GradientColorKey(hot, 0f),      // at the ball: white-hot
                        new GradientColorKey(warm, 0.18f),
                        new GradientColorKey(col, 1f),      // tail: cooled to the player colour
                    },
                    new[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.7f, 0.6f),
                        new GradientAlphaKey(0.35f, 1f),    // stays visible so the whole path reads
                    });
                g.trail.colorGradient = grad;
                g.trail.time = 12f;   // long enough to keep the whole flight path on screen
            }

            if (g.aimLine != null) {
                g.aimLine.material = new Material(Shader.Find("Sprites/Default"));
                g.aimLine.startColor = col;
                g.aimLine.endColor = new Color(col.r, col.g, col.b, 0.5f);
            }
        }

        // A transparent-additive material for the trails so overlapping/bright segments glow like hot plasma.
        // Uses URP's particle unlit shader (honours the trail's vertex colours); falls back to Sprites/Default.
        static Material MakeAdditiveMaterial() {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);       // additive
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            return mat;
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
                    UpdateFollowCamera();
                    _hud.SetTimer(roundTimeLimit > 0f, roundTimeLimit > 0f ? Mathf.Clamp01(1f - _phaseT / roundTimeLimit) : 0f);
                    _hud.SetGoalArrow(true, targetRing != null ? targetRing.transform.position : (Vector3)_targetPos);
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
            _hud.SetTimer(false, 0f);
            _hud.SetGoalArrow(false, Vector3.zero);
            ApplyPose(_panPoses.Count > 0 ? _panPoses[0] : Home());   // establishing shot behind the black
            _hud.SetFade(1f);
            SetPhase(Phase.FadeIn);
        }

        void EnterPan() { SetPhase(Phase.Pan); }

        void EnterRoundName() {
            ApplyPose(Home());
            FreezeAll(true);
            _hud.SetBanner(CurrentRoundName(), 0f);
            _hud.SetTimer(false, 0f);
            _hud.SetGoalArrow(false, Vector3.zero);
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
            _hud.SetTimer(false, 0f);
            _hud.SetGoalArrow(false, Vector3.zero);
            _hud.SetResults(true, "Round " + (_round + 1) + " — Totals");
            SetPhase(Phase.Results);
        }

        void EnterFadeOut() { SetPhase(Phase.FadeOut); }

        void EnterGameOver() {
            _hud.SetFade(0f);
            _hud.SetBarVisible(false);
            _hud.SetTimer(false, 0f);
            _hud.SetGoalArrow(false, Vector3.zero);
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

        void CacheCameraHome() {
            _cam = Camera.main;
            if (_cam != null) _camHomeZ = _cam.transform.position.z;
        }

        // Pan the intended flight: whole-level overview → goal fly-by → settle on the players' start.
        // Rebuilt each round from the level's actual spawns + target.
        void BuildPanWaypoints() {
            _panPoses.Clear();
            _panPoses.Add(new CamPose(Vector2.zero, _playRadius * 1.05f));                                  // whole-level overview
            _panPoses.Add(new CamPose(_targetPos, Mathf.Clamp(_playRadius * establishZoom, cameraMinZoom, cameraMaxZoom))); // the goal
            _panPoses.Add(Home());                                                                          // settle on the start
        }

        // "Home" = the players' start framing (used as the pan's final pose and the Playing hand-off).
        CamPose Home() {
            Vector2 c = SpawnCentroid();
            float size = Mathf.Clamp(SpawnExtent() + cameraMargin, cameraMinZoom, cameraMaxZoom);
            return new CamPose(c, size);
        }

        Vector2 SpawnCentroid() {
            if (_spawnPositions.Count == 0) return Vector2.zero;
            Vector2 c = Vector2.zero;
            foreach (var p in _spawnPositions) c += (Vector2)p;
            return c / _spawnPositions.Count;
        }

        float SpawnExtent() {
            Vector2 c = SpawnCentroid();
            float e = 0f;
            foreach (var p in _spawnPositions) e = Mathf.Max(e, Vector2.Distance(c, (Vector2)p));
            return e;
        }

        // Follow the active (still-flying) players and zoom to fit them, clamped so they're never too small
        // and never zoomed out past the cap. Smoothed. Used every frame during Playing.
        void UpdateFollowCamera() {
            if (_cam == null) return;
            bool any = false;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            foreach (var r in _runners) {
                if (r.finished || r.glider == null) continue;
                Vector2 p = r.glider.transform.position;
                if (!any) { min = max = p; any = true; }
                else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            }
            if (!any) return;   // nobody flying — hold the last framing

            Vector2 c = (min + max) * 0.5f;
            float aspect = _cam.aspect > 0.01f ? _cam.aspect : 1.6f;
            float halfW = (max.x - min.x) * 0.5f, halfH = (max.y - min.y) * 0.5f;
            float fit = Mathf.Max(halfW / aspect, halfH) + cameraMargin;
            float size = Mathf.Clamp(fit, cameraMinZoom, cameraMaxZoom);

            float k = 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime);
            Vector2 np = Vector2.Lerp((Vector2)_cam.transform.position, c, k);
            _cam.transform.position = new Vector3(np.x, np.y, _camHomeZ);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, size, k);
        }

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
            _cam.transform.position = new Vector3(pose.pos.x, pose.pos.y, _camHomeZ);
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
