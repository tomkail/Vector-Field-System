using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldDotsDebugView : VectorFieldDebugView {

	protected override void UpdateParticle (int index) {
		Point gridPoint = VectorFieldManager.Instance.vectorField.ArrayIndexToGridPoint(index);

		Vector3 worldPosition = VectorFieldManager.Instance.gridRenderer.cellCenter.GridToWorldPoint(gridPoint) + VectorFieldManager.Instance.gridRenderer.floorPlane.normal * heightOffset;
		particles[index].position = worldPosition;

		particles[index].startColor = new Color32(255,255,255,(byte)(255*opacity));

		Vector3 direction = VectorFieldManager.Instance.EvaluateVector(worldPosition);
		particles[index].rotation3D = Quaternion.LookRotation(VectorFieldManager.Instance.planeNormal, direction).eulerAngles;
	}
}
