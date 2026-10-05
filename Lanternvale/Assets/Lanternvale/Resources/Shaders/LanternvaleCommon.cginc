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

inline half3 LV_Light(float3 worldPos, half3 n, half3 viewDir)
{
    // sun: wrapped diffuse eased into a soft painterly ramp
    half ndl = dot(n, _LV_SunDir.xyz);
    half wrap = saturate((ndl + 0.45) / 1.45);
    half ramp = wrap * wrap * (3.0 - 2.0 * wrap);
    half3 light = _LV_SunColor.rgb * ramp;

    // hemisphere ambient (sky above, bounce below)
    half hemi = dot(n, LV_UP) * 0.5 + 0.5;
    light += lerp(_LV_GroundAmb.rgb, _LV_SkyAmb.rgb, hemi);

    // point lights
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
            light += _LV_LightCol[i].rgb * (att * pl);
        }
    }
    return light;
}

// Full surface shade: lit albedo + rim + emission, then fog. rimBoost adds a highlight rim (hover / selection);
// emission 0..1 blends towards the unlit, night-boosted colour (windows, lantern glass, crystals, the sky).
inline half3 LV_Shade(float3 worldPos, half3 n, half3 albedo, half emission, half rimBoost, half fogScale)
{
    half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
    half3 light = LV_Light(worldPos, n, viewDir);
    half3 col = albedo * light;
    half rim = pow(1.0 - saturate(dot(n, viewDir)), max(_LV_RimColor.a, 0.5));
    half lum = dot(light, half3(0.3, 0.59, 0.11));
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
