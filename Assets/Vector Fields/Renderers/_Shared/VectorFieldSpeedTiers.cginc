#ifndef VECTOR_FIELD_SPEED_TIERS_INCLUDED
#define VECTOR_FIELD_SPEED_TIERS_INCLUDED

// Shared speed-tier blending for the flow visualizers: given a normalised speed (0..1) and a SORTED array of tier
// speed positions, find the two tiers that bracket it and the 0..1 weight between them. Each effect then samples its
// own per-tier data (textures via a Texture2DArray, scalar params via float[] uniforms) for tiers `lo` and `hi` and
// lerps by `w`. Keep VF_MAX_TIERS in sync with the C# side (VectorFieldSpeedTiers.MaxTiers).
#define VF_MAX_TIERS 8

// speeds[] must be sorted ascending and have `count` valid entries (count >= 1). Below the first tier clamps to it,
// above the last clamps to it; a single tier gives lo==hi, w==0.
void FindTierBracket(float speed01, float speeds[VF_MAX_TIERS], int count, out int lo, out int hi, out float w) {
    lo = 0;
    for (int k = 0; k < count - 1; k++) {
        if (speed01 >= speeds[k]) lo = k;   // last tier whose position we've passed
    }
    hi = min(lo + 1, count - 1);
    float span = max(speeds[hi] - speeds[lo], 1e-5);
    w = (hi > lo) ? saturate((speed01 - speeds[lo]) / span) : 0.0;
}

#endif
