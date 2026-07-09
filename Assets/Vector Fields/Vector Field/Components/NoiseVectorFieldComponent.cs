using UnityEngine;
using UnityX.NoiseSampler;

// Editor-facing wrapper around the code-callable NoiseVectorField generator: holds the sampler settings, detects
// changes, builds the grid->sample matrix from the grid/transform, and dispatches into the base render texture.
[AddComponentMenu("Vector Fields/Noise Vector Field")]
public class NoiseVectorFieldComponent : VectorFieldComponent {
    public enum Space {
        Local,
        World
    }
    public Space space = Space.Local;
    public NoiseSampler noiseSampler = new NoiseSampler();
    public float vortexAngle = 90;
    // When enabled, `magnitude` is auto-set to 1 / (the raw noise field's strongest vector), so the field's peak
    // output length is exactly 1 (the cookie, being a mask, can only attenuate below that). The inspector shows the
    // computed Magnitude value disabled while this is on.
    public bool normalizeMagnitude = false;

    protected override void RenderInternal() {
        EnsureHasValidRenderTexture();
        // Render at unit magnitude — the base applies `magnitude` (and cookie) as an output transform in Render(), so
        // it's scaled once, consistently with every other field type. (Passing `magnitude` here would double-apply it.)
        NoiseVectorField.Dispatch(renderTexture, GridSize, GridToSampleMatrix(), noiseSampler.properties, vortexAngle, 1f);

        // The texture holds the RAW unit-magnitude noise at this point (Render() applies magnitude+cookie after we
        // return, and the reduction is enqueued before that), so this measures the generator's true peak. The
        // readback is async — no stall; the callback writes `magnitude`, the parameter hash notices next tick and
        // re-renders once, and since the raw peak is unchanged it converges immediately. Cost: one 16x16-block GPU
        // reduction + a few-KB readback, and only on renders (which are already change-gated), never per idle frame.
        if (normalizeMagnitude)
            VectorFieldMaxMagnitude.Request(renderTexture, GridSize, max => {
                if (this == null || !normalizeMagnitude || max <= 0f) return;
                magnitude = 1f / max;
            });
    }

    // Maps a grid cell into the space the noise is sampled in. World mode samples in world space (so the field
    // flows past a moving grid); Local mode samples in the grid's own space, offset far away so different fields
    // don't sample the same noise region.
    Matrix4x4 GridToSampleMatrix() {
        if (space == Space.Local)
            return Matrix4x4.Translate(new Vector3(1000f, 0, 0)) * GridToLocalMatrix * Matrix4x4.Translate(noiseSampler.position);
        return GridToWorldMatrix * Matrix4x4.Translate(noiseSampler.position);
    }

    // Re-render when any noise parameter changes. If something animates noiseSampler.position over time, this
    // detects it each frame (so the field updates), and when nothing changes nothing re-renders.
    protected override void CollectParameters(ref System.HashCode hash) {
        base.CollectParameters(ref hash);
        hash.Add((int)space);
        hash.Add(noiseSampler.position);
        hash.Add(vortexAngle);
        hash.Add(noiseSampler.properties);
        hash.Add(normalizeMagnitude);
    }
}
