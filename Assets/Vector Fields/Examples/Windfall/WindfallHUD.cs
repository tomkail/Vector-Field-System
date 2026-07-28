using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Windfall {
    /// <summary>
    /// Runtime-built screen-space HUD for the multiplayer round (GAME_DESIGN.md §7a). Built entirely in code
    /// (no scene wiring) and created by <see cref="WindfallGame"/> on start:
    ///  - a top bar listing every player in rank order, each with a colour swatch, live rank (#1…) and current score;
    ///  - floating "+N" score popups that appear above a player's head on collecting points, rising and fading.
    /// Uses legacy uGUI with a ConstantPixelSize canvas so world→screen popup placement is a straight pixel mapping.
    /// </summary>
    public class WindfallHUD : MonoBehaviour {
        WindfallGame _game;
        Font _font;
        Sprite _solid;
        Canvas _canvas;
        RectTransform _canvasRT;
        RectTransform _bar;

        // Round-flow presentation (driven by WindfallGame).
        Image _overlay;                 // full-screen black fade
        Text _banner;                   // centred round-name / message
        Image _timerBg, _timerFill;     // round-timer bar under the top bar
        Text _goalArrow;                // off-screen goal indicator at the screen edge
        bool _goalActive;
        Vector3 _goalWorld;
        RectTransform _resultsPanel;    // centred end-of-round standings
        Text _resultsTitle;
        readonly List<Text> _resultRows = new List<Text>();
        bool _barVisible = true;
        bool _resultsVisible;
        string _resultsTitleText = "";

        class Row { public Image swatch; public Text label; public Text sub; }
        readonly List<Row> _rows = new List<Row>();

        class Popup { public Text text; public Transform track; public Vector3 lastWorld; public float age; }
        readonly List<Popup> _popups = new List<Popup>();

        const float BarHeight = 52f;   // two lines: name + score, then the button beneath
        const float CellWidth = 210f;
        const float StartX = 16f;
        const float PopupLife = 1.1f;
        const float PopupRisePixels = 55f;
        const float HeadOffset = 0.9f;   // world units above the tracked transform
        const float ResultRowHeight = 40f;

        public void Init(WindfallGame game) {
            _game = game;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans", "Segoe UI", "sans-serif" }, 18);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tex = Texture2D.whiteTexture;
            _solid = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

            BuildCanvas();
            BuildBar();
            BuildTimer();
            BuildResults();
            BuildBanner();
            BuildOverlay();   // last child → drawn on top of everything
            BuildGoalArrow();
            _game.OnPointsGained += HandlePoints;
        }

        void OnDestroy() {
            if (_game != null) _game.OnPointsGained -= HandlePoints;
        }

        void BuildCanvas() {
            // Build the canvas GameObject with a RectTransform up front (can't swap Transform→RectTransform later).
            var canvasGO = new GameObject("HUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1 canvas unit == 1 screen pixel
            _canvasRT = (RectTransform)canvasGO.transform;
        }

        void BuildBar() {
            _bar = NewRect("Bar", _canvasRT);
            _bar.anchorMin = new Vector2(0f, 1f);
            _bar.anchorMax = new Vector2(1f, 1f);
            _bar.pivot = new Vector2(0f, 1f);          // local origin = top-left corner of the screen
            _bar.sizeDelta = new Vector2(0f, BarHeight);
            _bar.anchoredPosition = Vector2.zero;

            var bg = NewImage("BarBG", _bar);
            bg.color = new Color(0f, 0f, 0f, 0.5f);
            var bgRT = bg.rectTransform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;   // fill the bar
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            var hint = NewText("Hint", _bar);
            hint.text = "Backspace: reset";
            hint.fontSize = 14;
            hint.color = new Color(1f, 1f, 1f, 0.5f);
            hint.alignment = TextAnchor.MiddleRight;
            var hRT = hint.rectTransform;
            hRT.anchorMin = new Vector2(1f, 1f); hRT.anchorMax = new Vector2(1f, 1f); hRT.pivot = new Vector2(1f, 1f);
            hRT.sizeDelta = new Vector2(160f, BarHeight);
            hRT.anchoredPosition = new Vector2(-12f, 0f);
        }

        void BuildTimer() {
            _timerBg = NewImage("TimerBG", _canvasRT);
            var bg = _timerBg.rectTransform;
            bg.anchorMin = new Vector2(0f, 1f); bg.anchorMax = new Vector2(1f, 1f); bg.pivot = new Vector2(0.5f, 1f);
            bg.sizeDelta = new Vector2(-24f, 7f);                      // full width minus a 12px margin each side
            bg.anchoredPosition = new Vector2(0f, -(BarHeight + 4f));  // just under the player bar
            _timerBg.color = new Color(0f, 0f, 0f, 0.45f);

            _timerFill = NewImage("TimerFill", _timerBg.rectTransform);
            var f = _timerFill.rectTransform;
            f.anchorMin = new Vector2(0f, 0f); f.anchorMax = new Vector2(0f, 1f); f.pivot = new Vector2(0f, 0.5f);
            f.sizeDelta = Vector2.zero; f.anchoredPosition = Vector2.zero;

            _timerBg.gameObject.SetActive(false);
        }

        public void SetTimer(bool visible, float frac01) {
            if (_timerBg == null) return;
            if (_timerBg.gameObject.activeSelf != visible) _timerBg.gameObject.SetActive(visible);
            if (!visible) return;
            frac01 = Mathf.Clamp01(frac01);
            float w = _timerBg.rectTransform.rect.width; if (w < 1f) w = Screen.width - 24f;
            _timerFill.rectTransform.sizeDelta = new Vector2(frac01 * w, 0f);
            // green when there's plenty of time, through amber, to red as it runs out
            _timerFill.color = Color.Lerp(new Color(1f, 0.3f, 0.2f, 0.9f), new Color(0.4f, 0.9f, 0.5f, 0.9f), frac01);
        }

        void BuildGoalArrow() {
            _goalArrow = NewText("GoalArrow", _canvasRT);
            _goalArrow.text = "▶";                     // ▶ points +x at 0° rotation
            _goalArrow.fontSize = 44;
            _goalArrow.fontStyle = FontStyle.Bold;
            _goalArrow.alignment = TextAnchor.MiddleCenter;
            _goalArrow.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            var rt = _goalArrow.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f, 64f);
            _goalArrow.gameObject.SetActive(false);
        }

        public void SetGoalArrow(bool active, Vector3 worldPos) {
            _goalActive = active;
            _goalWorld = worldPos;
            if (!active && _goalArrow != null && _goalArrow.gameObject.activeSelf) _goalArrow.gameObject.SetActive(false);
        }

        void UpdateGoalArrow() {
            if (_goalArrow == null) return;
            var cam = Camera.main;
            if (!_goalActive || cam == null) { if (_goalArrow.gameObject.activeSelf) _goalArrow.gameObject.SetActive(false); return; }

            Vector3 sp = cam.WorldToScreenPoint(_goalWorld);
            const float margin = 44f;
            var view = new Rect(margin, margin, Screen.width - 2f * margin, Screen.height - 2f * margin);
            bool onScreen = sp.z > 0f && view.Contains(new Vector2(sp.x, sp.y));
            if (onScreen) { if (_goalArrow.gameObject.activeSelf) _goalArrow.gameObject.SetActive(false); return; }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = new Vector2(sp.x, sp.y) - center;
            if (sp.z < 0f) dir = -dir;                       // target behind camera (safety; N/A for top-down ortho)
            if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
            dir.Normalize();

            if (!_goalArrow.gameObject.activeSelf) _goalArrow.gameObject.SetActive(true);
            _goalArrow.rectTransform.anchoredPosition = ClampRayToRect(center, dir, view);
            _goalArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        // First boundary of `rect` hit by the ray from `origin` along `dir`.
        static Vector2 ClampRayToRect(Vector2 origin, Vector2 dir, Rect rect) {
            float best = float.MaxValue;
            if (Mathf.Abs(dir.x) > 1e-4f) {
                TryEdge(ref best, (rect.xMin - origin.x) / dir.x, origin, dir, rect, true);
                TryEdge(ref best, (rect.xMax - origin.x) / dir.x, origin, dir, rect, true);
            }
            if (Mathf.Abs(dir.y) > 1e-4f) {
                TryEdge(ref best, (rect.yMin - origin.y) / dir.y, origin, dir, rect, false);
                TryEdge(ref best, (rect.yMax - origin.y) / dir.y, origin, dir, rect, false);
            }
            if (best == float.MaxValue) best = 0f;
            return origin + dir * best;
        }

        static void TryEdge(ref float best, float t, Vector2 origin, Vector2 dir, Rect rect, bool verticalEdge) {
            if (t <= 0f || t >= best) return;
            Vector2 p = origin + dir * t;
            bool ok = verticalEdge ? (p.y >= rect.yMin && p.y <= rect.yMax) : (p.x >= rect.xMin && p.x <= rect.xMax);
            if (ok) best = t;
        }

        void BuildOverlay() {
            _overlay = NewImage("Overlay", _canvasRT);
            var rt = _overlay.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;   // full screen
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _overlay.color = new Color(0f, 0f, 0f, 1f);               // starts black; the game fades it in
        }

        void BuildBanner() {
            _banner = NewText("Banner", _canvasRT);
            _banner.fontSize = 64;
            _banner.fontStyle = FontStyle.Bold;
            _banner.alignment = TextAnchor.MiddleCenter;
            var rt = _banner.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 140f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            _banner.color = new Color(1f, 1f, 1f, 0f);
        }

        void BuildResults() {
            _resultsPanel = NewRect("Results", _canvasRT);
            _resultsPanel.anchorMin = _resultsPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _resultsPanel.pivot = new Vector2(0.5f, 0.5f);
            _resultsPanel.sizeDelta = new Vector2(440f, 380f);
            _resultsPanel.anchoredPosition = Vector2.zero;

            var bg = NewImage("ResBG", _resultsPanel);
            bg.color = new Color(0f, 0f, 0f, 0.6f);
            var bgRT = bg.rectTransform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            _resultsTitle = NewText("ResTitle", _resultsPanel);
            _resultsTitle.fontSize = 32;
            _resultsTitle.fontStyle = FontStyle.Bold;
            _resultsTitle.alignment = TextAnchor.MiddleCenter;
            _resultsTitle.color = Color.white;
            var trt = _resultsTitle.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 56f);
            trt.anchoredPosition = new Vector2(0f, -14f);

            _resultsPanel.gameObject.SetActive(false);
        }

        // ---- round-flow setters (called by WindfallGame) ----
        public void SetFade(float alpha01) {
            if (_overlay == null) return;
            var c = _overlay.color; c.a = Mathf.Clamp01(alpha01); _overlay.color = c;
        }

        public void SetBanner(string text, float alpha01) {
            if (_banner == null) return;
            _banner.text = text ?? "";
            var c = _banner.color; c.a = Mathf.Clamp01(alpha01); _banner.color = c;
        }

        public void SetBarVisible(bool visible) {
            _barVisible = visible;
            if (_bar != null) _bar.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Height in screen pixels of the top HUD strip (player bar plus the timer bar when shown), so the
        /// game camera can keep players framed below it rather than behind it. Returns 0 when the bar is
        /// hidden. The canvas is ConstantPixelSize, so canvas units == screen pixels.
        /// </summary>
        public float TopInsetPixels() {
            if (_bar == null || !_bar.gameObject.activeSelf) return 0f;
            float inset = BarHeight;
            if (_timerBg != null && _timerBg.gameObject.activeSelf)
                inset = BarHeight + 4f + _timerBg.rectTransform.rect.height;   // timer sits just under the bar
            return inset + 8f;   // a little breathing room below the strip
        }

        public void SetResults(bool visible, string title) {
            _resultsVisible = visible;
            _resultsTitleText = title ?? "";
            if (_resultsPanel != null) _resultsPanel.gameObject.SetActive(visible);
        }

        void UpdateResults() {
            if (!_resultsVisible || _game == null) return;
            _resultsTitle.text = _resultsTitleText;
            int n = _game.PlayerCount;
            EnsureResultRows(n);

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => _game.GetPlayer(b).score.CompareTo(_game.GetPlayer(a).score));

            float y = -70f;   // below the title
            for (int d = 0; d < n; d++) {
                var info = _game.GetPlayer(order[d]);
                var row = _resultRows[d];
                row.color = info.color;
                row.text = $"{d + 1}.   {info.name}      {info.score}";
                row.rectTransform.anchoredPosition = new Vector2(0f, y);
                y -= ResultRowHeight;
            }
        }

        void EnsureResultRows(int n) {
            if (_resultRows.Count == n) return;
            foreach (var t in _resultRows) if (t != null) Destroy(t.gameObject);
            _resultRows.Clear();
            for (int i = 0; i < n; i++) {
                var t = NewText("ResRow" + i, _resultsPanel);
                t.fontSize = 26;
                t.fontStyle = FontStyle.Bold;
                t.alignment = TextAnchor.MiddleCenter;
                var rt = t.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-40f, ResultRowHeight);
                _resultRows.Add(t);
            }
        }

        void EnsureRows(int n) {
            if (_rows.Count == n) return;
            foreach (var r in _rows) {
                if (r.swatch != null) Destroy(r.swatch.gameObject);
                if (r.label != null) Destroy(r.label.gameObject);
                if (r.sub != null) Destroy(r.sub.gameObject);
            }
            _rows.Clear();
            for (int i = 0; i < n; i++) {
                var sw = NewImage("Swatch" + i, _bar);
                sw.rectTransform.anchorMin = new Vector2(0f, 1f); sw.rectTransform.anchorMax = new Vector2(0f, 1f); sw.rectTransform.pivot = new Vector2(0f, 1f);
                sw.rectTransform.sizeDelta = new Vector2(18f, 18f);

                var lab = NewText("Label" + i, _bar);
                lab.rectTransform.anchorMin = new Vector2(0f, 1f); lab.rectTransform.anchorMax = new Vector2(0f, 1f); lab.rectTransform.pivot = new Vector2(0f, 1f);
                lab.rectTransform.sizeDelta = new Vector2(CellWidth - 28f, 24f);
                lab.alignment = TextAnchor.MiddleLeft;
                lab.fontStyle = FontStyle.Bold;

                var sub = NewText("Button" + i, _bar);   // the player's input button, shown under the name
                sub.rectTransform.anchorMin = new Vector2(0f, 1f); sub.rectTransform.anchorMax = new Vector2(0f, 1f); sub.rectTransform.pivot = new Vector2(0f, 1f);
                sub.rectTransform.sizeDelta = new Vector2(CellWidth - 28f, 18f);
                sub.alignment = TextAnchor.MiddleLeft;
                sub.fontSize = 13;

                _rows.Add(new Row { swatch = sw, label = lab, sub = sub });
            }
        }

        void Update() {
            if (_game == null) return;
            if (_barVisible) UpdateBar();
            UpdateResults();
            UpdatePopups();
            UpdateGoalArrow();
        }

        void UpdateBar() {
            int n = _game.PlayerCount;
            EnsureRows(n);
            if (n == 0) return;

            // Distribute cells across the actual bar width so the bar scales with the screen (the canvas is
            // ConstantPixelSize, so bar width == screen width). Reserve room on the right for the reset hint.
            float barW = _bar.rect.width; if (barW < 1f) barW = Screen.width;
            const float HintReserve = 150f;
            float cell = Mathf.Clamp((barW - StartX - HintReserve) / n, 78f, CellWidth);
            int labelFont = Mathf.RoundToInt(Mathf.Clamp(cell * 0.095f, 12f, 18f));
            int subFont = Mathf.RoundToInt(labelFont * 0.72f);

            // Rank by score (desc), with competition ranking for ties (1,2,2,4).
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => _game.GetPlayer(b).score.CompareTo(_game.GetPlayer(a).score));
            var rank = new int[n];
            for (int d = 0; d < n; d++) {
                int p = order[d];
                if (d > 0 && _game.GetPlayer(order[d - 1]).score == _game.GetPlayer(p).score) rank[p] = rank[order[d - 1]];
                else rank[p] = d + 1;
            }

            float x = StartX;
            for (int d = 0; d < n; d++) {
                int p = order[d];
                var info = _game.GetPlayer(p);
                var row = _rows[p];
                float a = info.finished ? 0.55f : 1f;
                row.swatch.color = new Color(info.color.r, info.color.g, info.color.b, a);
                row.label.color = new Color(info.color.r, info.color.g, info.color.b, a);
                row.sub.color = new Color(info.color.r, info.color.g, info.color.b, a * 0.7f);
                string tag = info.finished ? (info.scoredZone ? " *" : " -") : "";
                row.label.text = $"#{rank[p]}  {info.name}   {info.score}{tag}";
                row.label.fontSize = labelFont;
                row.label.rectTransform.sizeDelta = new Vector2(cell - 26f, 24f);
                row.sub.text = info.button;
                row.sub.fontSize = subFont;
                row.sub.rectTransform.sizeDelta = new Vector2(cell - 26f, 18f);
                row.swatch.rectTransform.anchoredPosition = new Vector2(x, -5f);
                row.label.rectTransform.anchoredPosition = new Vector2(x + 26f, -2f);
                row.sub.rectTransform.anchoredPosition = new Vector2(x + 26f, -28f);
                x += cell;
            }
        }

        void HandlePoints(Transform track, int amount, Color color) {
            var t = NewText("Popup", _canvasRT);
            t.text = (amount >= 0 ? "+" : "") + amount;
            t.fontSize = 22;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(140f, 34f);
            _popups.Add(new Popup { text = t, track = track, lastWorld = track != null ? track.position : Vector3.zero, age = 0f });
        }

        void UpdatePopups() {
            var cam = Camera.main;
            for (int i = _popups.Count - 1; i >= 0; i--) {
                var pu = _popups[i];
                pu.age += Time.deltaTime;
                if (pu.age >= PopupLife || pu.text == null) {
                    if (pu.text != null) Destroy(pu.text.gameObject);
                    _popups.RemoveAt(i);
                    continue;
                }
                Vector3 world = pu.track != null ? pu.track.position : pu.lastWorld;
                if (pu.track != null) pu.lastWorld = world;
                world += Vector3.up * HeadOffset;
                float k = pu.age / PopupLife;
                if (cam != null) {
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    if (sp.z > 0f) pu.text.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y + k * PopupRisePixels);
                }
                var c = pu.text.color; c.a = 1f - k; pu.text.color = c;
            }
        }

        // --- tiny uGUI builders ---
        RectTransform NewRect(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        Image NewImage(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ((RectTransform)go.transform).SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _solid;
            img.type = Image.Type.Simple;
            return img;
        }

        Text NewText(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            ((RectTransform)go.transform).SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = 18;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
