using System.Collections.Generic;
using UnityEngine;

namespace Windfall {
    /// <summary>
    /// Drives the Windfall game camera: the skippable intro pan (overview → goal fly-by → players' start) and
    /// the in-play follow-cam that frames the active players, leads toward the goal, and keeps everyone clear of
    /// the top HUD strip. Extracted from <see cref="WindfallGame"/> so the framing logic lives in one place;
    /// the game feeds it the per-level data (spawns, target, play radius) and the live player positions.
    ///
    /// When <see cref="drawGizmos"/> is on it draws Scene-view gizmos (during play) that showcase what the
    /// camera is doing: the pan waypoints and their view rects, the players' bounding box, the lead point and
    /// its reach toward the goal, the goal's breathing-room pad, and the final view rect with its usable
    /// (below-HUD) region highlighted.
    /// </summary>
    [DisallowMultipleComponent]
    public class WindfallCamera : MonoBehaviour {
        [Header("Zoom / framing")]
        [Tooltip("Follows the active players and zooms to fit them; never tighter than min nor wider than max (ortho half-height).")]
        public float minZoom = 9f;
        public float maxZoom = 30f;
        [Tooltip("World-unit margin kept around the players when framing.")]
        public float margin = 5f;
        [Tooltip("Follow/zoom smoothing (higher = snappier).")]
        public float followSpeed = 4f;
        [Tooltip("How far ahead (toward the goal) the camera looks, as a multiple of the view half-height. Keeps players on the trailing edge so what's coming is visible. 0 = centred on the players.")]
        public float lead = 0.8f;
        [Tooltip("Zoom for the goal fly-by during the intro pan, as a fraction of the play radius.")]
        public float establishZoom = 0.4f;

        [Header("Gizmos")]
        [Tooltip("Draw Scene-view gizmos (during play) showing the pan path and the live follow framing.")]
        public bool drawGizmos = true;

        struct CamPose { public Vector2 pos; public float size; public CamPose(Vector2 p, float s) { pos = p; size = s; } }

        Camera _cam;
        float _homeZ = -10f;
        TargetRing _targetRing;
        WindfallHUD _hud;
        readonly List<CamPose> _panPoses = new List<CamPose>();

        // Per-level data captured for framing + gizmos.
        readonly List<Vector2> _spawns = new List<Vector2>();
        Vector2 _targetPos;
        float _playRadius;

        // Live follow state, retained so gizmos can visualise the most recent framing.
        readonly List<Vector2> _activePlayers = new List<Vector2>();
        Vector2 _playersMin, _playersMax, _framedCenter, _leadPoint;
        float _framedSize, _topFrac;
        bool _hasFraming, _hasLead;

        public float HomeZ => _homeZ;

        /// <summary>Grab the main camera + remember its Z plane, and the objects framing needs. Call once at startup.</summary>
        public void Acquire(TargetRing targetRing, WindfallHUD hud) {
            _cam = Camera.main;
            if (_cam != null) _homeZ = _cam.transform.position.z;
            _targetRing = targetRing;
            _hud = hud;
        }

        /// <summary>Capture a fresh level and (re)build the intro-pan waypoints from its spawns + target.</summary>
        public void BeginLevel(IReadOnlyList<Vector3> spawnPositions, Vector2 targetPos, float playRadius) {
            _spawns.Clear();
            if (spawnPositions != null) foreach (var p in spawnPositions) _spawns.Add((Vector2)p);
            _targetPos = targetPos;
            _playRadius = playRadius;
            _hasFraming = false; _hasLead = false;

            // Pan the intended flight: whole-level overview → goal fly-by → settle on the players' start.
            _panPoses.Clear();
            _panPoses.Add(new CamPose(Vector2.zero, _playRadius * 1.05f));
            _panPoses.Add(new CamPose(_targetPos, Mathf.Clamp(_playRadius * establishZoom, minZoom, maxZoom)));
            _panPoses.Add(Home());
        }

        /// <summary>Snap to the pan's opening pose (used behind the fade-in).</summary>
        public void ApplyPanStart() => ApplyPose(_panPoses.Count > 0 ? _panPoses[0] : Home());

