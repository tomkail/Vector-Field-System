using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "vector-field-brush", "Vector Field Brush")]
public class VectorFieldDrawingToolSettingsOverlay : Overlay, ITransientOverlay {
    VectorFieldDrawingTool _tool;

    // Give the collapsed sidebar tab a real icon instead of the "VF" text fallback. Falls back silently if the
    // built-in icon name ever changes.
    public override void OnCreated() {
        var icon = EditorGUIUtility.IconContent("Grid.PaintTool")?.image as Texture2D;
        if (icon != null) collapsedIcon = icon;
    }

    // Re-synced when the tool changes a setting outside the overlay (e.g. Action+Scroll resizing the brush, M cycling
    // the mode).
    Slider _brushSizeSlider;
    Slider _pressureSlider;
    Image _preview;
    readonly Dictionary<string, Button> _opButtons = new();

    // Cookie controls, shown/hidden by mode.
    VisualElement _cookieSoftness;
    VisualElement _cookieCurve;
    VisualElement _cookieTexture;

    // Emitter (direction) controls. The whole section hides for ops that don't paint the emitter direction; within it,
    // the angle vs swirl control depends on the emitter type.
    VisualElement _emitterSection;
    VisualElement _emitterDirection;
    VisualElement _emitterSwirl;

    VectorFieldDrawingToolSettings settings => VectorFieldDrawingToolSettings.Instance;

    public void Init(VectorFieldDrawingTool tool) {
        _tool = tool;
    }

    public bool visible => ToolManager.IsActiveTool(_tool);

    public override VisualElement CreatePanelContent() {
        var root = new VisualElement { style = { minWidth = 232 } };

        // Bind to the settings singleton so the cookie controls read/write it directly.
        var so = new SerializedObject(settings);
        root.Bind(so);

        BuildModeSection(root);
        BuildBrushSection(root);
        BuildEmitterSection(so, root);
        BuildShapeSection(so, root);
        BuildHelpSection(root);

        return root;
    }

