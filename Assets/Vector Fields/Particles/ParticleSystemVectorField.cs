using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// Allows using a Vector Field Component as a force field in a particle system.
// Creates a 3D texture which is used by ParticleSystemForceField, which can be referenced by Particle System
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystemForceField))]
[RequireComponent(typeof(PositionConstraint), typeof(RotationConstraint), typeof(ScaleConstraint))]
public class ParticleSystemVectorField : MonoBehaviour
{
	[SerializeField] VectorFieldComponent _vectorFieldComponent;
	public VectorFieldComponent vectorFieldComponent
	{
		get => _vectorFieldComponent;
		set
		{
			if (_vectorFieldComponent == value) return;
			if (isActiveAndEnabled) Unsubscribe();
			_vectorFieldComponent = value;
			SetupConstraints();
			if (isActiveAndEnabled) Subscribe();
		}
	}
	ParticleSystemForceField forceField => GetComponent<ParticleSystemForceField>();
	PositionConstraint positionConstraint;
	RotationConstraint rotationConstraint;
	ScaleConstraint scaleConstraint;
	Texture3D texture3D;

	void OnEnable()
	{
		SetupConstraints();
		ConfigureForceField();
		Subscribe();
	}

	// Tell the field we need its CPU copy (it won't produce one otherwise), and refresh whenever it's ready. We
	// don't need it the same frame it changes, so register as a non-immediate consumer — that keeps GPU-combine
	// fields on the async readback (no per-frame stall) even when they change every frame.
	void Subscribe()
	{
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnCpuDataReady += Refresh;
		_vectorFieldComponent.RegisterCpuConsumer(this, immediate: false);
		Refresh(); // pick up data that's already available
	}

	void Unsubscribe()
	{
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnCpuDataReady -= Refresh;
		_vectorFieldComponent.UnregisterCpuConsumer(this);
	}

	// The force field's shape/range/gravity/etc. never change at runtime, so set them once rather than on every Refresh.
	void ConfigureForceField()
	{
		forceField.shape = ParticleSystemForceFieldShape.Box;
		forceField.startRange = 0f;
		forceField.endRange = 0.5f;

		forceField.directionX = 0f;
		forceField.directionY = 0f;
		forceField.directionZ = 0f;

		forceField.gravity = 0f;
		forceField.gravityFocus = 0f;

		forceField.rotationAttraction = 0f;
		forceField.rotationRandomness = Vector2.zero;
		forceField.rotationSpeed = 0f;

		forceField.drag = 0f;
	}

	private void SetupConstraints()
	{
		if (vectorFieldComponent == null) return;

		positionConstraint = GetComponent<PositionConstraint>();
		rotationConstraint = GetComponent<RotationConstraint>();
		scaleConstraint = GetComponent<ScaleConstraint>();

		// Hide constraint components
		positionConstraint.hideFlags = HideFlags.HideInInspector;
		rotationConstraint.hideFlags = HideFlags.HideInInspector;
		scaleConstraint.hideFlags = HideFlags.HideInInspector;

		// Setup position constraint
		positionConstraint.constraintActive = true;
		var posSource = new ConstraintSource { sourceTransform = vectorFieldComponent.transform, weight = 1 };
		positionConstraint.SetSource(0, posSource);

		// Setup rotation constraint
		rotationConstraint.constraintActive = true;
		var rotSource = new ConstraintSource { sourceTransform = vectorFieldComponent.transform, weight = 1 };
		rotationConstraint.SetSource(0, rotSource);

		// Setup scale constraint
		scaleConstraint.constraintActive = true;
		var scaleSource = new ConstraintSource { sourceTransform = vectorFieldComponent.transform, weight = 1 };
		scaleConstraint.SetSource(0, scaleSource);
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void Refresh()
	{
		if (_vectorFieldComponent == null || _vectorFieldComponent.vectorField == null) return;
		// Allocate a fresh Texture3D each refresh. ParticleSystemForceField only re-reads its vector field when the
		// texture reference changes, so updating one in place (SetPixels/Apply) leaves the particles on stale data.
		// Refresh now runs only when the field changes (not every frame), so this allocation is no longer per-frame.
		if (texture3D != null) ObjectX.DestroyAutomatic(texture3D);
		texture3D = VectorFieldUtils.CreateTexture3D(_vectorFieldComponent.vectorField);

		forceField.vectorField = texture3D;
		forceField.vectorFieldSpeed = _vectorFieldComponent.magnitude;
	}

	void OnValidate()
	{
		SetupConstraints();
	}
}
