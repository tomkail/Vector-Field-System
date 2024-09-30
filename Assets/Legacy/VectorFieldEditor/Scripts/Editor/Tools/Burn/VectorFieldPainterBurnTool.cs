using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterBurnTool : VectorFieldPainterTool {

	public override string name => "Burn";
	public override string iconPath => "VectorFieldEditor/Icons/Tools/BurnIcon";

	public float strength = 1;
	
	public VectorFieldPainterBurnTool (VectorFieldPainterToolManager toolManager) : base (toolManager) {}
	
	public override void Loop () {
		base.Loop();
		if(toolManager.editorWindow.leftMouseIsPressed && toolManager.editorWindow.canUseTools) {
			Apply(toolManager.editorWindow.canvasWindow.gridPosition);
		}
	}
	
	private void Apply (Vector2 targetPosition) {
		List<Point> editedPoints = new List<Point>();
		HeightMap brushMap = brush.intensityMap;
		foreach(TypeMapCellInfo<float> cellInfo in brushMap) {
			Point gridPoint = new Point(targetPosition.x-brush.radius, targetPosition.y-brush.radius) + cellInfo.point;
			if(toolManager.editorWindow.vectorField.IsOnGrid(gridPoint)) {
				int index = toolManager.editorWindow.vectorField.GridPointToArrayIndex(gridPoint);
				toolManager.editorWindow.vectorField.values[index] += toolManager.editorWindow.vectorField.values[index].normalized * cellInfo.value * strength * EditorTime.deltaTime;
				editedPoints.Add(gridPoint);
			}
		}
		toolManager.EditVectorField(editedPoints);
	}
}
