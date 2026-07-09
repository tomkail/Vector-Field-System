#include "../_Shared/VectorFieldFlowColor.cginc"   // shared styling: _BackgroundColor/_Contrast/_Gamma/_MaxSpeed/_FlowAlpha + helpers

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
// Streaks are coloured by SPEED through the shared _ColorGradient (see VectorFieldFlowColor.cginc); _UseTextureColor
// tints that speed colour by the streak texture's own RGB. (The former _GradientSource magnitude/luminance selector
// was removed — colouring is always speed-driven now.)
// Rotates the sampled _Tex frame by k*90 degrees: 0 = 0, 1 = 90, 2 = 180, 3 = 270.
uniform float _TextureRotation;
// Flow sampling mode: 0 = Cell Blend (Legacy, has the seam), 1 = Cell Blend Seam Masked (bridge/blur across the seam),
// 2 = Cell Blend Seam Copy (replace seam pixels with the nearest good pixel — no blend). See FLOW_ALIGNED_NOTES.md.
uniform float _FlowSamplingMode;
// Seam-mask (mode 1) band half-width, in screen pixels: how wide a strip around each cell edge gets the bridge.
uniform float _SeamBand;
// Seam-mask (mode 1) reach, in screen pixels: how far across the seam to sample for the bridge. Keep > _SeamBand so
// the bridge samples land in clean cell interiors, clear of the kink.
uniform float _SeamReach;
// 1 = sample amplitude continuously (default; smooth alpha). 0 = legacy four-corner amplitude blend (shows its own
// per-cell seam). Toggle to A/B whether the continuous-amplitude fix is still needed.
uniform float _ContinuousAmplitude;
// Debug: when 1, show whether each seam pixel's mode-2 COPY TARGET is clean. green = clean source (copy works),
// red = target still inside a seam band (reach too small), black = interior (not copied).
uniform float _SeamDebug;
// Maps flow magnitude (x in 0..1) to an alpha multiplier (r). Baked from an AnimationCurve by VectorFieldTextureRenderer.
uniform sampler2D _AmplitudeRamp;

// TODO: Pull out to varying
float2 fragGridCellFrac;
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
// Streak colour for an EXPLICIT constant flow direction. Because flowDir is fixed (not a function of position), the
// streak UV dot(flowDir, fragVec) is a clean linear ramp -> broad, filament-free streaks. This is the building block
// for both the legacy corner blend and the steerable-basis mode; varying the direction *per pixel* instead is what
// turns those broad streaks into fine filaments (the global-position lever arm amplifies dD/dP), see NOTES.
float3 streakForDirection(float2 flowDir, float flowMag)
{
    float2 flowSide = float2(flowDir.y, -flowDir.x);

    float2 fragVec = fragGridCellFrac - midGrid;
    float2 fluidTexUV = float2( dot(flowDir, fragVec), dot(flowSide, fragVec) );

    // Scroll along flow (texture X) over time.
    fluidTexUV.x += flowMag * _Speed * _AnimationTime;

    fluidTexUV /= _TextureScale;
    fluidTexUV = rotateUV90(fluidTexUV, _TextureRotation);

    // Animated streak pattern only; amplitude drives alpha separately in CalculateFrag.
    return _Brightness * tex2D(_Tex, fluidTexUV).rgb;
}

float3 sampleStreakAtPoint(float2 loc)
{
    // Sample the flow direction. Bicubic so the field is smooth across its texel boundaries. Guard the zero-flow case:
    // normalize(0) is NaN, which would punch black/garbage texels into still regions of the field.
    // Orientation here is sign-invariant (the ripple lines only care about the axis); the negate keeps the subtle
    // scroll travelling WITH the field, consistent with the water/LIC visualizers (IBFV uses the opposite sign).
    float2 flowVec  = -1.0 * ( sampleBicubic(_MainTex, loc, _MainTex_TexelSize).rg  - float2(0.5, 0.5) );
    float flowMag = length(flowVec);
    float2 flowDir = flowMag > 1e-5 ? flowVec / flowMag : float2(1.0, 0.0);
    return streakForDirection(flowDir, flowMag);
}

