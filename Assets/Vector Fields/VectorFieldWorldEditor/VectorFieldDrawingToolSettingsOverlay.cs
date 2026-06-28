using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "Vector Field Editor Settings")]
public class VectorFieldDrawingToolSettingsOverlay : Overlay, ITransientOverlay {
    VectorFieldDrawingTool _tool;

    // Re-synced when the tool changes a setting outside the overlay (e.g. Cmd+Scroll resizing the brush).
    Slider _brushSizeSlider;
    Slider _pressureSlider;
    Image _preview;

    VectorFieldDrawingToolSettings settings => VectorFieldDrawingToolSettings.Instance;

    public void Init(VectorFieldDrawingTool tool) {
        _tool = tool;
    }

    public bool visible => ToolManager.IsActiveTool(_tool);

    public override VisualElement CreatePanelContent() {
        var root = new VisualElement { style = { minWidth = 220 } };

        // Bind to the settings singleton so the cookie PropertyField reads/writes it directly.
        var so = new SerializedObject(settings);
        root.Bind(so);

        // --- Brush -------------------------------------------------------------------------------------------------
        root.Add(Header("Brush"));

        _brushSizeSlider = new Slider("Size", 0.1f, 30f) { value = _tool.gridSpaceBrushSize };
        _brushSizeSlider.RegisterValueChangedCallback(evt => {
            _tool.gridSpaceBrushSize = evt.newValue;
            _tool.OnBrushSettingsChange();
            RefreshPreview();
        });
        root.Add(_brushSizeSlider);

        _pressureSlider = new Slider("Pressure", 0f, 1f) { value = _tool.pressure };
        _pressureSlider.RegisterValueChangedCallback(evt => {
            _tool.pressure = evt.newValue;
            VectorFieldDrawingToolSettings.Save();
        });
        root.Add(_pressureSlider);

        // --- Shape (cookie) ----------------------------------------------------------------------------------------
        root.Add(Header("Shape"));

        var cookieField = new PropertyField(so.FindProperty("brushCookie"), "Cookie");
        // Rebuild + persist the brush whenever any cookie sub-field changes (uses the existing IMGUI drawer).
        cookieField.TrackPropertyValue(so.FindProperty("brushCookie"), _ => {
            so.ApplyModifiedProperties();
            _tool.OnBrushSettingsChange();
            VectorFieldDrawingToolSettings.Save();
            RefreshPreview();
        });
        root.Add(cookieField);

        // Live preview of the generated brush.
        _preview = new Image {
            scaleMode = ScaleMode.ScaleToFit,
            style = { width = 64, height = 64, marginTop = 4, alignSelf = Align.Center }
        };
        RefreshPreview();
        root.Add(_preview);

        // --- Help --------------------------------------------------------------------------------------------------
        root.Add(Header("Shortcuts"));
        var help = new Label("Drag: draw\nCtrl+Drag: add\nCmd+Drag: erase\nShift+Click: stamp\nCmd+Scroll: size") {
            style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, fontSize = 11 }
        };
        root.Add(help);

        return root;
    }

    // Push tool-side changes (e.g. Cmd+Scroll brush resize) back into the slider values.
    public void SyncFromTool() {
        if (_tool == null) return;
        _brushSizeSlider?.SetValueWithoutNotify(_tool.gridSpaceBrushSize);
        _pressureSlider?.SetValueWithoutNotify(_tool.pressure);
    }

    void RefreshPreview() {
        if (_preview != null && _tool?.brushCreator != null)
            _preview.image = _tool.brushCreator.RenderTexture;
    }

    static Label Header(string text) => new Label(text) {
        style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 2 }
    };
}
