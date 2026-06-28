uniform sampler2D _MainTex;
uniform sampler2D _Tex;
uniform float _AnimationTime;
uniform float4 _Rect;
uniform float _GridCellCount;
uniform float _Speed;
uniform float _TextureScale;
uniform float _Brightness;
// Maps flow magnitude (x in 0..1) to an alpha multiplier (r). Baked from an AnimationCurve by VectorFieldTextureRenderer.
uniform sampler2D _AmplitudeRamp;

// TODO: Pull out to varying
float2 fragGridCellFrac;

float2 cellSizeNorm;
float2 midGrid;

float3 getTexelFromTileWithCentre(float2 loc)
{
    // Sample the flow from the centre of the tile
    float2 flowVec  = -1.0 * ( tex2D(_MainTex, loc).rg  - float2(0.5, 0.5) );

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

    float3 texPixel = tex2D(_Tex, fluidTexUV).rgb;

    // The animated streak pattern only. Amplitude is no longer baked in here — it now drives alpha separately
    // (see CalculateFrag), so the pattern stays crisp and opacity is a clean function of flow magnitude.
    return _Brightness * texPixel;
}

// Flow magnitude (amplitude) at the centre of the tile, decoded the same way as above.
float getAmplitudeFromTileWithCentre(float2 loc)
{
    float2 flowVec = -1.0 * ( tex2D(_MainTex, loc).rg - float2(0.5, 0.5) );
    return length(flowVec);
}

float4 CalculateFrag(float2 uv) {
	cellSizeNorm = float2(1.0,1.0) / _GridCellCount;

    fragGridCellFrac = uv * _GridCellCount;

    float2 cellCoordBaseIdx = floor(fragGridCellFrac);
    midGrid = float2(0.5, 0.5) * _GridCellCount;

    float2 topLeftNorm = cellCoordBaseIdx / _GridCellCount;
    float2 botRightNorm = (cellCoordBaseIdx + float2(1.0,1.0)) / _GridCellCount;
    float2 topRightNorm = float2( botRightNorm.x, topLeftNorm.y );
    float2 botLeftNorm = float2( topLeftNorm.x, botRightNorm.y );

    float3 texelFromTopLeftTile  = getTexelFromTileWithCentre(topLeftNorm);
    float3 texelFromBotRightTile = getTexelFromTileWithCentre(botRightNorm);
    float3 texelFromTopRightTile = getTexelFromTileWithCentre(topRightNorm);
    float3 texelFromBotLeftTile  = getTexelFromTileWithCentre(botLeftNorm);

    float2 xyInCell = fragGridCellFrac - cellCoordBaseIdx;

    float smoothX = smoothstep(0.0, 1.0, xyInCell.x);
    float smoothY = smoothstep(0.0, 1.0, xyInCell.y);

    float3 top = lerp(texelFromTopLeftTile, texelFromTopRightTile, smoothX);
    float3 bot = lerp(texelFromBotLeftTile, texelFromBotRightTile, smoothX);
    float3 streak = lerp(top, bot, smoothY);

    // Bilinearly interpolate the flow amplitude across the same four tile centres, so alpha is a smooth function of
    // magnitude rather than stepping per cell.
    float ampTopLeft  = getAmplitudeFromTileWithCentre(topLeftNorm);
    float ampBotRight = getAmplitudeFromTileWithCentre(botRightNorm);
    float ampTopRight = getAmplitudeFromTileWithCentre(topRightNorm);
    float ampBotLeft  = getAmplitudeFromTileWithCentre(botLeftNorm);
    float ampTop = lerp(ampTopLeft, ampTopRight, smoothX);
    float ampBot = lerp(ampBotLeft, ampBotRight, smoothX);
    float amplitude = lerp(ampTop, ampBot, smoothY);

    float streakBrightness = (streak.r + streak.g + streak.b) * 0.33;

    // Gate the streak opacity by flow magnitude through the ramp curve. amplitude = 0.5 * |vector| (see the rg decode
    // above), so 2 * amplitude is the magnitude in 0..1 — that's the ramp's lookup. The curve (its r channel) maps
    // magnitude to an alpha multiplier, so still regions can be driven fully transparent and the rolloff is fully
    // art-directable.
    float magnitude = saturate(2.0 * amplitude);
    float amplitudeAlpha = tex2D(_AmplitudeRamp, float2(magnitude, 0.5)).r;

    // White streaks; the streak pattern and the ramped magnitude drive opacity.
    float4 pathColor = float4(1.0, 1.0, 1.0, streakBrightness * amplitudeAlpha);

    return pathColor;
}