// Flow magnitude (amplitude) at one sample point, decoded the same way as above.
float sampleAmplitudeAtPoint(float2 loc)
{
    float2 flowVec = -1.0 * ( sampleBicubic(_MainTex, loc, _MainTex_TexelSize).rg - float2(0.5, 0.5) );
    return length(flowVec);
}

// Legacy four-corner cell blend: samples the flow at the four cell corners and bilinearly blends the streak lookups.
// Uses the globals fragGridCellFrac / midGrid (set by the caller). This is what defines the woven, cell-locked look —
// and the per-cell direction quantisation it relies on is what leaves the boundary seam.
float3 legacyStreakBlend(float2 uv)
{
    // Set the global the streak UV's fragVec reads from, so this can be evaluated at any (possibly offset) position.
    fragGridCellFrac = uv * _GridCellCount;
    float2 cellCoordBaseIdx = floor(fragGridCellFrac);
    float2 topLeftNorm = cellCoordBaseIdx / _GridCellCount;
    float2 botRightNorm = (cellCoordBaseIdx + float2(1.0,1.0)) / _GridCellCount;
    float2 topRightNorm = float2( botRightNorm.x, topLeftNorm.y );
    float2 botLeftNorm = float2( topLeftNorm.x, botRightNorm.y );

    float3 texelFromTopLeftTile  = sampleStreakAtPoint(topLeftNorm);
    float3 texelFromBotRightTile = sampleStreakAtPoint(botRightNorm);
    float3 texelFromTopRightTile = sampleStreakAtPoint(topRightNorm);
    float3 texelFromBotLeftTile  = sampleStreakAtPoint(botLeftNorm);

    // Plain LINEAR interpolation between the corners. smoothstep/smootherstep weights have zero slope at the cell
    // edges, which flattens the blended surface at every grid line and tiles into a visible quilt of ridges/valleys.
    // Linear has no forced-flat edges, so the corners blend without that per-cell pillowing — same woven look, no quilt.
    float2 xyInCell = fragGridCellFrac - cellCoordBaseIdx;
    float smoothX = xyInCell.x;
    float smoothY = xyInCell.y;

    float3 top = lerp(texelFromTopLeftTile, texelFromTopRightTile, smoothX);
    float3 bot = lerp(texelFromBotLeftTile, texelFromBotRightTile, smoothX);
    return lerp(top, bot, smoothY);
}

