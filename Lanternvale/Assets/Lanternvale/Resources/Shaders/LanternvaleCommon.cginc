// Lanternvale stylized lighting, shared by every world shader.
//
// The game does its own lighting (no Unity light components, no pipeline-specific includes), so the same shaders run
// unchanged under the built-in pipeline and any SRP and the look is fully authored: a soft wrapped sun, a sky/ground
// hemisphere ambient, up to 16 point lights (lanterns, fires, spells), a warm rim, distance fog (atmospheric
// perspective) and emissive vertex colours that glow at night. The values are shader globals pushed every frame by
// Lanternvale.Game.SceneLighting.
//
// World convention: the ground is the XY plane, UP IS -Z (see World3D.cs).
#ifndef LANTERNVALE_COMMON_INCLUDED
#define LANTERNVALE_COMMON_INCLUDED

#include "UnityCG.cginc"

#define LV_MAX_LIGHTS 16

float4 _LV_SunDir;        // xyz: unit direction from the surface TO the sun
float4 _LV_SunColor;      // rgb: colour * intensity
float4 _LV_SkyAmb;        // rgb: ambient from above
float4 _LV_GroundAmb;     // rgb: ambient from below
float4 _LV_FogColor;      // rgb: fog colour, a: maximum fog amount
float4 _LV_FogParams;     // x: start distance, y: end distance
float4 _LV_RimColor;      // rgb: rim colour, a: rim power
float4 _LV_Misc;          // x: night glow (emission boost), y: wind strength (m), z: wind speed, w: unused
float4 _LV_LightPos[LV_MAX_LIGHTS];   // xyz: position, w: range
float4 _LV_LightCol[LV_MAX_LIGHTS];   // rgb: colour * intensity (already in the working colour space)
float _LV_LightCount;

static const float3 LV_UP = float3(0.0, 0.0, -1.0);

// Vertex colours are not colour-space converted by Unity: convert them like material colours are.
inline half3 LV_VertexColor(half3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return GammaToLinearSpace(c);
#endif
}

// Gentle wind sway in world space; weight 0 at the base of a plant, 1 at its tips.
inline float3 LV_Wind(float3 worldPos, float weight)
{
    if (weight <= 0.0) return worldPos;
    float t = _Time.y * _LV_Misc.z;
    float s = sin(t + worldPos.x * 0.37 + worldPos.y * 0.21) + 0.45 * sin(t * 2.3 + worldPos.x * 0.93 + 1.7);
    float a = _LV_Misc.y * weight;
    worldPos.x += s * a;
    worldPos.y += 0.35 * s * a;
    return worldPos;
}

