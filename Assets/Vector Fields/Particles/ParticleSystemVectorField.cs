using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

// Allows using a Vector Field Component as a force field in a particle system.
// Creates a 3D texture which is used by ParticleSystemForceField, which can be referenced by Particle System
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Consumers/Particle System Vector Field")]
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
			_vectorFieldComponent = value;
			SetupConstraints();
			if (isActiveAndEnabled) Subscribe(); // Subscribe reconciles: drops the old field, hooks the new one
		}
	}
	// The field we currently hold an OnCpuDataReady handler + CPU-consumer registration on — the single source of
	// truth for our subscription. It can diverge from _vectorFieldComponent when the inspector writes the serialized
	// field directly (that bypasses the property setter); Subscribe()/OnValidate reconcile the two. Not serialized: a
	// live subscription can't survive a domain reload, so OnEnable re-establishes it from scratch.
	[NonSerialized] VectorFieldComponent _subscribedComponent;
	// Maps flow magnitude (0..1 along the X axis) to a remapped magnitude (Y), reshaping how the field's strength drives
	// the particles' force. Default is identity (linear 0->1), so the field is unchanged until you edit it; e.g. drop
	// weak regions to zero with a threshold, or ease the falloff. Baked into a LUT so the per-voxel cost is a cheap
	// lookup rather than an AnimationCurve.Evaluate, and only re-applied on Refresh (when the field changes), never
	// per-frame.
	// Rendered as a 0..1 ranged curve by ParticleSystemVectorFieldEditor (was [CurveRange]).
	[SerializeField] AnimationCurve amplitudeCurve = AnimationCurve.Linear(0, 0, 1, 1);
	const int AmplitudeResolution = 256;
	float[] amplitudeLut;

	// Cached (RequireComponent guarantees it exists). Re-resolves if the cache is cleared by a domain reload. Avoids a
	// GetComponent on every access, including each Refresh.
	ParticleSystemForceField _forceField;
	ParticleSystemForceField forceField => _forceField ? _forceField : (_forceField = GetComponent<ParticleSystemForceField>());
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

	// Reconcile our subscription so we're hooked to _vectorFieldComponent and nothing else, then refresh. Tell the
	// field we need its CPU copy (it won't produce one otherwise). We don't need it the same frame it changes, so
	// register as a non-immediate consumer — that keeps GPU-combine fields on the async readback (no per-frame stall)
	// even when they change every frame. Idempotent: safe to call after an inspector edit has swapped the serialized
	// field out from under us, and calling it twice never double-subscribes.
	void Subscribe()
	{
		if (_subscribedComponent != _vectorFieldComponent) Unsubscribe(); // drop the stale field (no-op if none)
		if (_vectorFieldComponent != null && _subscribedComponent == null)
		{
			_subscribedComponent = _vectorFieldComponent;
			_subscribedComponent.OnCpuDataReady += Refresh;
			_subscribedComponent.RegisterCpuConsumer(this, immediate: false);
		}
		Refresh(); // pick up data that's already available (or clear stale output if the field is now null)
	}

	void Unsubscribe()
	{
		if (_subscribedComponent == null) return;
		_subscribedComponent.OnCpuDataReady -= Refresh;
		_subscribedComponent.UnregisterCpuConsumer(this);
		_subscribedComponent = null;
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

	// Release the Texture3D we allocated. It's flagged DontSave, so it isn't cleaned up by serialization — without
	// this it would leak until the next domain reload (e.g. add/remove the component repeatedly in the editor).
	void OnDestroy()
	{
		if (texture3D != null)
		{
			VectorFieldObjectUtils.DestroyAutomatic(texture3D);
			texture3D = null;
		}
	}

	void Refresh()
	{
		// No field (or no CPU copy yet): tear down our texture and clear the force field so particles don't keep
		// running on stale data after the reference is cleared. The force field's own texture is left to whatever
		// authored it; we only clear a texture we created.
		if (_vectorFieldComponent == null || _vectorFieldComponent.vectorField == null)
		{
			if (texture3D != null)
			{
				if (forceField.vectorField == texture3D) forceField.vectorField = null;
				VectorFieldObjectUtils.DestroyAutomatic(texture3D);
				texture3D = null;
			}
			return;
		}
		// Allocate a fresh Texture3D each refresh. ParticleSystemForceField only re-reads its vector field when the
		// texture reference changes, so updating one in place (SetPixels/Apply) leaves the particles on stale data.
		// Refresh runs only when the field changes (not every frame), so this allocation is not per-frame.
		if (texture3D != null) VectorFieldObjectUtils.DestroyAutomatic(texture3D);
		texture3D = VectorFieldUtils.CreateTexture3D(_vectorFieldComponent.vectorField, amplitudeLut);
		// Derived data, regenerated here every OnEnable/Refresh. Under [ExecuteAlways] it's created at edit time and
		// assigned to the ParticleSystemForceField (a scene component), so without this Unity would embed the whole
		// Texture3D into the scene/prefab/build (huge). DontSave keeps it out of serialization; it's rebuilt on load.
		texture3D.hideFlags = HideFlags.DontSave;

		forceField.vectorField = texture3D;
		// magnitude is now baked into the field's output (and thus into vectorField / texture3D above), so the force
		// field speed is a plain 1 — folding in magnitude here too would apply it twice.
		forceField.vectorFieldSpeed = 1f;
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

	// Inspector edits write _vectorFieldComponent directly, bypassing the property setter, so reconcile here.
	// Subscribe() re-points our subscription if the reference changed and refreshes either way; when only the
	// amplitude curve changed it's already the subscribed field, so this is just a rebake + Refresh.
	void OnValidate()
	{
		SetupConstraints();
		BakeAmplitudeLut();
		if (isActiveAndEnabled) Subscribe();
	}
}
