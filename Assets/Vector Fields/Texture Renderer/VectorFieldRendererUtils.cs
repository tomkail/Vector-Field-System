using System;
using UnityEngine;

// Shared helper for renderers that lay a unit quad (1x1 in local space, +Z = its normal) exactly over a vector field's
// rendered rect — same position, plane orientation, and world size as the field. Both VectorFieldTextureRenderer and
// VectorFieldFlowIBFV used to carry their own near-identical copy of this; keep it here so they can't drift apart.
public static class VectorFieldRendererUtils {

    // Position, orient, and scale `target` so a unit quad overlays `field`. Works whether or not `target` is a child of
    // the field (or of any other scaled/rotated transform). `depthOffset` shifts it along the field's plane normal
    // (draw-order control).
    public static void MatchFieldRect(Transform target, VectorFieldComponent field, float depthOffset = 0f) {
        if (target == null || field == null) return;
        var grid = field.GridSize;
        if (grid.x < 1 || grid.y < 1) return; // grid renderer not ready yet

        // Orientation + position from the field. Matching the field's rotation means that when `target` is parented to
        // the field the local rotation is identity, so the world-scale solve below is exact (and it lies in-plane when
        // the field is tilted, instead of staying axis-aligned).
        target.rotation = field.transform.rotation;
        target.position = field.GetBounds().center + field.planeNormal * depthOffset;

        // World size from the grid->world mapping, not the AABB: this is exactly what the field renders, and it's
        // plane-agnostic. Reading two hard-coded axes off the world-space AABB (the old approach) collapsed to zero on
        // any plane other than XY.
        var m = field.GridToWorldMatrix;
        float worldW = ((Vector3)m.GetColumn(0)).magnitude * grid.x;
        float worldH = ((Vector3)m.GetColumn(1)).magnitude * grid.y;
        SetWorldScale(target, new Vector3(worldW, worldH, 1f));
    }

    // Bind a single texture on a renderer via its MaterialPropertyBlock (overrides only this instance, never the shared
    // material asset). Allocation-free once `block` exists, so it's fine to call every frame.
    public static void SetRendererTexture(Renderer renderer, ref MaterialPropertyBlock block, int propertyId, Texture texture) {
        if (renderer == null) return;
        block ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetTexture(propertyId, texture);
        renderer.SetPropertyBlock(block);
    }

    // Set several properties on a renderer's MaterialPropertyBlock in one get/edit/set round-trip. Prefer
    // SetRendererTexture on hot paths — `edit` is a closure and may allocate.
    public static void EditPropertyBlock(Renderer renderer, ref MaterialPropertyBlock block, Action<MaterialPropertyBlock> edit) {
        if (renderer == null) return;
        block ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        edit(block);
        renderer.SetPropertyBlock(block);
    }

    // Lazily create `material` from `shader` (no-op if already set, or if the shader is missing). Consolidates the
    // "spin up a throwaway material for a known shader" idiom scattered across the renderers, combiner, and editors.
    public static Material GetOrCreateMaterial(ref Material material, Shader shader, bool hideAndDontSave = false) {
        if (material == null && shader != null) {
            material = new Material(shader);
            if (hideAndDontSave) material.hideFlags = HideFlags.HideAndDontSave;
        }
        return material;
    }

    // Convenience overload that resolves the shader by name via Shader.Find.
    public static Material GetOrCreateMaterial(ref Material material, string shaderName, bool hideAndDontSave = false)
        => GetOrCreateMaterial(ref material, Shader.Find(shaderName), hideAndDontSave);

    // Set `target`'s effective (lossy) scale, compensating for whatever the parent chain contributes. Reading lossyScale
    // at localScale = 1 folds in the parent, so this is correct for a child of the field (the case the old per-renderer
    // code got subtly wrong) as well as for an unparented quad.
    static void SetWorldScale(Transform target, Vector3 worldScale) {
        if (target.parent == null) {
            target.localScale = worldScale;
            return;
        }
        target.localScale = Vector3.one;
        var lossy = target.lossyScale;
        target.localScale = new Vector3(
            Mathf.Approximately(lossy.x, 0f) ? worldScale.x : worldScale.x / lossy.x,
            Mathf.Approximately(lossy.y, 0f) ? worldScale.y : worldScale.y / lossy.y,
            Mathf.Approximately(lossy.z, 0f) ? worldScale.z : worldScale.z / lossy.z);
    }
}
