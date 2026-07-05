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
    public NoiseSampler noiseSampler;
    public float vortexAngle = 90;

    protected override void RenderInternal() {
        EnsureHasValidRenderTexture();
        // Render at unit magnitude — the base applies `magnitude` (and cookie) as an output transform in Render(), so
        // it's scaled once, consistently with every other field type. (Passing `magnitude` here would double-apply it.)
        NoiseVectorField.Dispatch(renderTexture, GridSize, GridToSampleMatrix(), noiseSampler.properties, vortexAngle, 1f);
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
    }
}
