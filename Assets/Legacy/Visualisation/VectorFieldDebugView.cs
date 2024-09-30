using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[RequireComponent(typeof(ParticleSystem))]
public class VectorFieldDebugView : MapDebugView {

	public bool rotate = true;

	protected override void UpdateParticle (int index) {
		
		Point gridPoint = particlePoints[index];
		Vector2 vectorFieldValue = Vector2.zero;

		vectorFieldValue = VectorFieldManager.Instance.vectorField.GetValueAtGridPoint(gridPoint);
		float magnitude = vectorFieldValue.magnitude;
		float scaledMagnitude = Mathf.InverseLerp(valueRange.x, valueRange.y, magnitude);
		Vector3 worldPosition = VectorFieldManager.Instance.gridRenderer.cellCenter.GridToWorldPoint(gridPoint) + -Vector3.forward * heightOffset;
		particles[index].position = transform.InverseTransformPoint(worldPosition);
		
		particles[index].startSize = sizeMultiplier * scaledMagnitude;

		float angle = Vector2X.Degrees(vectorFieldValue);

		if(color) {
			Color c = new HSLColor(angle, 1, scaledMagnitude * 0.75f);
//			c = colorGradient.Evaluate(scaledMagnitude);
			c = c.WithAlpha(opacity);
			particles[index].startColor = c;
		} else {
			particles[index].startColor = new Color32(255,255,255,(byte)(255*opacity));
		}
		
		Vector3 gridNormal = -Vector3.forward;
		if(rotate) {
			particles[index].axisOfRotation = gridNormal;
			particles[index].rotation = angle;
		} else {
			particles[index].rotation3D = Quaternion.LookRotation(gridNormal, Vector3.forward).eulerAngles;
		}
	}
}