// Interleaved gradient noise in [0,1): ordered-looking dither for fades without transparency sorting.
inline float LV_Dither(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Cut-outs (MapView: a tall prop hiding a unit, the hovered object, the cursor or the camera's focus point): up to four
// soft dithered holes per renderer, round on screen around a world point. Per renderer (MaterialPropertyBlock):
//   _CutN.xyz = the centre (world), _CutN.w = the hole's radius in metres at that centre (0 = no cut, the default).
// Inside half the radius the surface is gone, towards the rim it dithers back in. Surfaces (keepBehind) clearly
// behind the centre — further along the view ray than centre + 0.35 radius — are kept, so a trunk behind a unit stays
// solid; the ink hull (Outline: the model's back faces) is cut at any depth, or the far inside of a hollowed canopy
// would show through the hole as solid ink.
float4 _Cut0, _Cut1, _Cut2, _Cut3;

inline half LV_CutKeep(float4 cut, float3 toFrag, bool keepBehind)
{
    if (cut.w <= 0.0) return 1.0;
    float3 c = cut.xyz - _WorldSpaceCameraPos.xyz;
    float dc = max(length(c), 0.0001);
    float3 dir = c / dc;
    float along = dot(toFrag, dir);
    if (along <= 0.0001) return 1.0;
    if (keepBehind && along > dc + 0.35 * cut.w) return 1.0;
    // angular distance from the centre, relative to the hole's angular radius (≈ a circle on screen)
    float perp = length(toFrag - dir * along);
    float r = (perp / along) * dc / cut.w;
    return smoothstep(0.5, 1.0, r);
}

// Coverage the cut-outs leave at a surface point (1 = solid, 0 = cut away).
inline half LV_CutCoverage(float3 worldPos, bool keepBehind)
{
    if (_Cut0.w <= 0.0 && _Cut1.w <= 0.0 && _Cut2.w <= 0.0 && _Cut3.w <= 0.0) return 1.0;
    float3 v = worldPos - _WorldSpaceCameraPos.xyz;
    return min(min(LV_CutKeep(_Cut0, v, keepBehind), LV_CutKeep(_Cut1, v, keepBehind)),
               min(LV_CutKeep(_Cut2, v, keepBehind), LV_CutKeep(_Cut3, v, keepBehind)));
}

// Night grade (MapView, Shader.SetGlobalVector; zero = off, the default): under moon and sky light, colours drift
// towards a cool blue-grey (x = amount, yzw = the tint the albedo's luma takes), while lamplight and fire (the point
// lights) keep the full colour — warm pools stand out of a deep blue night instead of washing into teal.
float4 _LV_Grade;

// Warmth (MapView, Shader.SetGlobalVector; zero = off, the default — the look is then exactly as without it):
//   x = golden hour: how much the sun's share of a surface's light lifts its saturation (rich, glowing greens instead
//       of grey-olive under a warm low sun)
//   y = night: how far lamplit colours lean towards amber (the albedo's luma × LV_LAMP_AMBER), so a lamp pool reads
//       candle-gold on reddish dirt, grass and stone alike
//   z = night: how much a lamp's pool greys out and dims the cool moon / sky fill under it (warm light plus blue
//       moonlight on a red path would otherwise mix to pink)
float4 _LV_Warmth;
static const half3 LV_LAMP_AMBER = half3(1.3, 1.0, 0.5);
static const half3 LV_LUMA = half3(0.3, 0.59, 0.11);

// The sun's soft painterly ramp: wrapped diffuse eased in.
inline half LV_SunRamp(half3 n)
{
    half ndl = dot(n, _LV_SunDir.xyz);
    half wrap = saturate((ndl + 0.45) / 1.45);
    return wrap * wrap * (3.0 - 2.0 * wrap);
}

// Sun and hemisphere ambient; the point lights' contribution separately (points).
inline half3 LV_LightSplit(float3 worldPos, half3 n, out half3 points)
{
    // sun: wrapped diffuse eased into a soft painterly ramp
    half3 light = _LV_SunColor.rgb * LV_SunRamp(n);

    // hemisphere ambient (sky above, bounce below)
    half hemi = dot(n, LV_UP) * 0.5 + 0.5;
    light += lerp(_LV_GroundAmb.rgb, _LV_SkyAmb.rgb, hemi);

    // point lights
    points = half3(0.0, 0.0, 0.0);
    int count = (int)_LV_LightCount;
    for (int i = 0; i < LV_MAX_LIGHTS; i++)
    {
        if (i < count)
        {
            float3 d = _LV_LightPos[i].xyz - worldPos;
            float dist2 = max(dot(d, d), 0.0001);
            float r = max(_LV_LightPos[i].w, 0.01);
            float att = saturate(1.0 - dist2 / (r * r));
            att *= att;
            half pl = saturate((dot(n, d * rsqrt(dist2)) + 0.6) / 1.6);
            points += _LV_LightCol[i].rgb * (att * pl);
        }
    }
    return light;
}

inline half3 LV_Light(float3 worldPos, half3 n, half3 viewDir)
{
    half3 points;
    half3 light = LV_LightSplit(worldPos, n, points);
    return light + points;
}

// Full surface shade: lit albedo + rim + emission, then fog. rimBoost adds a highlight rim (hover / selection);
// emission 0..1 blends towards the unlit, night-boosted colour (windows, lantern glass, crystals, the sky).
inline half3 LV_Shade(float3 worldPos, half3 n, half3 albedo, half emission, half rimBoost, half fogScale)
{
    half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
    half3 points;
    half3 base = LV_LightSplit(worldPos, n, points);
    half3 light = base + points;
    half3 moonlit = albedo;
    if (_LV_Grade.x > 0.0)
        moonlit = lerp(albedo, dot(albedo, LV_LUMA) * _LV_Grade.yzw, saturate(_LV_Grade.x));
    half3 col = moonlit * base;
    half3 lampAlbedo = albedo;
    if (_LV_Warmth.y > 0.0 || _LV_Warmth.z > 0.0)
    {
        // lamp pools: amber on any ground, and under them the warm light wins over the blue fill
        lampAlbedo = lerp(albedo, dot(albedo, LV_LUMA) * LV_LAMP_AMBER, saturate(_LV_Warmth.y));
        // towards a pool the cool fill greys out (so blue + warm passes through warm grey, never pink) and yields
        half pl = dot(points, LV_LUMA);
        half share = pl / max(pl + dot(base, LV_LUMA), 0.0001);
        half z = saturate(_LV_Warmth.z);
        col = lerp(col, dot(col, LV_LUMA).xxx, saturate(z * share * 4.0)) * (1.0 - 0.5 * z * share);
    }
    col += lampAlbedo * points;
    if (_LV_Warmth.x > 0.0)
    {
        // golden hour: sunlit colours richer, the cool-lit shade as it is
        half sunShare = saturate(dot(_LV_SunColor.rgb, LV_LUMA) * LV_SunRamp(n) / max(dot(light, LV_LUMA), 0.0001));
        half cl = dot(col, LV_LUMA);
        col = max(cl + (col - cl) * (1.0 + _LV_Warmth.x * sunShare), 0.0);
    }
    half rim = pow(1.0 - saturate(dot(n, viewDir)), max(_LV_RimColor.a, 0.5));
    half lum = dot(light, LV_LUMA);
    col += _LV_RimColor.rgb * rim * (0.22 * saturate(lum) + rimBoost);
    col = lerp(col, albedo * (1.0 + _LV_Misc.x), saturate(emission));
    float dist = distance(worldPos, _WorldSpaceCameraPos.xyz);
    half fog = saturate((dist - _LV_FogParams.x) / max(_LV_FogParams.y - _LV_FogParams.x, 0.01)) * _LV_FogColor.a * fogScale;
    return lerp(col, _LV_FogColor.rgb, fog);
}

inline half LV_FogAmount(float3 worldPos, half fogScale)
{
    float dist = distance(worldPos, _WorldSpaceCameraPos.xyz);
    return saturate((dist - _LV_FogParams.x) / max(_LV_FogParams.y - _LV_FogParams.x, 0.01)) * _LV_FogColor.a * fogScale;
}

#endif
