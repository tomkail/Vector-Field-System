using UnityEngine;
using System.Collections;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterStampTool : VectorFieldPainterTool {

	public override string name => "Stamp";
	public override string iconPath => "VectorFieldEditor/Icons/Tools/StampIcon";

	public float strength = 50;
	
	public VectorFieldPainterStampTool (VectorFieldPainterToolManager toolManager) : base (toolManager) {}
	
	public override void Enter () {
		base.Enter();
	}
	
	public override void Loop () {
		base.Loop();
//		if(editing) {
//			Apply(VectorFieldInputManager.Instance.gridPosition);
//		}
	}
	
//	private void Apply (Vector2 targetPosition) {
//		for(int y = 0; y < SpaceGameWorld.Instance.vectorFieldManager.vectorField.size.y; y++){
//			for(int x = 0; x < SpaceGameWorld.Instance.vectorFieldManager.vectorField.size.x; x++){
//				Point gridPoint = new Point(x, y);
//				Vector2 relativePoint = targetPosition - (Vector2)gridPoint;
////				if(repeatAroundEdges) relativePoint = SpaceGameWorld.Instance.vectorFieldManager.vectorField.RepeatGridPosition(relativePoint);
//				if(RectX.CreateFromCenter(Vector2.zero, Vector2.one * brush.size).Contains(relativePoint)) {
//					float brushIntensity = brush.GetBrushIntensity(relativePoint);
//					int index = SpaceGameWorld.Instance.vectorFieldManager.vectorField.GridPointToArrayIndex(x,y);
//					SpaceGameWorld.Instance.vectorFieldManager.vectorField.values[index] += Vector2.right * brushIntensity * strength * Time.deltaTime;
//				}
//			}
//		}
//	}
}
