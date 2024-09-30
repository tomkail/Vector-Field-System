using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public abstract class VectorFieldAffector : MonoBehaviour {
	public static List<VectorFieldAffector> affectors = new List<VectorFieldAffector>();
	
	public BlendMode blendMode = BlendMode.Add;
	public enum BlendMode {
		// Add to current value
		Add,
		// Lerp between current and new value based on brush alpha
		Blend
	}
	
	public Component components = Component.All;
	[Flags]
	public enum Component {
		None = 0,
		All = ~0,
		// Add to current value
		Magnitude = 1 << 0,
		// Lerp between current and new value based on brush alpha
		Direction = 1 << 1,
	}
	
	public VectorFieldManager vectorFieldManager;
	
	public float magnitude = 1;

	public static Vector2 EvaluateAtPoint (Vector3 position) {
		Vector2 force = Vector2.zero;
		foreach(var affector in affectors) {
			// if(affector.GetComponent<WindProjectile>() == null || affector.GetComponent<WindProjectile>().player != player) {
				force += affector.Evaluate(position);
			// }
		}
		return force;
	}

	void OnEnable () 
	{
		affectors.Add(this);

		if(vectorFieldManager == null) 
			vectorFieldManager = VectorFieldManager.Instance;

		//if we still cant find it return.
		if(vectorFieldManager == null) 
			return;
		
		vectorFieldManager.OnUpdate += UpdateVectorField;
	}

	void OnDisable () 
	{
		affectors.Remove(this);

		//if we cant find it return.
		if(vectorFieldManager == null) 
			return;

		vectorFieldManager.OnUpdate -= UpdateVectorField;
	}

	public abstract Vector2 Evaluate(Vector3 position);

	public abstract void UpdateVectorField(float deltaTime);
}