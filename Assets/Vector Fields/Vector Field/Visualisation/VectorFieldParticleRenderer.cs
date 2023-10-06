using UnityEngine;
using System.Collections;
using UnityX.Geometry;

[RequireComponent(typeof(ParticleSystem))]
public class VectorFieldParticleRenderer : MonoBehaviour {

	VectorFieldManager _vectorFieldManager;
	public VectorFieldManager vectorFieldManager {
		get {
			if(_vectorFieldManager == null) _vectorFieldManager = VectorFieldManager.Instance;
			return _vectorFieldManager;
		} set {
			if(_vectorFieldManager == value) return;
			_vectorFieldManager = value;
		}
	}

	public float speedMultiplier = 1;

	private new ParticleSystem particleSystem;
	private ParticleSystem.Particle[] particles;

	
	private void Awake () {
		particleSystem = GetComponent<ParticleSystem>();
	}
	
//	private void Start () {
//		CacheProperties();
//	}
	
//	private void OnEnable () {
//		if(Time.time > 0) {
//			Start();
//		}
//	}
	
	private void OnDisable () {
		particleSystem.Clear();
	}

	private void Update () {
		UpdateParticles();
	}

//	private void CacheProperties () {
//		float fieldScaleFactor = Mathf.Min (vectorFieldManager.vectorField.size.x, vectorFieldManager.vectorField.size.y) / Mathf.Min (vectorFieldManager.scale.x, vectorFieldManager.scale.y);
//		scaleFactor = sizeMultiplier/fieldScaleFactor;
//	}
	
	private void UpdateParticles () {

		particles = new ParticleSystem.Particle[particleSystem.particleCount+1];
		int length = particleSystem.GetParticles(particles);
		for(int i = 0; i < length; i++){
			Vector2 vector = VectorFieldManager.Instance.EvaluateVector(particles [i].position);
			particles [i].velocity = (Vector3)vector * speedMultiplier;
			particles [i].rotation = Vector2X.Degrees(vector);
		}
		particleSystem.SetParticles(particles, length);
	}
}
