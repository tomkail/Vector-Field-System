using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class VectorFieldTurbulenceAffector : VectorFieldAffector {
	
	public bool simulateInLocalSpace;
	public bool clampInBounds = true;
	public NoiseSampler noiseSampler;
	public Texture2D cookieTexture;
	public HeightMap cookieMap;

	public override Vector2 Evaluate(Vector3 position) {
		var localPosition = transform.InverseTransformPoint(position);
		if(clampInBounds)
			if(localPosition.x < -1 || localPosition.x > 1 || localPosition.y < -1 || localPosition.y > 1) return Vector2.zero;
		var cookieValue = 1f;
		if (cookieTexture != null) {
			var normalizedPosition = new Vector2((localPosition.x + 0.5f),( localPosition.y + 0.5f));
			cookieMap = new HeightMap(new Point(cookieTexture.width, cookieTexture.height), cookieTexture.GetPixels().Select(x => x.r).ToArray());
			cookieValue = cookieMap.GetValueAtNormalizedPosition(normalizedPosition);
		}

		var rawVector = noiseSampler.SampleAtPosition(noiseSampler.position + (simulateInLocalSpace ? localPosition : position)).derivative;
		return rawVector * (magnitude * (1f / noiseSampler.properties.frequency) * cookieValue);
	}
	
	public override void UpdateVectorField (float deltaTime) {
		var points = clampInBounds ? vectorFieldManager.gridRenderer.GetPointsInWorldBounds(transform.GetBounds()) : vectorFieldManager.vectorField.Points();
		foreach(var point in points) {
			var pointWorldPosition = vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(point);
			Vector2 vectorFieldForce = vectorFieldManager.vectorField.GetValueAtGridPoint(point);
			Vector2 affectorForce = Evaluate(pointWorldPosition);
			Vector2 finalForce = Vector2.zero;

			if (blendMode == BlendMode.Add) {
				if(components.HasFlag(Component.All)) finalForce = affectorForce + vectorFieldForce;
				else if(components.HasFlag(Component.Direction)) finalForce = affectorForce + vectorFieldForce.magnitude * affectorForce.normalized;
				else if (components.HasFlag(Component.Magnitude)) finalForce = vectorFieldForce + vectorFieldForce.normalized * affectorForce.magnitude;
			}

			if (blendMode == BlendMode.Blend) {
				if(components.HasFlag(Component.All)) finalForce = Vector2.Lerp(vectorFieldForce, affectorForce, affectorForce.magnitude/magnitude);
				else if(components.HasFlag(Component.Direction)) finalForce = affectorForce.normalized * vectorFieldForce.magnitude/magnitude;
				else if (components.HasFlag(Component.Magnitude)) finalForce = vectorFieldForce.normalized * affectorForce.magnitude/magnitude;
			}
			vectorFieldManager.vectorField.SetValueAtGridPoint(point, finalForce);
		}
	}
	
	void OnDrawGizmos () {
		if (clampInBounds) {
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(1,1,0));
			Gizmos.matrix = m;
		}
	}
}