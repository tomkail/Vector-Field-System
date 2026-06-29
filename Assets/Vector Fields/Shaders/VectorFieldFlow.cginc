uniform sampler2D _MainTex;
// Texel size of the flow field (1/w, 1/h, w, h). Set by VectorFieldTextureRenderer alongside _MainTex (Unity also
// auto-populates this for textures bound via material/property block). Needed for the bicubic field sample below.
uniform float4 _MainTex_TexelSize;
uniform sampler2D _Tex;
uniform float _AnimationTime;
uniform float4 _Rect;
uniform float _GridCellCount;
uniform float _Speed;
uniform float _TextureScale;
uniform float _Brightness;
// When 1, the streaks take their RGB from _Tex; when 0, streaks are recolored through _ColorGradient.
uniform float _UseTextureColor;
// Recolor ramp for the non-textured path: maps a 0..1 source value to an RGB color. Baked from a Gradient by
// VectorFieldTextureRenderer. Default white reproduces the plain white-streak look.
uniform sampler2D _ColorGradient;
// What drives the gradient lookup: 0 = flow magnitude (speed), 1 = streak luminance (gradient-map the texture detail).
uniform float _GradientSource;
// Rotates the sampled _Tex frame by k*90 degrees: 0 = 0, 1 = 90, 2 = 180, 3 = 270.
uniform float _TextureRotation;
// Flow sampling mode: 0 = legacy four-corner cell blend (shows the per-cell seam), 1 = continuous single sample
// (no seam, but loses the multi-directional woven look), 2 = continuous multi-sample (4-direction blend on a
// floor-free sliding stencil — aims to keep the look without the seam).
uniform float _FlowSamplingMode;
// Diagnostic: 0 = normal, otherwise output one intermediate term as opaque grayscale to localise the seam.
uniform float _DebugView;
// Supersampling level: 0 = off (1 sample), 1 = 2x2, 2 = 4x4. Integrates several sub-pixel evaluations per fragment to
// anti-alias the streak's per-cell directional quantisation (intrinsic to the legacy look) without altering it.
uniform float _SupersampleLevel;
// Maps flow magnitude (x in 0..1) to an alpha multiplier (r). Baked from an AnimationCurve by VectorFieldTextureRenderer.
uniform sampler2D _AmplitudeRamp;

// TODO: Pull out to varying
float2 fragGridCellFrac;

float2 cellSizeNorm;
float2 midGrid;

// Cubic B-spline weights for the four taps straddling a sample position (fractional offset v in [0,1]).
float4 cubicWeights(float v) {
    float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
    float4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return float4(x, y, z, w) * (1.0 / 6.0);
}

// Bicubic (cubic B-spline) sample of a bilinear-filtered texture, done in four bilinear taps (Sigg & Hadwiger).
// The flow field is only C0 under bilinear filtering, so its gradient kinks at every field texel boundary; multiplied
// by the large fragVec lever arm in the streak UV, those kinks show up as creases on the field grid. A C2-continuous
// B-spline sample removes them. texelSize is (1/w, 1/h, w, h).
float4 sampleBicubic(sampler2D tex, float2 uv, float4 texelSize) {
    float2 texSize = texelSize.zw;
    float2 invTexSize = texelSize.xy;

    float2 tc = uv * texSize - 0.5;
    float2 fxy = frac(tc);
    tc -= fxy;

    float4 xcubic = cubicWeights(fxy.x);
    float4 ycubic = cubicWeights(fxy.y);

    float4 c = tc.xxyy + float4(-0.5, 1.5, -0.5, 1.5);
    float4 s = float4(xcubic.x + xcubic.y, xcubic.z + xcubic.w, ycubic.x + ycubic.y, ycubic.z + ycubic.w);
    float4 offset = c + float4(xcubic.y, xcubic.w, ycubic.y, ycubic.w) / s;
    offset *= invTexSize.xxyy;

    float4 sample0 = tex2D(tex, offset.xz);
    float4 sample1 = tex2D(tex, offset.yz);
    float4 sample2 = tex2D(tex, offset.xw);
    float4 sample3 = tex2D(tex, offset.yw);

    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);

    return lerp(lerp(sample3, sample2, sx), lerp(sample1, sample0, sx), sy);
}

