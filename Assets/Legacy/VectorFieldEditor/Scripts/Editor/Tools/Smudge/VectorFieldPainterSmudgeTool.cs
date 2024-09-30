using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterSmudgeTool : VectorFieldPainterTool {
	
	public override string name => "Smudge";
	public override string iconPath => "VectorFieldEditor/Icons/Tools/SmudgeIcon";

	public float pressure = 30f;
	public float minPressure = 0f;
	public float maxPressure = 100f;

	public float targetPressure = 10f;
	public int targetSize = 10;
	public float tweenDistance = 250f;


	public float distance = 0;
	public float stepDistance = 1f;

	public AnimationCurve targetPressureCurve = AnimationCurve.Linear(0,0,200,30);
	public bool pressureDistanceTweening;
	public float pressureDistanceTweenTime = 0;

	public VectorFieldPainterSmudgeTool (VectorFieldPainterToolManager toolManager) : base (toolManager) {}
	
	public override void Enter () {
		base.Enter();
	}

	public override void Loop () {
		base.Loop();
		if(toolManager.editorWindow.canUseTools) {
			if(toolManager.editorWindow.leftMouseIsPressed) {
				if(toolManager.editorWindow.holdingAlt) {
					Sample (toolManager.editorWindow.canvasWindow.gridPoint);
				} else {
					float gridDistanceMoved = toolManager.editorWindow.canvasWindow.deltaGridPosition.magnitude;
					Move(gridDistanceMoved);
					// This is to compensate for the effect of having larger brushes, or of using brushes with different hardness. It's far from perfect, but it's better than nothing. Far worse with very small brushes.
					float sizeHardnessFactor = Mathf.Clamp(Mathf.Clamp((1.75f-brush.brushHardness), 1, 1.75f)/brush.size, 0,1);
					if(gridDistanceMoved >= 0) {
						List<Point> editedPoints = new List<Point>();
						distance += gridDistanceMoved;
						float interval = stepDistance / (distance-stepDistance);
						int i = 0;
						while (distance >= stepDistance) {
							Vector2 position = Vector2.Lerp(toolManager.editorWindow.canvasWindow.lastGridPosition, toolManager.editorWindow.canvasWindow.gridPosition, interval * i);
							Vector2 vector = toolManager.editorWindow.canvasWindow.deltaGridPosition.normalized * pressure * stepDistance * sizeHardnessFactor;
							editedPoints.AddRange(Smudge(position, vector));
							distance -= stepDistance;
							i++;
						}
						toolManager.EditVectorField(editedPoints);
					}
				}
			} else if(toolManager.editorWindow.rightMouseIsPressed) {
				if(toolManager.editorWindow.holdingAlt) {
					Sample (toolManager.editorWindow.canvasWindow.gridPoint);
				} else {
					float gridDistanceMoved = toolManager.editorWindow.canvasWindow.deltaGridPosition.magnitude;
					// This is to compensate for the effect of having larger brushes, or of using brushes with different hardness. It's far from perfect, but it's better than nothing. Far worse with very small brushes.
//					float sizeHardnessFactor = Mathf.Clamp(Mathf.Clamp((1.75f-brush.brushHardness), 1, 1.75f)/brush.size, 0,1);
					if(gridDistanceMoved >= 0) {
						List<Point> editedPoints = new List<Point>();
						distance += gridDistanceMoved;
						float interval = stepDistance / (distance-stepDistance);
						int i = 0;
						while (distance >= stepDistance) {
							Vector2 position = Vector2.Lerp(toolManager.editorWindow.canvasWindow.lastGridPosition, toolManager.editorWindow.canvasWindow.gridPosition, interval * i);
							editedPoints.AddRange(Erase(position));
							distance -= stepDistance;
							i++;
						}
						toolManager.EditVectorField(editedPoints);
					}
				}
			}
		}
	}

	private void Sample (Vector2 targetPosition) {
		Vector2 value = vectorField.GetValueAtGridPosition(targetPosition);
		pressure = value.magnitude;
	}

	private List<Point> Smudge (Vector2 targetPosition, Vector2 vector) {
		List<Point> editedPoints = new List<Point>();
		HeightMap brushMap = brush.intensityMap;
		targetPosition -= Vector2.one * brush.radius;
		Point targetPoint = new Point(targetPosition);
		Point gridPoint = Point.zero;
		int index = 0;
		foreach(var cellInfo in brushMap) {
			gridPoint = targetPoint + cellInfo.point;
			if(vectorField.IsOnGrid(gridPoint)) {
				index = vectorField.GridPointToArrayIndex(gridPoint);
				vectorField.values[index] += cellInfo.value * vector;
				if(toolManager.editorWindow.holdingCommand) {
					float magnitude = vectorField.values[index].magnitude;
					vectorField.values[index] = Vector2.ClampMagnitude(vectorField.values[index], Mathf.Lerp(magnitude, pressure, cellInfo.value));
				}
				editedPoints.Add(gridPoint);
			}
		}
		return editedPoints;
	}

	private List<Point> Erase (Vector2 targetPosition) {
		List<Point> editedPoints = new List<Point>();
		HeightMap brushMap = brush.intensityMap;
		targetPosition -= Vector2.one * brush.radius;
		Point targetPoint = new Point(targetPosition);
		Point gridPoint = Point.zero;
		int index = 0;
		foreach(var cellInfo in brushMap) {
			gridPoint = targetPoint + cellInfo.point;
			if(vectorField.IsOnGrid(gridPoint)) {
				index = vectorField.GridPointToArrayIndex(gridPoint);
				vectorField.values[index] *= 1f-cellInfo.value;
				if(toolManager.editorWindow.holdingCommand) {
					float magnitude = vectorField.values[index].magnitude;
					vectorField.values[index] = Vector2.ClampMagnitude(vectorField.values[index], Mathf.Lerp(magnitude, pressure, cellInfo.value));
				}
				editedPoints.Add(gridPoint);
			}
		}
		return editedPoints;
	}

	public override void Move (float gridDistanceMoved) {
		base.Move (gridDistanceMoved);
		if(pressureDistanceTweening) {
			pressureDistanceTweenTime += gridDistanceMoved;
			pressure = targetPressureCurve.Evaluate(pressureDistanceTweenTime);
			if(pressureDistanceTweenTime > targetPressureCurve.keys.Last().time) {
				pressureDistanceTweening = false;
			}
		}
	}
}