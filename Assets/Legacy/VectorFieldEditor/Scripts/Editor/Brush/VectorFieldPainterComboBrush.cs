using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[System.Serializable]
public class VectorFieldPainterComboBrush : VectorFieldPainterBrush {
	
	public int numBrushes = 12;
	public List<VectorFieldPainterBasicBrush> brushes = new List<VectorFieldPainterBasicBrush>();
	
//	public override float GetBrushIntensity (Vector2 targetPosition) {
//		float intensity = 0;
//		foreach(VectorFieldPainterBasicBrush brush in brushes) {
//			intensity += brush.GetBrushIntensity(targetPosition);
//		}
//		return intensity;
//	}
}