float4 CalculateFrag(float2 uv) {
    fragGridCellFrac = uv * _GridCellCount;
    midGrid = float2(0.5, 0.5) * _GridCellCount;

    // Debug: for each seam pixel, is its COPY TARGET (the pixel mode 2 samples) a clean pixel or still on a seam?
    // green = target is clean (copy works), red = target still inside a seam band (reach too small / source dirty),
    // black = interior (not a seam pixel). Uses the exact same target math as mode 2.
    if (_SeamDebug > 0.5) {
        float2 g = uv * _GridCellCount;
        float2 sd = g - floor(g + 0.5);
        float2 cellsPerPx = max(fwidth(g), 1e-5);
        bool inX = abs(sd.x) < _SeamBand * cellsPerPx.x;
        bool inY = abs(sd.y) < _SeamBand * cellsPerPx.y;
        if (!inX && !inY) return float4(0.0, 0.0, 0.0, 1.0);          // interior = black

        float2 targetG = g;                                           // same shift as mode 2
        if (inX) targetG.x = g.x + (sd.x >= 0.0 ? 1.0 : -1.0) * _SeamReach * cellsPerPx.x;
        if (inY) targetG.y = g.y + (sd.y >= 0.0 ? 1.0 : -1.0) * _SeamReach * cellsPerPx.y;

        float2 tSd = targetG - floor(targetG + 0.5);
        bool targetOnSeam = (abs(tSd.x) < _SeamBand * cellsPerPx.x) || (abs(tSd.y) < _SeamBand * cellsPerPx.y);
        return targetOnSeam ? float4(1,0,0,1) : float4(0,1,0,1);      // red = still on a seam, green = clean source
    }

    // Amplitude gates alpha. Sampling it CONTINUOUSLY (default) removes a per-cell line that the legacy four-corner
    // bilinear blend produced in the smooth magnitude field — visible straight through the flat-white alpha. The
    // toggle re-enables the legacy blend so its contribution can be compared.
    float amplitude;
    if (_ContinuousAmplitude > 0.5) {
        amplitude = sampleAmplitudeAtPoint(uv);
    } else {
        // Legacy: bilinear blend of the flow magnitude sampled at the four cell corners (the old amplitude seam).
        float2 cellBase = floor(fragGridCellFrac);
        float2 tl = cellBase / _GridCellCount;
        float2 br = (cellBase + float2(1.0, 1.0)) / _GridCellCount;
        float2 f = fragGridCellFrac - cellBase;
        float aTop = lerp(sampleAmplitudeAtPoint(tl), sampleAmplitudeAtPoint(float2(br.x, tl.y)), f.x);
        float aBot = lerp(sampleAmplitudeAtPoint(float2(tl.x, br.y)), sampleAmplitudeAtPoint(br), f.x);
        amplitude = lerp(aTop, aBot, f.y);
    }

    float3 streak;
    if (_FlowSamplingMode < 0.5) {
        // Mode 0 — Cell Blend (Legacy). The original effect; carries the per-cell seam (see FLOW_ALIGNED_NOTES.md).
        streak = legacyStreakBlend(uv);
    } else if (_FlowSamplingMode < 1.5) {
        // Mode 1 — Cell Blend, Seam Masked. The legacy effect everywhere, but in a thin band straddling each cell edge
        // we COVER the seam: sample the legacy streak reached ACROSS the seam into each neighbouring cell's interior
        // (perpendicular to that edge) and bridge between them. Because the reach clears the kink, the bridge is an
        // interpolation of two clean interior values — it paints over the seam with its own neighbours. The seam is
        // intrinsic to the legacy algorithm (orienting an anisotropic texture over rotational flow — see NOTES), so
        // this masks it, it does not remove it. Knobs: _SeamBand (mask width, px), _SeamReach (reach across, px).
        float2 g = uv * _GridCellCount;
        float2 fr = frac(g);
        float2 sdCells = fr - step(0.5, fr);                 // SIGNED dist to nearest cell edge (cell units, -0.5..0.5)
        float2 px = max(fwidth(g), 1e-5);
        float2 sdPx = sdCells / px;                          // signed dist in screen px (<0 one side, >0 the other)

        // Derivatives must be taken in uniform control flow.
        float2 dpx = ddx(uv);
        float2 dpy = ddy(uv);

        float3 result = legacyStreakBlend(uv);

        // Vertical seam (near an x-edge) -> bridge horizontally. Position-weighted: a pixel left of the seam reads the
        // clean LEFT interior, one to the right reads the clean RIGHT interior, and only the exact centre line averages
        // the two. So contrast is preserved across the band (no flat 50/50 dimming) — the average that greys out the
        // streaks happens on one line, not the whole strip.
        // Anchor the two bridge samples to the SEAM (hop to the edge via -sdPx first, then reach out), NOT to the
        // current pixel. That way both always land _SeamReach px into the clean interiors, clear of the kink, for
        // EVERY pixel in the band. Sampling relative to the pixel let off-centre band pixels pull one sample back onto
        // the line — re-including the very seam we're masking. Keep _SeamReach > _SeamBand so the anchors clear the band.
        float wv = 1.0 - smoothstep(0.0, _SeamBand, abs(sdPx.x));
        if (wv > 0.001) {
            float3 leftS  = legacyStreakBlend(uv - dpx * (sdPx.x + _SeamReach));
            float3 rightS = legacyStreakBlend(uv - dpx * (sdPx.x - _SeamReach));
            float t = saturate(0.5 + 0.5 * sdPx.x / max(_SeamReach, 1e-3));
            result = lerp(result, lerp(leftS, rightS, t), wv);
        }
        // Horizontal seam (near a y-edge) -> bridge vertically, same scheme.
        float wh = 1.0 - smoothstep(0.0, _SeamBand, abs(sdPx.y));
        if (wh > 0.001) {
            float3 downS = legacyStreakBlend(uv - dpy * (sdPx.y + _SeamReach));
            float3 upS   = legacyStreakBlend(uv - dpy * (sdPx.y - _SeamReach));
            float t = saturate(0.5 + 0.5 * sdPx.y / max(_SeamReach, 1e-3));
            result = lerp(result, lerp(downS, upS, t), wh);
        }
        streak = result;
    } else {
        // Mode 2 — Cell Blend, Seam Copy (experiment). Instead of blending across the seam, COPY the nearest good
        // (non-seam) pixel: for a seam-band pixel, sample the legacy streak offset to just past the band edge in the
        // escape direction (diagonal in corners). No averaging -> no orientation ghost; the tradeoff is a hard ~1px
        // changeover at the exact seam centre, where the escape sign flips and adjacent pixels copy from opposite sides.
        // Each seam pixel samples relative to ITS OWN position (parallel outward shift of _SeamReach px on its own
        // side), so the band is a shifted copy of the adjacent content, not a flat distant column. _SeamBand selects
        // seam pixels; _SeamReach is the shift. Never-zero sign so a pixel exactly on the boundary still picks a side.
        float2 g = uv * _GridCellCount;
        float2 sd = g - floor(g + 0.5);                      // signed dist to nearest edge (cell units)
        float2 cellsPerPx = max(fwidth(g), 1e-5);            // cell units per screen px

        float2 targetG = g;
        if (abs(sd.x) < _SeamBand * cellsPerPx.x) targetG.x = g.x + (sd.x >= 0.0 ? 1.0 : -1.0) * _SeamReach * cellsPerPx.x;
        if (abs(sd.y) < _SeamBand * cellsPerPx.y) targetG.y = g.y + (sd.y >= 0.0 ? 1.0 : -1.0) * _SeamReach * cellsPerPx.y;

        float2 targetUv = targetG / _GridCellCount;
        streak = legacyStreakBlend(targetUv);
        amplitude = sampleAmplitudeAtPoint(targetUv);        // copy amplitude too, so alpha matches the neighbour
    }

    float streakBrightness = (streak.r + streak.g + streak.b) * 0.33;

    // Gate the streak opacity by flow magnitude through the ramp curve. amplitude = 0.5 * |vector| (see the rg decode
    // above), so 2 * amplitude is the magnitude in 0..1 — that's the ramp's lookup. The curve (its r channel) maps
    // magnitude to an alpha multiplier, so still regions can be driven fully transparent and the rolloff is fully
    // art-directable.
    float magnitude = saturate(2.0 * amplitude);
    float amplitudeAlpha = tex2D(_AmplitudeRamp, float2(magnitude, 0.5)).r;

    // Colour by SPEED (always) through the shared gradient. 2*amplitude = |vector|, matching FlowSpeed01 elsewhere.
    // _UseTextureColor tints that speed colour by the streak texture's own RGB (normalised by _Brightness so it
    // modulates hue, not intensity — intensity is the coverage below). Off = pure speed colour.
    float speed01 = saturate(2.0 * amplitude / _MaxSpeed);
    float3 speedColor = tex2D(_ColorGradient, float2(speed01, 0.5)).rgb;
    float3 streakRGB = _Brightness > 1e-5 ? streak / _Brightness : streak;
    float3 baseColor = lerp(speedColor, speedColor * streakRGB, _UseTextureColor);

    // Shared styling: contrast/gamma shape the coverage, then composite over the background at the shared opacity.
    // Identity at defaults (contrast 1, gamma 1, background alpha 0, opacity 1) -> transparent-over-scene with
    // alpha = streakBrightness * amplitudeAlpha.
    float coverage = FlowContrastGamma(saturate(streakBrightness));
    return FlowCompose(baseColor, coverage, amplitudeAlpha);
}