        /// <summary>Interpolate along the intro pan; <paramref name="u01"/> in [0,1].</summary>
        public void UpdatePan(float u01) => ApplyPose(SamplePan(u01));

        /// <summary>Snap to the players' start framing (pan end / Playing hand-off).</summary>
        public void ApplyHome() => ApplyPose(Home());

        /// <summary>
        /// Follow the active players: frame their bounding box, lead toward the goal (capped just past it so the
        /// goal never sits on the edge), reserve the top HUD strip, and smooth toward the result. Pass the world
        /// positions of the still-flying players; an empty list holds the last framing.
        /// </summary>
        public void UpdateFollow(IReadOnlyList<Vector2> activePlayers) {
            if (_cam == null) return;
            _activePlayers.Clear();
            if (activePlayers != null) for (int i = 0; i < activePlayers.Count; i++) _activePlayers.Add(activePlayers[i]);
            if (_activePlayers.Count == 0) return;   // nobody flying — hold the last framing

            Vector2 min = _activePlayers[0], max = _activePlayers[0];
            for (int i = 1; i < _activePlayers.Count; i++) { min = Vector2.Min(min, _activePlayers[i]); max = Vector2.Max(max, _activePlayers[i]); }
            _playersMin = min; _playersMax = max;
            Vector2 pc = (min + max) * 0.5f;
            float halfW = (max.x - min.x) * 0.5f, halfH = (max.y - min.y) * 0.5f;

            // Zoom fits the PLAYERS alone (never too small / too big), reserving the top HUD strip: grow the
            // zoom for the reduced usable height. Keeping the lead out of the zoom is what lets it work evenly
            // on both axes — otherwise the axis with the wider player spread swallows the lead on the other.
            float aspect = Aspect();
            _topFrac = _hud != null ? Mathf.Clamp01(_hud.TopInsetPixels() / Mathf.Max(1f, Screen.height)) : 0f;
            float usable = Mathf.Max(0.35f, 1f - _topFrac);
            float needH = (halfH + margin) / usable;
            float needW = (halfW + margin) / aspect;
            float size = Mathf.Clamp(Mathf.Max(needH, needW), minZoom, maxZoom);

            // Lead: slide the centre toward the goal so players ride the trailing edge and the space ahead is
            // visible. Done as a per-axis offset that consumes the spare room (slack) between the players' box
            // and the usable view — independently in x and y, so a diagonal goal leads on BOTH axes. Bounded by
            // the slack (players never leave the usable view) and never past the goal centre (goal stays ahead).
            float viewHalfW = size * aspect;
            float usableHalfH = size * usable;
            float slackX = Mathf.Max(0f, viewHalfW - halfW - margin);
            float slackY = Mathf.Max(0f, usableHalfH - halfH - margin);

            Vector2 toTarget = _targetPos - pc;
            float dToTarget = toTarget.magnitude;
            Vector2 offset = Vector2.zero;
            _hasLead = false;
            if (lead > 0f && dToTarget > 0.001f) {
                Vector2 dir = toTarget / dToTarget;
                offset = new Vector2(dir.x * slackX, dir.y * slackY) * lead;
                if (offset.magnitude > dToTarget) offset *= dToTarget / offset.magnitude;   // don't overshoot the goal
                _leadPoint = pc + offset;
                _hasLead = offset.sqrMagnitude > 0.0001f;
            }

            Vector2 c = pc + offset;
            c.y += _topFrac * size;   // lift so the top strip covers empty sky, not the players

            _framedCenter = c; _framedSize = size; _hasFraming = true;

            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            Vector2 np = Vector2.Lerp((Vector2)_cam.transform.position, c, k);
            _cam.transform.position = new Vector3(np.x, np.y, _homeZ);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, size, k);
        }

        // Breathing room to leave beyond the goal centre = its (scaled) outer ring radius plus a small pad.
        float GoalPad() {
            float pad = 1.5f;
            if (_targetRing != null) {
                Vector3 s = _targetRing.transform.lossyScale;
                pad += _targetRing.OuterRadius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            }
            return pad;
        }

