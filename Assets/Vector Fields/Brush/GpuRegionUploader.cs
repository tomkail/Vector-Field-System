using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace VectorFields {
    // Base-level GPU upload helper shared by the painting targets (the vector field drawing tool and the smoke sim). The
    // painting core reports a small dirty RectInt per paint step, but the naive upload — Texture2D.SetPixels + Apply — re-
    // transfers the WHOLE texture to the GPU every frame (Apply has no partial variant). That full transfer is what makes
    // dragging a brush on a large grid (512x512 ≈ 4 MB/frame) hitch, and why it clears the moment you stop painting.
    //
    // This transfers ONLY the dirty sub-rect: it stages the region in a small reusable Texture2D and Graphics.CopyTexture's
    // just that block onto the destination GPU texture, so the per-frame cost scales with the brush footprint, not the grid
    // size. Both paint targets share it so the optimisation (and any future fix) lives in one place.
    //
    // One instance per destination texture — it owns a staging texture matched to that destination's format. Not thread safe.
    public sealed class GpuRegionUploader {
        Texture2D _staging;

        // Region copies need CopyTextureSupport.Basic (universal on desktop; checked once). When false the caller should
        // fall back to its full-texture upload path — TryUploadRegion returns false so that branch is taken automatically.
        public static bool Supported => (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        // Copy regionColors (length w*h, row-major bottom-up to match Texture2D.SetPixels) into dest at grid origin
        // (dstX, dstY). dest must already hold a complete field (region copies patch it in place). Returns false — leaving
        // dest untouched — if region copies aren't supported or the inputs/bounds are invalid, so the caller can do a full
        // upload instead.
        public bool TryUploadRegion(Color[] regionColors, int w, int h, Texture dest, int dstX, int dstY) {
            if (!Supported || dest == null || regionColors == null || w <= 0 || h <= 0) return false;
            if (regionColors.Length < w * h) return false;
            if (dstX < 0 || dstY < 0 || dstX + w > dest.width || dstY + h > dest.height) return false;

            var fmt = dest.graphicsFormat;
            // Staging must match the destination format for a region CopyTexture. Grow it monotonically and reuse it so
            // steady-state painting doesn't churn GPU textures; a slightly-larger staging just means Apply uploads a few
            // extra (still brush-sized) texels before we copy the exact sub-rect out of it.
            if (_staging == null || _staging.width < w || _staging.height < h || _staging.graphicsFormat != fmt) {
                int sw = _staging != null ? Mathf.Max(w, _staging.width) : w;
                int sh = _staging != null ? Mathf.Max(h, _staging.height) : h;
                if (_staging != null) VectorFieldObjectUtils.DestroyAutomatic(_staging);
                _staging = new Texture2D(sw, sh, fmt, TextureCreationFlags.None) { filterMode = FilterMode.Point };
            }

            _staging.SetPixels(0, 0, w, h, regionColors);
            _staging.Apply(false);
            Graphics.CopyTexture(_staging, 0, 0, 0, 0, w, h, dest, 0, 0, dstX, dstY);
            return true;
        }

        public void Dispose() {
            if (_staging != null) { VectorFieldObjectUtils.DestroyAutomatic(_staging); _staging = null; }
        }
    }
}
