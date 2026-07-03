using System;
using System.Collections.Generic;
using UnityEngine;

// Code-callable vector field combiner. Blends a stack of vector field layers into a target render texture on the
// GPU, with no dependency on a MonoBehaviour: give it the target, the destination ("group") transform, and the
// layers (each a render texture + its world transform + blend settings). GroupVectorFieldComponent is a thin
// wrapper over this — the same combine can be driven entirely from code.
public static class VectorFieldCombiner {
	public enum BlendMode {
		// Add to current value
		Add,
		// Lerp between current and new value based on strength
		Blend
	}

	[Flags]
	public enum Component {
		None = 0,
		// Which aspects of the incoming vector affect the result. These are independent bits, so they compose:
		// Magnitude | Direction == All.
		Magnitude = 1 << 0,
		Direction = 1 << 1,
		All = Magnitude | Direction,
	}

	public struct Layer {
		public RenderTexture field;
		public Matrix4x4 localToWorldMatrix;
		public float strength;
		public BlendMode blendMode;
		public Component components;
		// Pre-baked alignment ramp (alignment 0..1 -> strength multiplier); null = no alignment weighting.
		public Texture2D alignmentRamp;
		public bool scaleByFieldMagnitude;
	}

	static Shader combineVectorFieldsShader;
	public static Shader CombineVectorFieldsShader => combineVectorFieldsShader ? combineVectorFieldsShader : (combineVectorFieldsShader = Resources.Load<Shader>("CombineVectorFields"));

	// One material shared across every blit — blends are serial on the main thread and every property/keyword is set
	// per blit, so a single reused material is safe and avoids allocating + destroying a Material per layer per render.
	static Material sharedMaterial;
	static Material SharedMaterial {
		get {
			VectorFieldRendererUtils.GetOrCreateMaterial(ref sharedMaterial, CombineVectorFieldsShader);
			return sharedMaterial;
		}
	}

	// Blends `layers` (bottom to top) into `target`. `groupLocalToWorld` is the destination field's transform; each
	// layer is sampled/oriented relative to it. `gridSize` sizes the intermediate ping-pong buffers. Layers with no
	// field or Component.None are skipped.
	public static void Combine(RenderTexture target, Vector2Int gridSize, Matrix4x4 groupLocalToWorld, IReadOnlyList<Layer> layers) {
		if (target == null || layers == null || CombineVectorFieldsShader == null) return;
		if (gridSize.x <= 0 || gridSize.y <= 0) return;
		// No early-out on an empty list: an empty (or all-skipped) combine still produces a defined zero field.

		RenderTexture currentRT = RenderTexture.GetTemporary(gridSize.x, gridSize.y, 0, RenderTextureFormat.ARGBFloat);
		RenderTexture nextRT = RenderTexture.GetTemporary(gridSize.x, gridSize.y, 0, RenderTextureFormat.ARGBFloat);

		// Start from a zero field (encoded: vector 0 -> colour (0.5, 0.5, 0, 1)).
		RenderTexture.active = currentRT;
		GL.Clear(true, true, new Color(0.5f, 0.5f, 0, 1));
		RenderTexture.active = null;

		for (int i = 0; i < layers.Count; i++) {
			var layer = layers[i];
			if (layer.field == null || layer.components == Component.None) continue;
			Blend(groupLocalToWorld, currentRT, layer, nextRT);
			// Swap render textures
			(currentRT, nextRT) = (nextRT, currentRT);
		}

		Graphics.Blit(currentRT, target);

		RenderTexture.ReleaseTemporary(currentRT);
		RenderTexture.ReleaseTemporary(nextRT);
	}

