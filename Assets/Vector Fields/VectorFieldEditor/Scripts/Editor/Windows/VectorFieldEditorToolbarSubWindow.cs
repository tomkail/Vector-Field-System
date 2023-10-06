using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityX.Geometry;

public class VectorFieldEditorToolbarSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "Toolbar";}}

	Texture2D brushTexture;
	bool isDirty = true;

	public VectorFieldEditorToolbarSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {}

	public override void DrawWindow() {
		rect = new Rect(new Vector2(0,40), new Vector2(editorWindow.position.width, 40));
		if(editorWindow.vectorField == null) return;

		GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(rect.height), GUILayout.MaxWidth(rect.width));
		{
			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			foreach(var tool in editorWindow.toolManager.tools) {
				Color savedColor = GUI.color;
				if(editorWindow.toolManager.currentToolType == tool.Key) {
					GUI.color = Color.green;
				}
				if(GUILayout.Button(new GUIContent(tool.Value.icon, tool.Value.name), GUILayout.Width(32), GUILayout.Height(24))) {
					editorWindow.toolManager.ChangeTool(tool.Key);
				}
				GUI.color = savedColor;
			}
			EditorGUILayout.EndHorizontal();
		}
		EditorGUILayout.BeginHorizontal(GUI.skin.box, GUILayout.MaxWidth(editorWindow.position.width));

		if(editorWindow.toolManager.currentTool.brush.intensityMap != null && isDirty) {
			MonoBehaviour.DestroyImmediate(brushTexture);
			if(editorWindow.toolManager.currentTool.brush.intensityMap.size != Point.zero) {
				Color[] colorArray = editorWindow.toolManager.currentTool.brush.intensityMap.values.Select(x => ColorX.ToGrayscaleColor(x)).ToArray();
				brushTexture = TextureX.Create(editorWindow.toolManager.currentTool.brush.intensityMap.size.x, editorWindow.toolManager.currentTool.brush.intensityMap.size.y, colorArray);
				brushTexture.Apply();
				isDirty = false;
			}
		}
		if(brushTexture != null) {
			GUILayout.Box(new GUIContent(""), GUILayout.Width(22), GUILayout.Height(22));

			if (Event.current.type == EventType.Repaint) {
				EditorGUI.DrawTextureTransparent(GUILayoutUtility.GetLastRect(), brushTexture);
			}
		}

		{
			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			EditorGUILayout.LabelField("Size", GUILayout.Width(30));
			
			editorWindow.toolManager.currentTool.brush.maxSize = (editorWindow.vectorField.size.magnitude * 0.3f).RoundToInt();
			editorWindow.toolManager.currentTool.brush.maxSize = Mathf.Max(editorWindow.toolManager.currentTool.brush.maxSize, 2);
			editorWindow.toolManager.currentTool.brush.maxSize = Mathf.Min(editorWindow.toolManager.currentTool.brush.maxSize, 200);
			
			if(!editorWindow.toolManager.currentTool.brush.sizeDistanceTweening) {
				ChangeBrushSize(editorWindow.toolManager.currentTool, GUILayout.HorizontalSlider(editorWindow.toolManager.currentTool.brush.size, editorWindow.toolManager.currentTool.brush.minSize, editorWindow.toolManager.currentTool.brush.maxSize, GUILayout.Width(100)).RoundToInt());
				ChangeBrushSize(editorWindow.toolManager.currentTool, EditorGUILayout.IntField(editorWindow.toolManager.currentTool.brush.size, GUILayout.Width(40)));

				EditorGUILayout.CurveField(editorWindow.toolManager.currentTool.brush.targetSizeCurve, GUILayout.Width(100));
				if(GUILayout.Button("Tween", GUILayout.Width(60))) {
					editorWindow.toolManager.currentTool.brush.sizeDistanceTweening = true;
					editorWindow.toolManager.currentTool.brush.sizeDistanceTweenTime = 0;
				}
			} else {
				EditorGUI.BeginDisabledGroup(true);
				GUILayout.HorizontalSlider(editorWindow.toolManager.currentTool.brush.size, editorWindow.toolManager.currentTool.brush.minSize, editorWindow.toolManager.currentTool.brush.maxSize, GUILayout.Width(100)).RoundToInt();
				EditorGUILayout.IntField(editorWindow.toolManager.currentTool.brush.size, GUILayout.Width(40));
				EditorGUI.EndDisabledGroup();

				float currentTime = editorWindow.toolManager.currentTool.brush.sizeDistanceTweenTime;
				float targetTime = editorWindow.toolManager.currentTool.brush.targetSizeCurve.keys.Last().time;
				EditorGUI.BeginDisabledGroup(true);
				GUILayout.HorizontalSlider(currentTime, 0, targetTime, GUILayout.Width(60));
				EditorGUILayout.FloatField(currentTime, GUILayout.Width(40));
				EditorGUI.EndDisabledGroup();
				if(GUILayout.Button("Stop", GUILayout.Width(60))) {
					editorWindow.toolManager.currentTool.brush.sizeDistanceTweening = false;
				}
			}
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.BeginHorizontal(GUI.skin.box);
		EditorGUILayout.LabelField("Hardness", GUILayout.Width(60));
		ChangeBrushHardness(editorWindow.toolManager.currentTool, GUILayout.HorizontalSlider(editorWindow.toolManager.currentTool.brush.brushHardness, editorWindow.toolManager.currentTool.brush.minHardness, editorWindow.toolManager.currentTool.brush.maxHardness, GUILayout.Width(60)));
		ChangeBrushHardness(editorWindow.toolManager.currentTool, EditorGUILayout.FloatField(editorWindow.toolManager.currentTool.brush.brushHardness, GUILayout.Width(40)));
		EditorGUILayout.EndHorizontal();

		if(editorWindow.toolManager.currentToolType == VectorFieldPainterToolManager.ToolType.Smudge) {
			VectorFieldPainterSmudgeTool smudgeTool = (VectorFieldPainterSmudgeTool)editorWindow.toolManager.tools[VectorFieldPainterToolManager.ToolType.Smudge];

			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			EditorGUILayout.LabelField("Pressure", GUILayout.Width(60));
			if(!smudgeTool.pressureDistanceTweening) {
				ChangeSmudgePressure(smudgeTool, GUILayout.HorizontalSlider(smudgeTool.pressure.RoundToSig(3), smudgeTool.minPressure, smudgeTool.maxPressure, GUILayout.Width(60)));
				ChangeSmudgePressure(smudgeTool, EditorGUILayout.FloatField(smudgeTool.pressure.RoundToSig(3), GUILayout.Width(40)));

				EditorGUILayout.CurveField(smudgeTool.targetPressureCurve, GUILayout.Width(100));
				if(GUILayout.Button("Tween", GUILayout.Width(60))) {
					smudgeTool.pressureDistanceTweening = true;
					smudgeTool.pressureDistanceTweenTime = 0;
				}
			} else {
				EditorGUI.BeginDisabledGroup(true);
				GUILayout.HorizontalSlider(smudgeTool.pressure.RoundToSig(3), smudgeTool.minPressure, smudgeTool.maxPressure, GUILayout.Width(60));
				EditorGUILayout.FloatField(smudgeTool.pressure.RoundToSig(3), GUILayout.Width(40));
				EditorGUI.EndDisabledGroup();

				float currentTime = smudgeTool.pressureDistanceTweenTime;
				float targetTime = smudgeTool.targetPressureCurve.keys.Last().time;
				EditorGUI.BeginDisabledGroup(true);
				GUILayout.HorizontalSlider(currentTime, 0, targetTime, GUILayout.Width(60));
				EditorGUILayout.FloatField(currentTime, GUILayout.Width(40));
				EditorGUI.EndDisabledGroup();
				if(GUILayout.Button("Stop", GUILayout.Width(60))) {
					smudgeTool.pressureDistanceTweening = false;
				}
			}
			EditorGUILayout.EndHorizontal();
		}
		else if(editorWindow.toolManager.currentToolType == VectorFieldPainterToolManager.ToolType.Burn) {
			VectorFieldPainterBurnTool burnTool = (VectorFieldPainterBurnTool)editorWindow.toolManager.tools[VectorFieldPainterToolManager.ToolType.Burn];

			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			EditorGUILayout.LabelField("Strength", GUILayout.Width(60));
			ChangeBurnStrength(burnTool, GUILayout.HorizontalSlider(burnTool.strength.RoundToNearest(0.1f), -10, 10, GUILayout.Width(60)));
			ChangeBurnStrength(burnTool, EditorGUILayout.FloatField(burnTool.strength.RoundToNearest(0.1f), GUILayout.Width(50)));
			EditorGUILayout.EndHorizontal();
		} else if(editorWindow.toolManager.currentToolType == VectorFieldPainterToolManager.ToolType.Clamp) {
			VectorFieldPainterClampTool clampTool = (VectorFieldPainterClampTool)editorWindow.toolManager.tools[VectorFieldPainterToolManager.ToolType.Clamp];

			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			EditorGUILayout.LabelField("Strength", GUILayout.Width(60));
			ChangeClampStrength(clampTool, GUILayout.HorizontalSlider(clampTool.strength.RoundToSig(3), 0, 10, GUILayout.Width(60)));
			ChangeClampStrength(clampTool, EditorGUILayout.FloatField(clampTool.strength.RoundToSig(3), GUILayout.Width(40)));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			EditorGUILayout.LabelField("Clamp", GUILayout.Width(60));
			ChangeClampMax(clampTool, GUILayout.HorizontalSlider(clampTool.maxMagnitude.RoundToSig(3), 0, 100, GUILayout.Width(60)));
			ChangeClampMax(clampTool, EditorGUILayout.FloatField(clampTool.maxMagnitude.RoundToSig(3), GUILayout.Width(40)));
			EditorGUILayout.EndHorizontal();
		}
		EditorGUILayout.EndHorizontal();

		GUILayout.EndHorizontal();
	}

	void ChangeBrushSize (VectorFieldPainterTool tool, int size) {
		if(size != tool.brush.size) {
			tool.brush.sizeDistanceTweening = false;
			editorWindow.brushExampleTween.Tween(1,0,2.5f,AnimationCurveX.EaseIn(0.25f,1f,0f,1f));
			tool.brush.size = size;
			isDirty = true;
		}
	}

	void ChangeBrushHardness (VectorFieldPainterTool tool, float hardness) {
		if(hardness != tool.brush.brushHardness) {
			tool.brush.brushHardness = hardness;
			isDirty = true;
		}
	}

	void ChangeSmudgePressure (VectorFieldPainterSmudgeTool smudgeTool, float pressure) {
		if(pressure != smudgeTool.pressure) {
			smudgeTool.pressureDistanceTweening = false;
			smudgeTool.pressure = pressure;
			isDirty = true;
		}
	}

