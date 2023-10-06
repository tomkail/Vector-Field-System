using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

[ExecuteAlways]
public abstract class MapDebugView : MonoBehaviour {
	public VectorFieldManager vectorFieldManager;
	public Vector2Map vectorField => vectorFieldManager.vectorField;

	public new ParticleSystem particleSystem;

	public Vector2 spawnSize;

	[SetProperty("opacity")]
	public float _opacity = 0.5f;
	public float opacity {
		get => _opacity;
		set {
			_opacity = value;
			enabled = (_opacity > 0.01f);
		}
	}

	public float heightOffset;

	public bool update = true;
	public bool clearOnUpdate = false;

	public Vector2 valueRange = new Vector2(0,1);

	public float sizeMultiplier = 0.1f;

	public bool color = false;

	protected ParticleSystem.Particle[] particles;
	protected Point[] particlePoints;

	protected virtual void Awake () {
		particleSystem = GetComponent<ParticleSystem>();
		opacity = opacity;
	}
	
	private void Start () {
		CreateParticles();
	}

	void OnEnable () {
		if(Time.time > 0) {
			Start();
		}
		vectorFieldManager.OnRender += Render;
		CreateParticles();
	}

	void OnDisable () {
		particleSystem.Clear();
		vectorFieldManager.OnRender -= Render;
	}

	void Update() {
		Render(Time.deltaTime);
	}
	
	void Render (float deltaTime) {
		if(update) {
			if(clearOnUpdate) {
				CreateParticles();
			}
			Refresh();
		}
	}
	
	public void Refresh () {
		var emission = particleSystem.emission;
		emission.enabled = false;
		
		UpdateParticles();
	}

	[EasyButtons.Button("Create Particles")]
	public void CreateParticles () {
		particleSystem.Clear();

		particlePoints = new Point[vectorFieldManager.vectorField.cellCount];

		foreach(var cell in vectorFieldManager.vectorField) {
			particlePoints[cell.index] = cell.point;
			EmitParticle(particlePoints[cell.index]);
		}
		Refresh();
	}
	
	private void EmitParticle (Point gridPoint) {
		ParticleSystem.EmitParams particle = CreateParticle(gridPoint);
		particleSystem.Emit(particle, 1);
	}

	private void EmitParticle (int index) {
		Point gridPoint = vectorFieldManager.vectorField.ArrayIndexToGridPoint(index);
		EmitParticle(gridPoint);
	}

	protected virtual ParticleSystem.EmitParams CreateParticle (Point gridPoint) {
		ParticleSystem.EmitParams particleEmitParams = new ParticleSystem.EmitParams();
		particleEmitParams.position = vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(gridPoint) + Vector3.up * heightOffset;
		particleEmitParams.startColor = Color.white;
		particleEmitParams.velocity = Vector3.zero;
		particleEmitParams.startLifetime = Mathf.Infinity;
		return particleEmitParams;
	}
	
	private void UpdateParticles () {
		particles = new ParticleSystem.Particle[particleSystem.particleCount+1];
		int length = particleSystem.GetParticles(particles);
		for(int i = 0; i < length; i++) {
			UpdateParticle(i);
		}
		particleSystem.SetParticles(particles, length);
	}

	protected abstract void UpdateParticle (int index);
}