	// One layer blit: blends `layer` over `under` into `result`, in the group's frame (groupLocalToWorld).
	public static void Blend(Matrix4x4 groupLocalToWorld, RenderTexture under, Layer layer, RenderTexture result) {
		var material = SharedMaterial;
		if (material == null) return;
		material.SetTexture("_VectorField", layer.field);
		material.SetMatrix("_RelativeTransform", GetRelativeTransform(layer.localToWorldMatrix, groupLocalToWorld));
		// Pure, scale-free rotation taking a direction from the layer's local frame into the group's. Mirrors the CPU
		// path's InverseTransformDirection(TransformDirection(...)) (rotation only); the shader rotates the sampled
		// vector with this and projects onto the group plane. (_RelativeTransform still handles the sample position.)
		var relativeRotation = Quaternion.Inverse(groupLocalToWorld.rotation) * layer.localToWorldMatrix.rotation;
		material.SetMatrix("_VectorRotation", Matrix4x4.Rotate(relativeRotation));
		material.SetFloat("_Strength", layer.strength);
		material.SetInt("_BlendMode", (int)layer.blendMode);
		// Pass the component flags as a bitmask (Magnitude = 1, Direction = 2, All = 3); the shader checks the bits.
		material.SetInt("_Components", (int)layer.components);

		// The alignment-ramp / field-magnitude modulation is keyword-gated so an unmodulated layer compiles it out
		// entirely. Enable it only when one of the two is actually in use.
		bool usesAlignment = layer.alignmentRamp != null;
		bool modulate = usesAlignment || layer.scaleByFieldMagnitude;
		if (modulate) {
			material.EnableKeyword("VF_MODULATION");
			material.SetTexture("_AlignmentRamp", usesAlignment ? layer.alignmentRamp : Texture2D.whiteTexture);
			material.SetInt("_ScaleByFieldMagnitude", layer.scaleByFieldMagnitude ? 1 : 0);
		} else {
			material.DisableKeyword("VF_MODULATION");
		}

		Graphics.Blit(under, result, material);
	}

	// Affine map group-UV -> layer-UV, applied in the shader as mul(M, float4(uv, 0, 1)).xy. Each group cell is
	// orthographically projected onto the layer's plane ALONG THE GROUP'S NORMAL (not read as oblique coordinates),
	// so a layer tilted out of plane covers the foreshortened projection of its bounds — it shrinks as it tilts,
	// matching what you see, instead of growing. (The earlier brush.inverse * canvas read stretched it by 1/cos θ.)
	static Matrix4x4 GetRelativeTransform(Matrix4x4 brushMatrix4x4, Matrix4x4 canvasMatrix4x4) {
		// Work entirely in the GROUP's local space (coordinates ~[-0.5, 0.5]) so the projection stays well-conditioned
		// regardless of how large the fields are, how they're scaled, or where they sit in the world. relLayer maps
		// layer-local -> group-local; the group plane is z = 0 and its normal is +z here.
		Matrix4x4 relLayer = canvasMatrix4x4.inverse * brushMatrix4x4;
		Matrix4x4 groupToLayer = relLayer.inverse;
		Vector3 layerOrigin = relLayer.GetColumn(3);                              // layer centre, in group-local
		// A normal transforms by the inverse-transpose, so this stays the true plane normal even under non-uniform
		// scale/shear (whereas transforming the forward axis directly would not).
		Vector3 layerNormal = groupToLayer.transpose.MultiplyVector(Vector3.forward).normalized;

		float denom = layerNormal.z; // dot((0,0,1), layerNormal)
		// Near edge-on the layer projects to ~a line (no area); map everything outside [0,1] so it contributes nothing.
		if (Mathf.Abs(denom) < 1e-4f) {
			var degenerate = Matrix4x4.identity;
			degenerate.SetColumn(3, new Vector4(10, 10, 0, 1));
			return degenerate;
		}

		// Project a group-UV corner (on the group plane) onto the layer plane along the group normal (+z), then
		// express it as a layer UV.
		Vector2 ProjectCorner(float u, float v) {
			Vector3 p = new Vector3(u - 0.5f, v - 0.5f, 0);
			float t = Vector3.Dot(layerOrigin - p, layerNormal) / denom;
			Vector3 onLayer = new Vector3(p.x, p.y, t); // p + (0,0,1) * t
			Vector3 layerObject = groupToLayer.MultiplyPoint3x4(onLayer);
			return new Vector2(layerObject.x + 0.5f, layerObject.y + 0.5f);
		}

		// The projection between two planes is affine, so three corners define it exactly.
		Vector2 a = ProjectCorner(0, 0);
		Vector2 b = ProjectCorner(1, 0);
		Vector2 c = ProjectCorner(0, 1);

		var m = Matrix4x4.identity;
		m.SetRow(0, new Vector4(b.x - a.x, c.x - a.x, 0, a.x));
		m.SetRow(1, new Vector4(b.y - a.y, c.y - a.y, 0, a.y));
		return m;
	}
}
