using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public abstract class VectorFieldPainterTool {

	protected VectorFieldPainterToolManager toolManager;
	protected Vector2Map vectorField {
		get {
			return toolManager.editorWindow.vectorField;
		}
	}

	public abstract string name {get;}
	public abstract string iconPath {get;}

	public VectorFieldPainterBrush brush;
//	public bool editing = false;

	public Texture2D icon {
		get {
			return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
		}
	}

	public VectorFieldPainterTool (VectorFieldPainterToolManager toolManager) {
		this.toolManager = toolManager;
	}

	public virtual void Enter () {
		ChangeBrush(new VectorFieldPainterBasicBrush());
	}

	public virtual void Exit () {}
	
	public virtual void Loop () {}
	
	public virtual void ChangeBrush (VectorFieldPainterBrush newBrush) {
		brush = newBrush;
		brush.Init ();
	}

	public virtual void Move (float gridDistanceMoved) {
		if(brush.sizeDistanceTweening) {
			brush.sizeDistanceTweenTime += gridDistanceMoved;
			brush.size = brush.targetSizeCurve.Evaluate(brush.sizeDistanceTweenTime).RoundToInt();
			if(brush.sizeDistanceTweenTime > brush.targetSizeCurve.keys.Last().time) {
				brush.sizeDistanceTweening = false;
			}
		}
	}
}
