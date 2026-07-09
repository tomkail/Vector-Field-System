#ifndef VECTOR_FIELD_FLOW_MAP_INCLUDED
#define VECTOR_FIELD_FLOW_MAP_INCLUDED

// Core of the Valve "Water Flow" / van Wijk flow map: scroll a texture along a flow vector without the infinite
// smearing of a naive `uv += flow * time`. It pushes UVs by a BOUNDED sawtooth phase (frac of time) that resets each
// cycle, and runs two copies offset by half a cycle, cross-fading so each copy's reset is hidden behind the other's
// mid-cycle. The result flows forever without tearing.
//
// This is the reusable algorithm — a dev can drive their own water shader from just this. Our demo shaders add the
// shared VectorFieldFlowColor styling (and, in the tiered variant, speed tiers) on top.

// The two sample phase offsets + cross-fade weight at `time` for a flow of the given `speed`. Sample the texture at
// `uv - vel*strength*p0` and `uv - vel*strength*p1`, then lerp by `blend`.
void FlowMapPhases(float speed, float time, out float p0, out float p1, out float blend) {
    float t = time * speed;
    p0 = frac(t);
    p1 = frac(t + 0.5);
    blend = abs(1.0 - 2.0 * p0);   // 0 at phase 0, 1 at phase 0.5, 0 at phase 1
}

// Convenience: ping-pong sample `tex` along `vel`. `uv` should already be tiled; `time` = _Time.y.
fixed4 FlowMapSample(sampler2D tex, float2 vel, float2 uv, float strength, float speed, float time) {
    float p0, p1, blend;
    FlowMapPhases(speed, time, p0, p1, blend);
    fixed4 c0 = tex2D(tex, uv - vel * strength * p0);
    fixed4 c1 = tex2D(tex, uv - vel * strength * p1);
    return lerp(c0, c1, blend);
}

#endif
