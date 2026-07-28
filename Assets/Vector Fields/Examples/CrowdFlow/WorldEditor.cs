using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CrowdFlow {
    /// <summary>
    /// The play-mode landscape editor — the headline of the demo, and the public-facing control surface. Sculpt the
    /// terrain, raise/lower the water, or drop blocking obstacles, and watch every crowd re-route live. The UI is fully
    /// mouse-usable (click the brush buttons, drag the size slider, hit Reset) as well as keyboard-driven (1–4, scroll),
    /// with a ground-conforming brush ring so you can see where you're painting.
    ///
    /// Terrain edits go through <c>TerrainData.SetHeights</c>, which fires <c>TerrainCallbacks.heightmapChanged</c>; the
    /// manager listens for that and re-solves, so this editor never has to tell the manager the terrain changed — the
    /// engine's own dirty-region signal closes the loop. Water and obstacle edits aren't heightmap changes, so those call
    /// the manager's explicit dirty hooks. The original heightmap is snapshotted on Start and restored on exit, so a play
    /// session never permanently mutates the shared TerrainData asset.
    /// </summary>
    public class WorldEditor : MonoBehaviour {
        public enum Brush { Raise, Lower, Water, Obstacle }

        [Header("Wiring")]
        public CrowdFlowManager manager;
        [Tooltip("Camera used for picking (defaults to Camera.main).")]
        public Camera pickCamera;

        [Header("Brush")]
        public Brush brush = Brush.Raise;
        [Tooltip("Brush radius in world units.")]
        public float brushRadius = 8f;
        [Tooltip("Sculpt strength in world height units per second of painting.")]
        public float sculptStrength = 12f;
        [Tooltip("Water level change per second while in Water mode.")]
        public float waterSpeed = 4f;

        [Header("Obstacles")]
        [Tooltip("Layer placed obstacles live on. MUST be included in the manager's Blocked Mask.")]
        public int obstacleLayer = 0;
        public Vector3 obstacleSize = new Vector3(4f, 6f, 4f);

        // Per-brush accent colours (buttons + brush ring). Order matches the Brush enum.
        static readonly Color[] BrushColors = {
            new Color(0.90f, 0.58f, 0.22f),   // Raise  — earthy orange
            new Color(0.55f, 0.44f, 0.34f),   // Lower  — dug soil
            new Color(0.28f, 0.62f, 0.95f),   // Water  — blue
            new Color(0.88f, 0.32f, 0.32f),   // Obstacle — red
        };
        static readonly string[] BrushNames = { "Raise", "Lower", "Water", "Obstacle" };

        Terrain _terrain;
        CrowdDirector _director;
        FlowArrowVisualizer _arrows;

        // Original heightmap snapshot (restored on Reset and on exit so the shared asset isn't permanently edited).
        float[,] _originalHeights;
        float _initialWater;

        // Brush ring cursor.
        LineRenderer _ring;
        const int RingSegments = 64;
        bool _ringVisible;

        // UI.
        float _uiScale = 1f;
        Rect _panelRect;
        bool _pointerOverUI;
        GUIStyle _title, _subtitle, _btn, _stat, _hint, _sliderLabel;
        Texture2D _panelTex, _accentTex;

        void Start() {
            if (manager == null) manager = GetComponent<CrowdFlowManager>();
            if (pickCamera == null) pickCamera = Camera.main;
            _director = GetComponent<CrowdDirector>();
            _arrows = GetComponent<FlowArrowVisualizer>();
            if (manager != null) {
                _terrain = manager.terrain;
                _initialWater = manager.waterLevel;
            }
            if (_terrain != null) {
                var td = _terrain.terrainData;
                int res = td.heightmapResolution;
                _originalHeights = td.GetHeights(0, 0, res, res);
            }
            BuildRing();
        }

        void OnDisable() {
            // Return the shared TerrainData asset to its authored shape so play-mode sculpting doesn't persist.
            if (_terrain != null && _originalHeights != null) {
                try { _terrain.terrainData.SetHeights(0, 0, _originalHeights); } catch { /* scene teardown */ }
            }
        }

        void Update() {
            if (manager == null || _terrain == null) return;
            PollBrushSelection();

            var mouse = Mouse.current;
            if (mouse == null) { HideRing(); return; }

            Vector2 screen = mouse.position.ReadValue();
            bool onGround = RaycastGround(screen, out Vector3 hit);

            // Brush ring follows the cursor on the ground (hidden over the UI, off-ground, or in global Water mode).
            if (onGround && !_pointerOverUI && brush != Brush.Water) UpdateRing(hit); else HideRing();

            if (_pointerOverUI) return;   // don't paint through the panel

            bool lmb = mouse.leftButton.isPressed;
            bool rmb = mouse.rightButton.isPressed;
            if (!lmb && !rmb) return;
            if (!onGround) return;

            switch (brush) {
                case Brush.Raise:  Sculpt(hit, +sculptStrength * Time.deltaTime); break;
                case Brush.Lower:  Sculpt(hit, -sculptStrength * Time.deltaTime); break;
                case Brush.Water:  manager.SetWaterLevel(manager.waterLevel + (lmb ? 1f : -1f) * waterSpeed * Time.deltaTime); break;
                case Brush.Obstacle:
                    if (mouse.leftButton.wasPressedThisFrame) PlaceObstacle(hit);
                    else if (mouse.rightButton.wasPressedThisFrame) RemoveObstacle(hit);
                    break;
            }
        }

        void PollBrushSelection() {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.digit1Key.wasPressedThisFrame) brush = Brush.Raise;
            if (k.digit2Key.wasPressedThisFrame) brush = Brush.Lower;
            if (k.digit3Key.wasPressedThisFrame) brush = Brush.Water;
            if (k.digit4Key.wasPressedThisFrame) brush = Brush.Obstacle;
            if (k.gKey.wasPressedThisFrame) ResetWorld();   // (R is the camera tilt key; use G to reset the world)
            if (k.vKey.wasPressedThisFrame && _arrows != null) _arrows.Cycle();
            // Brush size on the bracket keys (scroll now zooms the camera).
            if (k.leftBracketKey.isPressed)  brushRadius = Mathf.Clamp(brushRadius - 24f * Time.unscaledDeltaTime, 1f, 60f);
            if (k.rightBracketKey.isPressed) brushRadius = Mathf.Clamp(brushRadius + 24f * Time.unscaledDeltaTime, 1f, 60f);
        }

        bool RaycastGround(Vector2 screenPos, out Vector3 hit) {
            hit = default;
            if (pickCamera == null) return false;
            Ray ray = pickCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit h, 5000f)) { hit = h.point; return true; }
            return false;
        }

        // ---------------------------------------------------------------- brush ring

        void BuildRing() {
            var go = new GameObject("BrushRing");
            go.transform.SetParent(transform, false);
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = RingSegments;
            _ring.widthMultiplier = 0.5f;
            _ring.numCornerVertices = 2;
            _ring.textureMode = LineTextureMode.Stretch;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _ring.material = new Material(sh);
            _ring.enabled = false;
        }

        void UpdateRing(Vector3 centre) {
            if (_ring == null) return;
            Color c = BrushColors[(int)brush];
            _ring.startColor = _ring.endColor = c;
            if (_ring.material.HasProperty("_BaseColor")) _ring.material.SetColor("_BaseColor", c);
            if (_ring.material.HasProperty("_Color")) _ring.material.color = c;
            for (int i = 0; i < RingSegments; i++) {
                float a = (i / (float)RingSegments) * Mathf.PI * 2f;
                float x = centre.x + Mathf.Cos(a) * brushRadius;
                float z = centre.z + Mathf.Sin(a) * brushRadius;
                float y = manager.SurfaceY(x, z) + 0.3f;   // hug the terrain
                _ring.SetPosition(i, new Vector3(x, y, z));
            }
            if (!_ringVisible) { _ring.enabled = true; _ringVisible = true; }
        }

        void HideRing() {
            if (_ring != null && _ringVisible) { _ring.enabled = false; _ringVisible = false; }
        }

        // ---------------------------------------------------------------- edits

        // Radial Gaussian sculpt on the terrain heightmap. The SetHeights write triggers heightmapChanged, which the
        // manager turns into a localised cost re-sample + re-solve.
        void Sculpt(Vector3 worldCentre, float deltaWorld) {
            TerrainData td = _terrain.terrainData;
            int hr = td.heightmapResolution;
            Vector3 local = worldCentre - _terrain.transform.position;
            float u = local.x / td.size.x, v = local.z / td.size.z;

            int radXTexels = Mathf.Max(1, Mathf.RoundToInt(brushRadius / td.size.x * (hr - 1)));
            int radZTexels = Mathf.Max(1, Mathf.RoundToInt(brushRadius / td.size.z * (hr - 1)));
            int cx = Mathf.RoundToInt(u * (hr - 1));
            int cz = Mathf.RoundToInt(v * (hr - 1));

            int x0 = Mathf.Clamp(cx - radXTexels, 0, hr - 1);
            int z0 = Mathf.Clamp(cz - radZTexels, 0, hr - 1);
            int x1 = Mathf.Clamp(cx + radXTexels, 0, hr - 1);
            int z1 = Mathf.Clamp(cz + radZTexels, 0, hr - 1);
            int w = x1 - x0 + 1, hgt = z1 - z0 + 1;
            if (w <= 0 || hgt <= 0) return;

            float[,] heights = td.GetHeights(x0, z0, w, hgt);   // [z, x], normalized 0..1
            float deltaNorm = deltaWorld / td.size.y;
            for (int z = 0; z < hgt; z++) {
                for (int x = 0; x < w; x++) {
                    float ddx = (x0 + x - cx) / (float)radXTexels;
                    float ddz = (z0 + z - cz) / (float)radZTexels;
                    float d2 = ddx * ddx + ddz * ddz;
                    if (d2 > 1f) continue;
                    float falloff = Mathf.Exp(-d2 * 3f);
                    heights[z, x] = Mathf.Clamp01(heights[z, x] + deltaNorm * falloff);
                }
            }
            td.SetHeights(x0, z0, heights);
        }

        void PlaceObstacle(Vector3 worldCentre) {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Obstacle";
            go.tag = "Untagged";
            go.layer = obstacleLayer;
            go.transform.position = worldCentre + Vector3.up * (obstacleSize.y * 0.5f);
            go.transform.localScale = obstacleSize;
            go.AddComponent<PlacedObstacle>();
            manager.MarkDirtyWorldBounds(go.GetComponent<Renderer>().bounds);
        }

        void RemoveObstacle(Vector3 worldCentre) {
            Ray ray = pickCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit h, 5000f)) return;
            var placed = h.collider.GetComponentInParent<PlacedObstacle>();
            if (placed == null) return;
            Bounds b = placed.GetComponent<Renderer>() != null ? placed.GetComponent<Renderer>().bounds
                                                               : new Bounds(placed.transform.position, obstacleSize);
            Destroy(placed.gameObject);
            manager.MarkDirtyWorldBounds(b);
        }

        // Restore the terrain, water and remove every placed obstacle, then let the manager re-solve from scratch.
        void ResetWorld() {
            if (_terrain != null && _originalHeights != null) _terrain.terrainData.SetHeights(0, 0, _originalHeights);
            foreach (var o in FindObjectsByType<PlacedObstacle>(FindObjectsSortMode.None)) DestroyImmediate(o.gameObject);
            if (manager != null) manager.SetWaterLevel(_initialWater);   // also forces a full cost rebuild (picks up removed obstacles)
        }

        // ---------------------------------------------------------------- UI

        void EnsureStyles() {
            _uiScale = Mathf.Clamp(Screen.height / 900f, 0.85f, 2f);
            if (_panelTex == null) {
                _panelTex = MakeTex(new Color(0.10f, 0.12f, 0.16f, 0.86f));
                _accentTex = MakeTex(Color.white);
            }
            if (_title != null) return;
            _title = new GUIStyle { richText = true, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = Color.white;
            _subtitle = new GUIStyle { richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            _subtitle.normal.textColor = new Color(0.75f, 0.82f, 0.9f);
            _stat = new GUIStyle { richText = true, alignment = TextAnchor.MiddleLeft };
            _stat.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
            _hint = new GUIStyle { richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            _hint.normal.textColor = new Color(0.6f, 0.66f, 0.74f);
            _sliderLabel = new GUIStyle { richText = true, alignment = TextAnchor.MiddleLeft };
            _sliderLabel.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
            _btn = new GUIStyle(GUI.skin.button) { richText = true, wordWrap = false, alignment = TextAnchor.MiddleCenter };
        }

        static Texture2D MakeTex(Color c) {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        void OnGUI() {
            if (manager == null) return;
            EnsureStyles();
            float s = _uiScale;
            float pad = 14f * s;
            float w = 300f * s;
            float h = 410f * s;
            _panelRect = new Rect(12f * s, 12f * s, w, h);

            // Panel background + a thin accent strip on the left in the active brush colour.
            GUI.DrawTexture(_panelRect, _panelTex);
            GUI.color = BrushColors[(int)brush];
            GUI.DrawTexture(new Rect(_panelRect.x, _panelRect.y, 4f * s, _panelRect.height), _accentTex);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(_panelRect.x + pad, _panelRect.y + pad, w - 2f * pad, h - 2f * pad));

            _title.fontSize = Mathf.RoundToInt(22f * s);
            GUILayout.Label("Crowd Flow", _title);
            _subtitle.fontSize = Mathf.RoundToInt(12f * s);
            GUILayout.Label("Sculpt the land — the crowd re-routes live.", _subtitle);
            GUILayout.Space(8f * s);

            // Brush buttons, 2×2, colour-coded, active one highlighted.
            for (int row = 0; row < 2; row++) {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++) {
                    int idx = row * 2 + col;
                    DrawBrushButton((Brush)idx, s);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f * s);

            // Brush size slider (scroll also works).
            _sliderLabel.fontSize = Mathf.RoundToInt(12f * s);
            GUILayout.Label($"Brush size  <b>{brushRadius:0}</b>", _sliderLabel);
            brushRadius = GUILayout.HorizontalSlider(brushRadius, 1f, 60f, GUILayout.Height(18f * s));
            GUILayout.Space(6f * s);

            // Reset.
            GUI.backgroundColor = new Color(0.9f, 0.9f, 0.95f, 0.9f);
            _btn.fontSize = Mathf.RoundToInt(13f * s);
            if (GUILayout.Button("↺  Reset world  (G)", _btn, GUILayout.Height(30f * s))) ResetWorld();
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8f * s);

            // Live stats.
            int visitors = _director != null ? _director.SpawnedCount : 0;
            _stat.fontSize = Mathf.RoundToInt(13f * s);
            GUILayout.Label($"<b>{visitors}</b> visitors   ·   water <b>{manager.waterLevel:0.#}</b>   ·   re-solve <b>{manager.LastSolveMs:0.0}</b> ms", _stat);
            _hint.fontSize = Mathf.RoundToInt(11f * s);
            GUILayout.Label("<b>left-drag</b> paint · <b>right-drag</b> lower/remove · <b>[ ]</b> brush size\n" +
                            "Camera: <b>WASD</b> pan · <b>scroll</b> zoom · <b>Q/E</b> rotate · <b>R/F</b> tilt", _hint);

            DrawFlowMapSelector(s);

            GUILayout.EndArea();

            // Track whether the pointer is over the panel (so Update won't sculpt through it). Event.mousePosition is
            // already in GUI space (top-left origin), which matches _panelRect.
            if (Event.current != null && Event.current.type == EventType.Repaint)
                _pointerOverUI = _panelRect.Contains(Event.current.mousePosition);
        }

        // Compact selector for which destination's flow field is shown as draped arrows (Off / per-attraction, colour-coded).
        void DrawFlowMapSelector(float s) {
            if (_arrows == null || _arrows.Count == 0) return;
            GUILayout.Space(8f * s);
            _sliderLabel.fontSize = Mathf.RoundToInt(12f * s);
            GUILayout.Label("Flow map — arrows over terrain  <size=10>(V)</size>", _sliderLabel);
            GUILayout.BeginHorizontal();
            bool off = _arrows.Visible < 0;
            GUI.backgroundColor = off ? new Color(0.9f, 0.9f, 0.95f, 0.95f) : new Color(0.9f, 0.9f, 0.95f, 0.4f);
            _btn.fontSize = Mathf.RoundToInt(12f * s);
            if (GUILayout.Button(off ? "✓ Off" : "Off", _btn, GUILayout.Height(26f * s))) _arrows.SetVisible(-1);
            for (int i = 0; i < _arrows.Count; i++) {
                var a = manager.GetAttraction(i);
                Color c = a != null ? a.color : Color.gray;
                bool active = _arrows.Visible == i;
                GUI.backgroundColor = active ? c : new Color(c.r, c.g, c.b, 0.4f);
                if (GUILayout.Button((active ? "✓ " : "") + (i + 1), _btn, GUILayout.Height(26f * s))) _arrows.SetVisible(i);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }

        void DrawBrushButton(Brush b, float s) {
            bool active = brush == b;
            Color c = BrushColors[(int)b];
            GUI.backgroundColor = active ? c : new Color(c.r, c.g, c.b, 0.42f);
            _btn.fontSize = Mathf.RoundToInt(12f * s);
            string check = active ? "✓ " : "";
            string label = $"<b>{check}{BrushNames[(int)b]}</b>\n<size={Mathf.RoundToInt(10f * s)}>key {(int)b + 1}</size>";
            if (GUILayout.Button(label, _btn, GUILayout.Height(44f * s))) brush = b;
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>Marks a runtime-placed obstacle so the editor can find and remove it.</summary>
    public class PlacedObstacle : MonoBehaviour { }
}
