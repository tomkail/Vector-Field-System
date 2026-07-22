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
    /// Simultaneous local-multiplayer round (GAME_DESIGN.md §9 simultaneous mode + §12 game loop). Spawns one
    /// player per spawn point, hands each its own field consumer + input, and runs the round: fly at once, collect
    /// scattered points, and score by SETTLING inside the <see cref="TargetRing"/>. Players who settle inside earn
    /// the zone's points plus a rank bonus by settle ORDER (first in gets the biggest bonus). Out of bounds ends a
    /// player's run with no zone score. Backspace resets the level. Presentation is a runtime <see cref="WindfallHUD"/>
    /// (created on start) that reads the player views below and the <see cref="OnPointsGained"/> event.
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

        [Header("Players (auto-filled from spawn children if left empty)")]
        public List<WindfallPlayerConfig> players = new List<WindfallPlayerConfig>();

        class Runner {
            public WindfallPlayerConfig cfg;
            public WindGlider glider;
            public int score;
            public bool finished;
            public bool scoredZone;
            public int zoneRank = -1;
        }

        /// <summary>Read-only view of a player for the HUD (GAME_DESIGN.md §7a).</summary>
        public struct PlayerView {
            public string name;
            public Color color;
            public int score;
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
                name = r.cfg.name, color = r.cfg.color, score = r.score,
                finished = r.finished, scoredZone = r.scoredZone, zoneRank = r.zoneRank,
                tracked = r.glider != null ? r.glider.transform : null,
            };
        }

        readonly List<Runner> _runners = new List<Runner>();
        readonly List<Collectible> _collectibles = new List<Collectible>();
        int _zoneScoredCount;
        WindfallHUD _hud;

        static readonly Color[] Palette = {
            new Color(1f, 0.35f, 0.35f), new Color(0.4f, 0.62f, 1f), new Color(0.45f, 0.9f, 0.45f),
            new Color(1f, 0.85f, 0.3f), new Color(0.9f, 0.45f, 1f), new Color(0.35f, 0.9f, 0.9f),
        };
        static readonly WindfallInput.Source[] DefaultSources = {
            WindfallInput.Source.KeyboardSpace, WindfallInput.Source.KeyboardEnter,
            WindfallInput.Source.GamepadSouth, WindfallInput.Source.GamepadSouth,
        };

        void Start() {
            BuildRound();
            EnsureHud();
        }

        void EnsureHud() {
            if (_hud != null) return;
            var go = new GameObject("WindfallHUD");
            _hud = go.AddComponent<WindfallHUD>();
            _hud.Init(this);
        }

        public void BuildRound() {
            // Tear down any previous round.
            foreach (var r in _runners) if (r.glider != null) Destroy(r.glider.gameObject);
            _runners.Clear();
            _zoneScoredCount = 0;

            // Refresh collectibles (find any in the scene, restore them).
            _collectibles.Clear();
            _collectibles.AddRange(FindObjectsByType<Collectible>(FindObjectsInactive.Include));
            foreach (var c in _collectibles) c.ResetCollectible();

            EnsureConfigs();

            if (playerPrefab == null) { Debug.LogWarning("WindfallGame: no playerPrefab assigned.", this); return; }

            foreach (var cfg in players) {
                Vector3 pos = cfg.spawnPoint != null ? cfg.spawnPoint.position : Vector3.zero;
                var g = Instantiate(playerPrefab, pos, Quaternion.identity);
                // Configure while inactive so WindGlider.OnEnable registers its field consumer with the field
                // already assigned. (The prefab can't hold a scene-field reference, so a live instance would
                // otherwise enable with field==null and never register — the CPU mirror would stay empty.)
                g.gameObject.SetActive(false);
                g.name = "Player_" + cfg.name;
                g.field = field;
                g.settings = settings;
                g.input = cfg.input;
                ApplyColor(g, cfg.color);
                g.gameObject.AddComponent<WindfallJuice>();   // §7a feedback; subscribes to the glider's events on enable
                var runner = new Runner { cfg = cfg, glider = g };
                g.OnSettle += _ => OnSettle(runner, g.transform.position);
                g.gameObject.SetActive(true);
                _runners.Add(runner);
            }
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

        void Update() {
            if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame) BuildRound();

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
    }
}
