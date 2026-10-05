// Minimal stand-in for Unity's UnityCG.cginc (only what Lanternvale's shaders use), so the HLSL of every pass can be
// syntax- and type-checked offline with glslangValidator (Tools/shadercheck/check.py). Not used by Unity.
#ifndef LV_STUB_UNITYCG
#define LV_STUB_UNITYCG
#define fixed half
#define fixed2 half2
#define fixed3 half3
#define fixed4 half4
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4x4 unity_MatrixVP;
#define UNITY_MATRIX_VP unity_MatrixVP
float4 _Time;
float3 _WorldSpaceCameraPos;
float4 _ScreenParams;
float4 _ProjectionParams;
inline float4 UnityObjectToClipPos(float3 v) { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(v, 1.0))); }
inline float4 UnityObjectToClipPos(float4 v) { return UnityObjectToClipPos(v.xyz); }
inline float4 UnityWorldToClipPos(float3 p) { return mul(unity_MatrixVP, float4(p, 1.0)); }
inline float3 UnityObjectToWorldNormal(float3 n) { return normalize(mul(n, (float3x3)unity_WorldToObject)); }
inline float3 UnityObjectToWorldDir(float3 d) { return normalize(mul((float3x3)unity_ObjectToWorld, d)); }
inline float4 ComputeScreenPos(float4 pos) { float4 o = pos * 0.5; o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w; o.zw = pos.zw; return o; }
#define TRANSFORM_TEX(tex, name) (tex.xy * name##_ST.xy + name##_ST.zw)
inline half3 GammaToLinearSpace(half3 sRGB) { return sRGB * (sRGB * (sRGB * 0.305306011 + 0.682171111) + 0.012522878); }
inline half3 LinearToGammaSpace(half3 lin) { return max(1.055 * pow(max(lin, 0.0), 0.416666667) - 0.055, 0.0); }
#endif
