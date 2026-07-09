#ifndef VECTOR_FIELD_LIC_INCLUDED
#define VECTOR_FIELD_LIC_INCLUDED

// Line Integral Convolution (Cabral & Leedom 1993). For a pixel at `uv`, walk a short streamline BOTH ways along the
// flow (sampled from `flowField`, RG-encoded as v*0.5+0.5) and average a tiling white `noise` texture along it. The
// noise smears ALONG the flow but stays sharp ACROSS it → dense hair-like streaks combed along the field lines.
// Returns the 0..1 streak value. `phase` (advance 0..1 over time) animates the streaks along the flow.
//
// This is the reusable algorithm; our demo shader (VectorFieldLIC.shader) wraps it with the shared FlowColor styling.

// Integrate one direction (dir = +1 forward, -1 back) from `uv`, accumulating weighted noise samples. Internal helper.
void VectorFieldLIC_Integrate(sampler2D flowField, sampler2D noise, float2 uv, float dir, int steps, float stepLen,
                              float noiseScale, float phase, inout float acc, inout float wsum) {
    float2 p = uv;
    for (int s = 1; s <= steps; s++) {
        // Signed flow, negated so streaks animate WITH the field on screen (convention shared with the water map).
        float2 v = -1.0 * (tex2D(flowField, p).rg - 0.5);
        float len = max(length(v), 1e-5);
        p += dir * (v / len) * stepLen;                                     // unit-speed march → uniform spacing
        float t = (float)s / steps;
        float w = (0.5 + 0.5 * cos(6.2831853 * (t - dir * phase))) * (1.0 - t); // moving bump, tapered with distance
        acc  += w * tex2D(noise, p * noiseScale).r;
        wsum += w;
    }
}

// Full LIC streak value at `uv` (both directions + the centre sample).
float VectorFieldLIC(sampler2D flowField, sampler2D noise, float2 uv, int steps, float stepLen, float noiseScale, float phase) {
    float acc = tex2D(noise, uv * noiseScale).r;   // centre sample
    float wsum = 1.0;
    VectorFieldLIC_Integrate(flowField, noise, uv, +1.0, steps, stepLen, noiseScale, phase, acc, wsum);
    VectorFieldLIC_Integrate(flowField, noise, uv, -1.0, steps, stepLen, noiseScale, phase, acc, wsum);
    return acc / max(wsum, 1e-5);
}

#endif