// Quintic smootherstep (Perlin). Unlike smoothstep it has zero FIRST AND SECOND derivative at t=0 and t=1, so blending
// adjacent tiles with it is C2-continuous across cell boundaries. smoothstep is only C1 there; its second-derivative
// jump shows up as a perceptual ~1px line on every cell edge (independent of zoom, scaling with _GridCellCount).
float smootherstep(float t) {
    t = saturate(t);
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// Rotate a UV by a multiple of 90 degrees (steps in {0,1,2,3}) about the origin. Used to reorient the sampled _Tex
// frame relative to flow.
float2 rotateUV90(float2 uv, float steps) {
    if (steps < 0.5) return uv;                 // 0
    if (steps < 1.5) return float2(-uv.y, uv.x); // 90
    if (steps < 2.5) return -uv;                 // 180
    return float2(uv.y, -uv.x);                  // 270
}

// Streak colour for one sample point `loc` (in field UV space). Samples the flow direction there and walks the streak
// texture along it. Called once per fragment at the fragment's own position — sampling at discrete cell corners and
// blending is what produced the per-cell seam.
float3 sampleStreakAtPoint(float2 loc)
{
    // Sample the flow direction. Bicubic so the field is smooth across its texel boundaries.
    float2 flowVec  = -1.0 * ( sampleBicubic(_MainTex, loc, _MainTex_TexelSize).rg  - float2(0.5, 0.5) );

    // Find direction and magnitude of this flow. Guard the zero-flow case: normalize(0) is NaN, which would punch
    // black/garbage texels into still regions of the field.
    float flowMag = length(flowVec);
    float2 flowDir = flowMag > 1e-5 ? flowVec / flowMag : float2(1.0, 0.0);
    float2 flowSide = float2(flowDir.y, -flowDir.x);

    // Use UV of exact pixel position to lookup a UV on the fluid texture
    float scalar = 1.0;///200.0;
    float2 fragVec = scalar * (fragGridCellFrac-midGrid);
    float2 fluidTexUV = float2( dot(flowDir,  fragVec), 
                            dot(flowSide, fragVec) );

    // Scroll the UV in the direction of flow on texture (X)
    fluidTexUV.x += flowMag * _Speed * scalar * _AnimationTime;

    fluidTexUV = fluidTexUV / _TextureScale;

    fluidTexUV /= scalar;

    fluidTexUV = rotateUV90(fluidTexUV, _TextureRotation);

    float3 texPixel = tex2D(_Tex, fluidTexUV).rgb;

    // The animated streak pattern only. Amplitude is no longer baked in here — it now drives alpha separately
    // (see CalculateFrag), so the pattern stays crisp and opacity is a clean function of flow magnitude.
    return _Brightness * texPixel;
}

// Flow magnitude (amplitude) at one sample point, decoded the same way as above.
float sampleAmplitudeAtPoint(float2 loc)
{
    float2 flowVec = -1.0 * ( sampleBicubic(_MainTex, loc, _MainTex_TexelSize).rg - float2(0.5, 0.5) );
    return length(flowVec);
}

float4 CalculateFragSingle(float2 uv) {
    fragGridCellFrac = uv * _GridCellCount;
    midGrid = float2(0.5, 0.5) * _GridCellCount;

    // Amplitude only gates alpha — it carries none of the directional streak look or animation — so sample it
    // continuously in every mode. The legacy four-corner bilinear blend of amplitude showed a per-cell line here (a
    // corner-interpolation artifact in the smooth magnitude field); since the colour is flat white by default, that
    // line bleeds straight into the visible alpha. A single continuous sample is smooth and removes it, with no effect
    // on the streak look.
    float amplitude = sampleAmplitudeAtPoint(uv);

    float3 streak;
    if (_FlowSamplingMode < 0.5) {
        // Mode 0 — legacy four-corner cell blend. Samples the flow at the four cell corners and blends. The corner SET
        // is snapped to the grid via floor() and jumps by one cell at every boundary; that discrete jump in the sample
        // positions is the per-cell seam (it survives any blend smoothing / mip / filter change, which is how we
        // localised it). Kept for comparison and because it defines the original look.
        float2 cellCoordBaseIdx = floor(fragGridCellFrac);
        float2 topLeftNorm = cellCoordBaseIdx / _GridCellCount;
        float2 botRightNorm = (cellCoordBaseIdx + float2(1.0,1.0)) / _GridCellCount;
        float2 topRightNorm = float2( botRightNorm.x, topLeftNorm.y );
        float2 botLeftNorm = float2( topLeftNorm.x, botRightNorm.y );

        float3 texelFromTopLeftTile  = sampleStreakAtPoint(topLeftNorm);
        float3 texelFromBotRightTile = sampleStreakAtPoint(botRightNorm);
        float3 texelFromTopRightTile = sampleStreakAtPoint(topRightNorm);
        float3 texelFromBotLeftTile  = sampleStreakAtPoint(botLeftNorm);

        float2 xyInCell = fragGridCellFrac - cellCoordBaseIdx;
        float smoothX = smootherstep(xyInCell.x);
        float smoothY = smootherstep(xyInCell.y);

        float3 top = lerp(texelFromTopLeftTile, texelFromTopRightTile, smoothX);
        float3 bot = lerp(texelFromBotLeftTile, texelFromBotRightTile, smoothX);
        streak = lerp(top, bot, smoothY);
    } else if (_FlowSamplingMode < 1.5) {
        // Mode 1 — continuous single sample. No floor / cell index / corner set, so uv -> bicubic flow dir ->
        // streak UV -> tex2D is continuous end to end: no seam, but only one flow direction, so the woven look is lost.
        streak = sampleStreakAtPoint(uv);
    } else {
        // Mode 2 — continuous multi-sample. Keeps the four-direction blend that gives the legacy look, but takes the
        // four samples from a stencil CENTRED on the fragment (±half a cell) that slides continuously with uv, instead
        // of snapping to cell corners via floor(). No discrete jump in sample positions -> no seam. Weights are equal
        // (legacy's per-cell varying weights are a function of frac(grid) and would reintroduce the discontinuity).
        float h = 0.5 / _GridCellCount;
        float3 s0 = sampleStreakAtPoint(uv + float2(-h, -h));
        float3 s1 = sampleStreakAtPoint(uv + float2( h, -h));
        float3 s2 = sampleStreakAtPoint(uv + float2(-h,  h));
        float3 s3 = sampleStreakAtPoint(uv + float2( h,  h));
        streak = (s0 + s1 + s2 + s3) * 0.25;
    }

    float streakBrightness = (streak.r + streak.g + streak.b) * 0.33;

    // Gate the streak opacity by flow magnitude through the ramp curve. amplitude = 0.5 * |vector| (see the rg decode
    // above), so 2 * amplitude is the magnitude in 0..1 — that's the ramp's lookup. The curve (its r channel) maps
    // magnitude to an alpha multiplier, so still regions can be driven fully transparent and the rolloff is fully
    // art-directable.
    float magnitude = saturate(2.0 * amplitude);
    float amplitudeAlpha = tex2D(_AmplitudeRamp, float2(magnitude, 0.5)).r;

    // Streak pattern and ramped magnitude drive opacity. Color is either the texture's own RGB (_UseTextureColor),
    // or the recolor gradient sampled by the chosen source. patternLum is the streak intensity with _Brightness
    // factored back out, so it stays in 0..1 for the gradient lookup.
    float patternLum = _Brightness > 1e-5 ? saturate(streakBrightness / _Brightness) : 0.0;
    float gradientT = _GradientSource < 0.5 ? magnitude : patternLum;
    float3 gradientColor = tex2D(_ColorGradient, float2(gradientT, 0.5)).rgb;

    float3 streakColor = lerp(gradientColor, streak, _UseTextureColor);
    float4 pathColor = float4(streakColor, streakBrightness * amplitudeAlpha);

    // Diagnostic: output a single intermediate term as opaque grayscale so we can see which one carries the seam.
    if (_DebugView > 0.5) {
        float dbg = amplitude;                          // 1: field magnitude (bilinear-blended)
        if (_DebugView > 1.5) dbg = streakBrightness;   // 2: blended streak luminance (texture path)
        if (_DebugView > 2.5) dbg = amplitudeAlpha;     // 3: alpha from the amplitude ramp
        if (_DebugView > 3.5) dbg = pathColor.a;        // 4: final alpha (streakBrightness * amplitudeAlpha)
        return float4(dbg, dbg, dbg, 1.0);
    }

    return pathColor;
}

// Entry point. Optionally supersamples: evaluates the per-fragment result at a regular grid of sub-pixel offsets and
// averages. This is the legacy effect integrated over the pixel — it changes nothing about the look or animation, it
// only anti-aliases the cell-boundary line left by the streak's intrinsic per-cell directional quantisation. Sub-pixel
// offsets are derived from the screen-space derivatives of uv, so they track zoom automatically.
float4 CalculateFrag(float2 uv) {
    if (_SupersampleLevel < 0.5) return CalculateFragSingle(uv);

    float2 dx = ddx(uv);
    float2 dy = ddy(uv);

    if (_SupersampleLevel < 1.5) {
        // 2x2 ordered grid: sample centres at ±0.25 px.
        float4 acc = float4(0,0,0,0);
        [unroll] for (int j = 0; j < 2; j++)
            [unroll] for (int i = 0; i < 2; i++) {
                float2 o = (float2(i, j) + 0.5) * 0.5 - 0.5;   // -0.25, +0.25
                acc += CalculateFragSingle(uv + o.x * dx + o.y * dy);
            }
        return acc * 0.25;
    }

    // 4x4 ordered grid.
    float4 acc = float4(0,0,0,0);
    [unroll] for (int j = 0; j < 4; j++)
        [unroll] for (int i = 0; i < 4; i++) {
            float2 o = (float2(i, j) + 0.5) * 0.25 - 0.5;
            acc += CalculateFragSingle(uv + o.x * dx + o.y * dy);
        }
    return acc / 16.0;
}