using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterClampTool : VectorFieldPainterTool {

	public override string name {
		get {
			return "Clamp";
		}
	}
	public override string iconPath {
		get {
			return "Assets/VectorFieldEditor/Icons/Tools/ClampIcon.psd";
		}
	}

	public float strength = 2;
	public float maxMagnitude = 10;
	
	public VectorFieldPainterClampTool (VectorFieldPainterToolManager toolManager) : base (toolManager) {}
	
	public override void Enter () {
		base.Enter();
	}
	
	public override void Loop () {
		base.Loop();
		if(toolManager.editorWindow.leftMouseIsPressed && toolManager.editorWindow.canUseTools) {
			if(toolManager.editorWindow.holdingAlt) {
				Sample (toolManager.editorWindow.canvasWindow.gridPoint);
			} else {
				Apply(toolManager.editorWindow.canvasWindow.gridPosition);
			}
		}
	}

	private void Sample (Vector2 targetPosition) {
		Vector2 value = vectorField.GetValueAtGridPosition(targetPosition);
		maxMagnitude = value.magnitude;
	}
	
	private void Apply (Vector2 targetPosition) {
		List<Point> editedPoints = new List<Point>();
		HeightMap brushMap = brush.intensityMap;
		foreach(var cellInfo in brushMap) {
			Point gridPoint = new Point(targetPosition.x-brush.radius, targetPosition.y-brush.radius) + cellInfo.point;
			if(vectorField.IsOnGrid(gridPoint)) {
				int index = vectorField.GridPointToArrayIndex(gridPoint);
				float magnitude = vectorField.values[index].magnitude;
				magnitude = Mathf.Lerp(magnitude, Mathf.Min(magnitude, maxMagnitude), cellInfo.value * strength * EditorTime.deltaTime);
				vectorField.values[index] = vectorField.values[index].normalized * magnitude;
				editedPoints.Add(gridPoint);
			}
		}
		toolManager.EditVectorField(editedPoints);
	}
}
