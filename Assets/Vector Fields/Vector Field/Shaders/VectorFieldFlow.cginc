uniform sampler2D _MainTex;
uniform sampler2D _Tex;
uniform float _AnimationTime;
uniform float4 _Rect;
uniform float _GridCellCount;
uniform float _Speed;
uniform float _TextureScale;
uniform float _Brightness;

// TODO: Pull out to varying
float2 fragGridCellFrac;

float2 cellSizeNorm;
float2 midGrid;

float3 getTexelFromTileWithCentre(float2 loc)
{
    // Sample the flow from the centre of the tile
    float2 flowVec  = -1.0 * ( tex2D(_MainTex, loc).rg  - float2(0.5, 0.5) );

    // Find direction and magnitude of this flow
    float flowMag = length(flowVec);
    float2 flowDir = normalize(flowVec);
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

    // Darken where the flow slows down
    return _Brightness * flowMag * texPixel;
}

float3 getTexelStrengthFromTileWithCentre(float2 loc)
            {
                // Sample the flow from the centre of the tile
                float2 flowVec  = -1.0 * ( tex2D(_MainTex, loc).rg  - float2(0.5, 0.5) );

                // Find direction and magnitude of this flow
                float flowMag = length(flowVec);
                return _Brightness * flowMag;
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
    float4 pathColor = float4( lerp(top, bot, smoothY), 1.0);



//    float texelStrengthFromTopLeftTile  = getTexelStrengthFromTileWithCentre(topLeftNorm);
//                float texelStrengthFromBotRightTile = getTexelStrengthFromTileWithCentre(botRightNorm);
//                float texelStrengthFromTopRightTile = getTexelStrengthFromTileWithCentre(topRightNorm);
//                float texelStrengthFromBotLeftTile  = getTexelStrengthFromTileWithCentre(botLeftNorm);

//    float topStrength = lerp(texelStrengthFromTopLeftTile, texelStrengthFromTopRightTile, smoothX);
//	float botStrength = lerp(texelStrengthFromBotLeftTile, texelStrengthFromBotRightTile, smoothX);
//    float pathStrength = lerp(topStrength, botStrength, smoothY);

//    float opacity = pathStrength * (pathStrength);
	float textureBrightness = (pathColor.r+pathColor.g+pathColor.b) * 0.33;
//    pathColor.a = pathStrength;
	pathColor = lerp(float4(1,1,1,0), float4(1,1,1,1), textureBrightness);
//    ;

    return pathColor;
}