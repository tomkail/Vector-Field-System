using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldPainterToolManager {
	public VectorFieldEditorWindow editorWindow;

	public enum ToolType {
		Smudge,
		Burn,
		Clamp
	}
	
	public ToolType currentToolType;
	public VectorFieldPainterTool currentTool;
	
	public Dictionary<ToolType, VectorFieldPainterTool> tools = new Dictionary<ToolType, VectorFieldPainterTool>();
	
	public Texture2D[] brushTextures;

	public delegate void OnEditVectorFieldEvent (List<Point> editorPoints);
	public event OnEditVectorFieldEvent OnEditVectorField;

	public VectorFieldPainterToolManager (VectorFieldEditorWindow editorWindow) {
		this.editorWindow = editorWindow;
		tools = new Dictionary<ToolType, VectorFieldPainterTool>() {
			{ToolType.Smudge, new VectorFieldPainterSmudgeTool(this)},
			{ToolType.Burn, new VectorFieldPainterBurnTool(this)},
			{ToolType.Clamp, new VectorFieldPainterClampTool(this)}
		};
		ChangeTool(ToolType.Smudge);
	}

	public void ChangeTool (ToolType toolType) {
		if(currentTool != null)
			currentTool.Exit();
		currentToolType = toolType;
		currentTool = tools[toolType];
		currentTool.Enter();
	}
	
	public void Update () {
		if(currentTool != null)
			currentTool.Loop();
	}

	// Called by the tools in order to send events back to the vector field.
	public void EditVectorField (List<Point> editedPoints) {
		if(OnEditVectorField != null)
			OnEditVectorField(editedPoints);
	}
}