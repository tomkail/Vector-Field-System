#ifndef VECTOR_FIELD_IBFV_INCLUDED
#define VECTOR_FIELD_IBFV_INCLUDED

// Image-Based Flow Visualization (van Wijk 2002). One feedback-update step: advect the previous accumulation buffer
// along the flow and blend in a little fresh "twinkling" noise. Run this as a fullscreen blit into a ping-pong pair
// (prev → next) each frame; the accumulation converges to seam-free, LIC-like streaks that flow along the field.
// Returns the new grey accumulation value. `time` = elapsed seconds (drives the twinkle).
//
// Reusable core; our demo shader (VectorFieldFlowIBFV.shader) is just this in a fullscreen pass, and the display/colour
// happens separately (VectorFieldFlowIBFVPresent.shader).
float3 VectorFieldIBFVStep(sampler2D prev, sampler2D flowField, sampler2D noise, float2 uv,
                           float flowStep, float noiseAmount, float noiseScale, float noiseRate, float time) {
    // Flow velocity. NOTE: +(rg-0.5) here — OPPOSITE the static-texture visualizers (water/LIC/sand): feedback
    // advection moves the image the other way for a given vel, so this sign makes IBFV streaks travel WITH the field.
    float2 vel = (tex2D(flowField, uv).rg - 0.5);

    // Advect: pull this pixel from where the flow carried it FROM last frame (no global coordinate → no seam).
    float3 advected = tex2D(prev, uv - vel * flowStep).rgb;

    // Twinkling noise — the crux of IBFV. R = per-texel value, G = per-texel temporal phase; each texel pulses on its
    // own phase so a spot persists long enough to be advected into a streak, then fades. Mean-preserving (centred 0.5).
    float2 nz = tex2D(noise, uv * noiseScale).rg;
    float pulse = 0.5 + 0.5 * sin(6.2831853 * (time * noiseRate + nz.g));
    float3 n = (0.5 + (nz.r - 0.5) * pulse).xxx;

    return lerp(advected, n, noiseAmount);
}

#endif