    // --- Mode: grouped accent buttons instead of a long radio list --------------------------------------------------
    void BuildModeSection(VisualElement root) {
        root.Add(Header("Mode"));
        _opButtons.Clear();

        foreach (var group in VectorFieldBrushOpRegistry.Groups) {
            root.Add(GroupLabel(group.name));

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 4 } };
            foreach (var op in group.ops) {
                var captured = op;
                var btn = new Button(() => SelectOp(captured)) { text = op.DisplayName, tooltip = op.Tooltip };
                btn.style.flexGrow = 1;
                btn.style.minWidth = 48;
                btn.style.marginRight = 3;
                btn.style.marginBottom = 3;
                btn.style.marginLeft = 0;
                btn.style.marginTop = 0;
                btn.style.paddingTop = 3;
                btn.style.paddingBottom = 3;
                btn.style.fontSize = 11;
                btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius = 3;
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 3;
                _opButtons[op.Id] = btn;
                row.Add(btn);
            }
            root.Add(row);
        }
        HighlightActiveOp();
    }

    void SelectOp(IVectorFieldBrushOp op) {
        _tool.brushOp = op;
        HighlightActiveOp();
        UpdateEmitterVisibility();
    }

    void HighlightActiveOp() {
        string activeId = _tool != null ? _tool.brushOp.Id : null;
        foreach (var op in VectorFieldBrushOpRegistry.Ops) {
            if (!_opButtons.TryGetValue(op.Id, out var btn)) continue;
            bool active = op.Id == activeId;
            var accent = op.GizmoColor;
            // A left accent bar in the op's gizmo colour ties the button to the scene-view cursor.
            btn.style.borderLeftWidth = 3;
            btn.style.borderLeftColor = active ? accent : new Color(accent.r, accent.g, accent.b, 0.5f);
            btn.style.backgroundColor = active ? new Color(accent.r, accent.g, accent.b, 0.30f)
                                               : new Color(1f, 1f, 1f, 0.06f);
            btn.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            btn.style.color = new Color(1f, 1f, 1f, active ? 1f : 0.8f);
        }
    }

    // --- Brush: size + pressure -------------------------------------------------------------------------------------
    void BuildBrushSection(VisualElement root) {
        root.Add(Header("Brush"));

        _brushSizeSlider = new Slider("Size", 0.1f, 30f) { value = _tool.gridSpaceBrushSize };
        _brushSizeSlider.RegisterValueChangedCallback(evt => {
            _tool.gridSpaceBrushSize = evt.newValue;
            VectorFieldDrawingToolSettings.Save();
        });
        root.Add(_brushSizeSlider);

        _pressureSlider = new Slider("Pressure", 0f, 1f) { value = _tool.pressure };
        _pressureSlider.RegisterValueChangedCallback(evt => {
            _tool.pressure = evt.newValue;
            VectorFieldDrawingToolSettings.Save();
        });
        root.Add(_pressureSlider);
    }

    // --- Direction (emitter): only shown for ops that paint the emitter's direction ---------------------------------
    void BuildEmitterSection(SerializedObject so, VisualElement root) {
        var section = new VisualElement();
        section.Add(Header("Direction"));

        var flow = new EnumField("Flow", VectorFieldDirectionMode.FollowStroke);
        flow.tooltip = "Follow stroke: flow points along the drag. Fixed angle: flow uses the emitter direction.";
        flow.BindProperty(so.FindProperty("directionMode"));
        flow.RegisterValueChangedCallback(_ => {
            so.ApplyModifiedProperties();
            VectorFieldDrawingToolSettings.Save();
            UpdateDirectionLabel();
            SceneView.RepaintAll();   // the gizmo arrow shows only in Fixed angle
        });
        section.Add(flow);

        var brushSettings = so.FindProperty("brushSettings");

        var emitter = new EnumField("Emitter", VectorFieldBrushSettings.ForceEmitterType.Directional);
        emitter.tooltip = "Directional: a uniform direction. Spot: emanates from the brush centre, swirled by Swirl.";
        emitter.BindProperty(brushSettings.FindPropertyRelative("forceType"));
        emitter.RegisterValueChangedCallback(_ => RebuildBrush(so));
        section.Add(emitter);

        var direction = new Slider("Angle", 0f, 360f) { tooltip = "Direction the brush paints, in degrees (0 = up)." };
        direction.BindProperty(brushSettings.FindPropertyRelative("directionalAngle"));
        direction.RegisterValueChangedCallback(_ => RebuildBrush(so));
        _emitterDirection = direction;
        section.Add(direction);

        var swirl = new Slider("Swirl", 0f, 360f) { tooltip = "Rotation of the spot emitter (0 = outward, 90 = tangential)." };
        swirl.BindProperty(brushSettings.FindPropertyRelative("vortexAngle"));
        swirl.RegisterValueChangedCallback(_ => RebuildBrush(so));
        _emitterSwirl = swirl;
        section.Add(swirl);

        _emitterSection = section;
        root.Add(section);
        UpdateEmitterVisibility();
        UpdateDirectionLabel();
    }

    // In Follow-stroke mode the emitter angle rotates the flow relative to the stroke, so the slider reads differently.
    void UpdateDirectionLabel() {
        if (_emitterDirection is Slider slider) {
            bool follow = settings.directionMode == VectorFieldDirectionMode.FollowStroke;
            slider.label = follow ? "Stroke Rotation" : "Angle";
            slider.tooltip = follow
                ? "Rotation of the painted flow relative to the stroke direction, in degrees."
                : "Direction the brush paints, in degrees (0 = up).";
        }
    }

    // Hide the whole section for ops that ignore the emitter direction; within it, show angle (Directional) or swirl
    // (Spot).
    void UpdateEmitterVisibility() {
        bool usesDirection = _tool != null && _tool.brushOp.UsesBrushDirection;
        SetDisplayed(_emitterSection, usesDirection);
        if (usesDirection) {
            bool spot = settings.brushSettings.forceType == VectorFieldBrushSettings.ForceEmitterType.Spot;
            SetDisplayed(_emitterDirection, !spot);
            SetDisplayed(_emitterSwirl, spot);
        }
    }

    // --- Shape (cookie): native, always-visible editor that shows only the field the mode uses ----------------------
    void BuildShapeSection(SerializedObject so, VisualElement root) {
        root.Add(Header("Shape"));

        var cookieProp = so.FindProperty("brushCookie");
        var modeProp = cookieProp.FindPropertyRelative("mode");

        var modeField = new EnumField("Cookie", VectorFieldCookieSource.Mode.None);
        modeField.BindProperty(modeProp);
        modeField.RegisterValueChangedCallback(_ => RebuildBrush(so));
        root.Add(modeField);

        var softness = new Slider("Softness", 0f, 1f);
        softness.BindProperty(cookieProp.FindPropertyRelative("falloffSoftness"));
        softness.RegisterValueChangedCallback(_ => RebuildBrush(so));
        _cookieSoftness = softness;
        root.Add(softness);

        var curve = new CurveField("Curve");
        curve.BindProperty(cookieProp.FindPropertyRelative("curve"));
        curve.RegisterValueChangedCallback(_ => RebuildBrush(so));
        _cookieCurve = curve;
        root.Add(curve);

        var texture = new ObjectField("Texture") { objectType = typeof(Texture2D), allowSceneObjects = false };
        texture.BindProperty(cookieProp.FindPropertyRelative("texture"));
        texture.RegisterValueChangedCallback(_ => RebuildBrush(so));
        _cookieTexture = texture;
        root.Add(texture);

        UpdateCookieControls();

        // Live preview of the generated brush.
        _preview = new Image {
            scaleMode = ScaleMode.ScaleToFit,
            style = { width = 64, height = 64, marginTop = 4, alignSelf = Align.Center }
        };
        RefreshPreview();
        root.Add(_preview);
    }

    void RebuildBrush(SerializedObject so) {
        so.ApplyModifiedProperties();
        UpdateCookieControls();
        UpdateEmitterVisibility();
        _tool.OnBrushSettingsChange();
        VectorFieldDrawingToolSettings.Save();
        RefreshPreview();
    }

    // Show only the control the current cookie mode actually uses.
    void UpdateCookieControls() {
        var mode = settings.brushCookie != null ? settings.brushCookie.mode : VectorFieldCookieSource.Mode.None;
        SetDisplayed(_cookieSoftness, mode == VectorFieldCookieSource.Mode.Falloff);
        SetDisplayed(_cookieCurve, mode == VectorFieldCookieSource.Mode.Curve);
        SetDisplayed(_cookieTexture, mode == VectorFieldCookieSource.Mode.Texture);
    }

    void BuildHelpSection(VisualElement root) {
        root.Add(Header("Shortcuts"));
        // "Action" is Cmd on macOS, Ctrl on Windows/Linux.
        var help = new Label("Drag: paint (current mode)\nAction+Drag: erase\nShift+Click: stamp\nAction+Scroll: size\nM: cycle mode") {
            style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, fontSize = 11 }
        };
        root.Add(help);
    }

    // Push tool-side changes (Action+Scroll resize, M cycle) back into the controls.
    public void SyncFromTool() {
        if (_tool == null) return;
        _brushSizeSlider?.SetValueWithoutNotify(_tool.gridSpaceBrushSize);
        _pressureSlider?.SetValueWithoutNotify(_tool.pressure);
        HighlightActiveOp();
        UpdateEmitterVisibility();
        UpdateDirectionLabel();
    }

    void RefreshPreview() {
        // Show the cookie's grayscale shape mask, not the coloured vector brush.
        if (_preview != null && _tool != null)
            _preview.image = _tool.cookiePreview;
    }

    static void SetDisplayed(VisualElement element, bool shown) {
        if (element != null) element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static Label Header(string text) => new Label(text) {
        style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 3 }
    };

    static Label GroupLabel(string text) => new Label(text) {
        style = { fontSize = 10, opacity = 0.55f, marginTop = 2, marginBottom = 1, unityFontStyleAndWeight = FontStyle.Bold }
    };
}
