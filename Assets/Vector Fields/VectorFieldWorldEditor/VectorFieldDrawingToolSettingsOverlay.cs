using System.Collections;
using System.Collections.Generic;
using UnityEngine;


using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "Vector Field Editor Settings")]
// [Icon("Assets/Icons/VectorFieldIcon.png")] // Set your icon path here
public class VectorFieldDrawingToolSettingsOverlay : Overlay, ITransientOverlay
{
    private VectorFieldDrawingTool _tool;

    public VectorFieldDrawingToolSettingsOverlay()
    {
        // _tool = tool;
    }
    
    public void Init (VectorFieldDrawingTool tool) {
        _tool = tool;
        
    }

    public bool visible => ToolManager.IsActiveTool(_tool);

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        
        Label title = new Label("Vector Field Drawing Tool Settings");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.Add(title);
    
        // Brush Size Slider
        Slider brushSizeSlider = new Slider("Brush Size", 0.1f, 10.0f);
        brushSizeSlider.value = _tool.gridSpaceBrushSize;
        brushSizeSlider.RegisterValueChangedCallback(evt =>
        {
            _tool.gridSpaceBrushSize = evt.newValue;
            _tool.OnBrushSettingsChange();
        });
        root.Add(brushSizeSlider);
        
        // Brush Strength Slider
        Slider brushStrengthSlider = new Slider("Pressure", 0.1f, 1.0f);
        brushStrengthSlider.value = _tool.pressure;
        brushStrengthSlider.RegisterValueChangedCallback(evt =>
        {
            _tool.pressure = evt.newValue;
            _tool.OnBrushSettingsChange();
        });
        root.Add(brushStrengthSlider);
        
        // PropertyField gridSizeField = new PropertyField(gridSizeProp, "Grid Size");
        // root.Add(gridSizeField);
        
        // Create an ObjectField for a RenderTexture
        var objectField = new ObjectField("Cookie Texture") {
            objectType = typeof(Texture2D), // Specify the type of object that can be dragged into the field
            value = _tool.brushCreator.RenderTexture // Set the initial value (if any)
        };

        // Set up a callback for when the user assigns a new texture
        objectField.RegisterValueChangedCallback(evt =>
        {
            // Update the render texture when the user drags in a new one
            _tool.brushCreator.cookieTexture = evt.newValue as Texture2D;
    
            // Optionally update the Image to display the new texture
            // renderTextureImage.image = _tool.brushCreator.RenderTexture;
        });

// Add the ObjectField to the UI
        root.Add(objectField);

// Continue with your Image display
        var renderTextureImage = new Image();
        renderTextureImage.image = _tool.brushCreator.RenderTexture;
        renderTextureImage.style.width = 64;
        renderTextureImage.style.height = 64;

        root.Add(renderTextureImage);
        return root;
    }
}