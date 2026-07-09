using UnityEngine;

// Binds a vector field's live GPU render texture onto a mesh quad. This is the generic adapter every flow shader uses:
// it reads the field's renderTexture directly (no CPU readback) and pushes it into the renderer's material as _MainTex
// (+ _MainTex_TexelSize) via a MaterialPropertyBlock, so it overrides only this renderer's instance — never the shared
// material asset, and never the material you assigned in the inspector.
//
// It sets ONLY _MainTex / _MainTex_TexelSize. Shader-specific inputs live on dedicated subclasses — e.g.
// FlowAlignedTextureRenderer adds the amplitude/colour ramps the Flow-Aligned Texture shader samples. Use this plain
// component for the Water Flow Map, Water Flow Lit, LIC, or your own shaders, which only need the field texture.
// Quad-follows-field alignment (matchFieldBounds / depthOffset) is inherited from VectorFieldQuad.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Texture Renderer")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class VectorFieldTextureRenderer : VectorFieldQuad {
	static readonly int MainTex = Shader.PropertyToID("_MainTex");
	static readonly int MainTexTexelSize = Shader.PropertyToID("_MainTex_TexelSize");

	[SerializeField] VectorFieldComponent _vectorFieldComponent;
	public VectorFieldComponent vectorFieldComponent {
		get => _vectorFieldComponent;
		set {
			if (_vectorFieldComponent == value) return;
			if (isActiveAndEnabled) Unsubscribe();
			_vectorFieldComponent = value;
			if (isActiveAndEnabled) Subscribe();
		}
	}

	protected override VectorFieldComponent Field => _vectorFieldComponent;

	// Optional. Leave empty to use the material already on the MeshRenderer (the common case); assign one to have the
	// script drive the renderer's material too.
	[SerializeField] Material materialPrefab;

	MaterialPropertyBlock propertyBlock;

	protected virtual void OnEnable() {
		Subscribe();
	}

	protected virtual void OnDisable() {
		Unsubscribe();
	}

	void Subscribe() {
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnRendered += BindTexture;
		BindTexture(); // pick up whatever has already been rendered
	}

	void Unsubscribe() {
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnRendered -= BindTexture;
	}

	// Point the material at the field's live render texture. Driven by OnRendered, since that's when the texture (and
	// its reference, after a resize/recreate) can change. Marked protected so subclasses can force a re-bind after
	// changing their own shader inputs.
	protected void BindTexture() {
		if (_vectorFieldComponent == null) return;

		if (materialPrefab != null && meshRenderer.sharedMaterial != materialPrefab)
			meshRenderer.sharedMaterial = materialPrefab;

		var fieldTexture = _vectorFieldComponent.renderTexture;
		if (fieldTexture == null) return; // nothing rendered yet; OnRendered will call us again once it has

		VectorFieldRendererUtils.EditPropertyBlock(meshRenderer, ref propertyBlock, pb => {
			pb.SetTexture(MainTex, fieldTexture);
			// Bicubic field sampling in the shader needs the field dimensions; set explicitly so we don't rely on Unity
			// auto-populating _MainTex_TexelSize for a property-block-bound texture.
			pb.SetVector(MainTexTexelSize, new Vector4(
				1f / fieldTexture.width, 1f / fieldTexture.height, fieldTexture.width, fieldTexture.height));
			ConfigurePropertyBlock(pb);
		});

		MatchFieldBounds();
	}

	// Hook for subclasses to add their shader-specific inputs to the same property block (baked in the same get/set
	// round-trip as _MainTex). The base binder sets nothing here.
	protected virtual void ConfigurePropertyBlock(MaterialPropertyBlock block) { }

#if UNITY_EDITOR
	protected virtual void OnValidate() {
		if (isActiveAndEnabled) BindTexture();
	}
#endif
}
