#ifndef VECTOR_FIELD_FLOW_COLOR_INCLUDED
#define VECTOR_FIELD_FLOW_COLOR_INCLUDED

// Shared styling for the flow visualizers (Flow-Aligned, LIC, IBFV). All three are driven from their component via
// VectorFieldFlowStyle.Apply(...), so they expose the same knobs and are controlled from script, not the material.
//
// Each shader declares its OWN `_ColorGradient` / `_AmplitudeRamp` sampler2D (so this include never clashes with the
// Flow-Aligned shader's pre-existing declarations of those). This file holds only the shared scalar knobs + helpers.
//
// Colour model: streaks are ALWAYS coloured by SPEED through _ColorGradient, sampled at FlowSpeed01(vel). The streak
// pattern (LIC/IBFV/sand) becomes the coverage/alpha over the background — so speed gives the hue, the pattern gives
// the texture. (A luminance-driven gradient mode may return later; it would add a mode uniform + a second lookup here.)
uniform float4 _BackgroundColor;  // composited under the streaks (rgb + base alpha; alpha 0 = overlay the scene)
uniform float  _Contrast;         // streak contrast expansion about mid-grey (1 = none)
uniform float  _Gamma;            // streak gamma (1 = none)
uniform float  _MaxSpeed;         // |v| that maps to the top of the gradient / amplitude ramps
uniform float  _FlowAlpha;        // overall opacity multiplier

// Contrast about mid-grey, then gamma. streak in 0..1 -> shaped coverage in 0..1.
float FlowContrastGamma(float streak) {
    streak = saturate((streak - 0.5) * _Contrast + 0.5);
    return pow(saturate(streak), _Gamma);
}

// Normalised speed (0..1) for the ramp lookups.
float FlowSpeed01(float2 vel) { return saturate(length(vel) / max(_MaxSpeed, 1e-5)); }

// Composite a streak colour over the background, gating alpha by the amplitude-ramp value and the overall opacity.
// `coverage` is the (already contrast/gamma-shaped) streak visibility in 0..1. Straight-alpha output for a
// SrcAlpha/OneMinusSrcAlpha blend, designed so:
//   _BackgroundColor.a = 0  -> coloured streaks over the scene (streak alpha = coverage*ampAlpha); the RGB is the
//                              streak colour untouched, so this is identity for the legacy transparent-over-scene look.
//   _BackgroundColor.a -> 1 -> streaks over an opaque background colour (still water / dark rock), fully opaque.
float4 FlowCompose(float3 color, float coverage, float ampAlpha) {
    float streakA = saturate(coverage * ampAlpha);
    float bgA = _BackgroundColor.a;
    float3 rgb = lerp(color, _BackgroundColor.rgb, (1.0 - streakA) * bgA);
    float  a   = (streakA + (1.0 - streakA) * bgA) * _FlowAlpha;
    return float4(rgb, a);
}

#endif
