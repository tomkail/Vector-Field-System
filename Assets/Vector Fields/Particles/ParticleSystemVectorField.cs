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
	// Maps flow magnitude (0..1 along the X axis) to a remapped magnitude (Y), reshaping how the field's strength drives
	// the particles' force. Default is identity (linear 0->1), so the field is unchanged until you edit it; e.g. drop
	// weak regions to zero with a threshold, or ease the falloff. Baked into a LUT so the per-voxel cost is a cheap
	// lookup rather than an AnimationCurve.Evaluate, and only re-applied on Refresh (when the field changes), never
	// per-frame.
	[SerializeField, CurveRange(0, 0, 1, 1)] AnimationCurve amplitudeCurve = AnimationCurve.Linear(0, 0, 1, 1);
	const int AmplitudeResolution = 256;
	float[] amplitudeLut;

	ParticleSystemForceField forceField => GetComponent<ParticleSystemForceField>();
	PositionConstraint positionConstraint;
	RotationConstraint rotationConstraint;
	ScaleConstraint scaleConstraint;
	Texture3D texture3D;

	void OnEnable()
	{
		SetupConstraints();
		ConfigureForceField();
		BakeAmplitudeLut();
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
		texture3D = VectorFieldUtils.CreateTexture3D(_vectorFieldComponent.vectorField, amplitudeLut);

		forceField.vectorField = texture3D;
		forceField.vectorFieldSpeed = _vectorFieldComponent.magnitude;
	}

	// Bake the amplitude curve into a LUT once, so Refresh's per-voxel remap is a cheap lookup. Reuses the array in
	// place; a null/identity curve leaves amplitudeLut null so CreateTexture3D skips the remap entirely.
	void BakeAmplitudeLut()
	{
		if (amplitudeCurve == null) { amplitudeLut = null; return; }
		if (amplitudeLut == null || amplitudeLut.Length != AmplitudeResolution) amplitudeLut = new float[AmplitudeResolution];
		for (int i = 0; i < AmplitudeResolution; i++)
			amplitudeLut[i] = amplitudeCurve.Evaluate(i / (float)(AmplitudeResolution - 1));
	}

	void OnValidate()
	{
		SetupConstraints();
		BakeAmplitudeLut();
		if (isActiveAndEnabled) Refresh();
	}
}