        // "Home" = the players' start framing (pan's final pose and the Playing hand-off).
        CamPose Home() {
            Vector2 c = SpawnCentroid();
            float size = Mathf.Clamp(SpawnExtent() + margin, minZoom, maxZoom);
            return new CamPose(c, size);
        }

        Vector2 SpawnCentroid() {
            if (_spawns.Count == 0) return Vector2.zero;
            Vector2 c = Vector2.zero;
            foreach (var p in _spawns) c += p;
            return c / _spawns.Count;
        }

        float SpawnExtent() {
            Vector2 c = SpawnCentroid();
            float e = 0f;
            foreach (var p in _spawns) e = Mathf.Max(e, Vector2.Distance(c, p));
            return e;
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
            _cam.transform.position = new Vector3(pose.pos.x, pose.pos.y, _homeZ);
            if (_cam.orthographic) _cam.orthographicSize = pose.size;
        }

        float Aspect() => (_cam != null && _cam.aspect > 0.01f) ? _cam.aspect : 16f / 9f;

        // ------------------------------------------------------------------ gizmos

        void OnDrawGizmos() {
            if (!drawGizmos || _panPoses.Count == 0) return;   // only meaningful once a level has begun (play mode)
            float aspect = Aspect();

            // Intro-pan waypoints: each framing rect, a node, and the path between them.
            Gizmos.color = new Color(0.3f, 0.75f, 1f, 0.9f);
            for (int i = 0; i < _panPoses.Count; i++) {
                DrawViewRect(_panPoses[i].pos, _panPoses[i].size, aspect);
                Gizmos.DrawSphere(_panPoses[i].pos, Mathf.Max(0.15f, _panPoses[i].size * 0.03f));
                if (i > 0) Gizmos.DrawLine(_panPoses[i - 1].pos, _panPoses[i].pos);
            }

            // Goal breathing-room pad (how far past the goal centre the follow-cam is allowed to reach).
            Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            DrawCircle(_targetPos, GoalPad(), 40);

            if (!_hasFraming) return;

            // Players' bounding box + a dot per active player.
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            DrawRectMinMax(_playersMin, _playersMax);
            foreach (var p in _activePlayers) Gizmos.DrawSphere(p, Mathf.Max(0.1f, _framedSize * 0.02f));

            // Lead point and its reach from the players' centre toward the goal.
            if (_hasLead) {
                Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.95f);
                Vector2 pc = (_playersMin + _playersMax) * 0.5f;
                Gizmos.DrawLine(pc, _leadPoint);
                Gizmos.DrawSphere(_leadPoint, Mathf.Max(0.12f, _framedSize * 0.025f));
            }

            // Final view rect (green) + the usable region below the HUD strip (faint green fill outline).
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.95f);
            DrawViewRect(_framedCenter, _framedSize, aspect);
            if (_topFrac > 0.0001f) {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.4f);
                float usableHalfH = _framedSize * (1f - _topFrac);
                Vector2 uc = new Vector2(_framedCenter.x, _framedCenter.y - _framedSize * _topFrac);
                DrawRect(uc, _framedSize * aspect, usableHalfH);
            }
        }

        void DrawViewRect(Vector2 center, float size, float aspect) => DrawRect(center, size * aspect, size);

        // Draw an axis-aligned rectangle from a centre and half-extents.
        void DrawRect(Vector2 c, float halfW, float halfH) {
            Vector3 a = new Vector3(c.x - halfW, c.y - halfH), b = new Vector3(c.x + halfW, c.y - halfH),
                    d = new Vector3(c.x + halfW, c.y + halfH), e = new Vector3(c.x - halfW, c.y + halfH);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, d); Gizmos.DrawLine(d, e); Gizmos.DrawLine(e, a);
        }

        void DrawRectMinMax(Vector2 min, Vector2 max) {
            Vector3 a = new Vector3(min.x, min.y), b = new Vector3(max.x, min.y),
                    c = new Vector3(max.x, max.y), d = new Vector3(min.x, max.y);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
        }

        void DrawCircle(Vector2 c, float r, int seg) {
            Vector3 prev = new Vector3(c.x + r, c.y, 0f);
            for (int i = 1; i <= seg; i++) {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0f);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