//	void ChangeTargetSmudgePressure (VectorFieldPainterSmudgeTool smudgeTool, float targetPressure) {
//		if(targetPressure != smudgeTool.targetPressure) {
//			smudgeTool.targetPressure = targetPressure;
//			isDirty = true;
//		}
//	}
//
//	void ChangeTargetSmudgeBrushSize (VectorFieldPainterSmudgeTool smudgeTool, int targetSize) {
//		if(targetSize != smudgeTool.targetSize) {
//			smudgeTool.targetSize = targetSize;
//			isDirty = true;
//		}
//	}

//	void ChangeTargetSmudgePressureTweenDistance (VectorFieldPainterSmudgeTool smudgeTool, float targetPressureTweenDistance) {
//		if(targetPressureTweenDistance != smudgeTool.tweenDistance) {
//			smudgeTool.tweenDistance = targetPressureTweenDistance.RoundToInt();
//			isDirty = true;
//		}
//	}



	void ChangeClampStrength (VectorFieldPainterClampTool clampTool, float strength) {
		if(strength != clampTool.strength) {
			clampTool.strength = strength;
			isDirty = true;
		}
	}

	void ChangeClampMax (VectorFieldPainterClampTool clampTool, float maxMagnitude) {
		if(maxMagnitude != clampTool.strength) {
			clampTool.maxMagnitude = maxMagnitude;
			isDirty = true;
		}
	}

	void ChangeBurnStrength (VectorFieldPainterBurnTool burnTool, float strength) {
		if(strength != burnTool.strength) {
			burnTool.strength = strength;
			isDirty = true;
		}
	}
}