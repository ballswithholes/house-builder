// 士兵群（第三版）← 网页版 js/figure.js 的 crowdMaterial / crowdDepthMaterial（CROWD_VS_PARS + 装具材质补丁）
// Graphics.DrawMeshInstanced 实例化绘制：顶点着色器按烘焙贴图蒙皮（每帧一行，每骨 3 个 RGBAHalf 像素 = 3×4 矩阵的三行，
// 双线性过滤 = 两帧插值）；实例属性 _SgAnim = (起始行, 帧数（> 0 循环 / < 0 单次）, 起始时间, 速度)、_SgTeam = 势力色。
// 蒙皮骨与权重在 uv2 / uv3（TEXCOORD2 / 3）。片元部分与 Sanguo/Figure 相同（three PhysicalMaterial 的光照、环境贴图
// _EnvCube / _EnvIrr、轮廓光；不含受击着色），光照量的约定见 Figure.shader 头部（Unity 灯光强度 = JS 强度 / π）。
// 步兵组 _SgGroundOn = 1：蒙皮后的顶点（实例局部坐标）不低于 0.006（倒地时的披风、腰裙不穿地）。
Shader "Sanguo/FigureCrowd"
{
    Properties
    {
        _MainTex ("Albedo (RGB) Tint Mask (A)", 2D) = "white" {}
        _BumpMap ("Normal (RGB)", 2D) = "bump" {}
        _ORM ("AO (R) Roughness (G) Metal (B)", 2D) = "white" {}
        _EnvCube ("Env Radiance", CUBE) = "" {}
        _EnvIrr ("Env Irradiance", CUBE) = "" {}
        _EnvK ("Env Intensity", Float) = 0.85
        _AOK ("AO Intensity", Float) = 0.8
        _SgBake ("Bake (RGBAHalf)", 2D) = "black" {}
        _SgSize ("Bake Size (W, rows)", Vector) = (1,1,0,0)
        _SgTime ("Time", Float) = 0
        _SgFps ("Bake Fps", Float) = 24
        _SgGroundOn ("Ground Clamp", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf SgStandard vertex:vert fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex, _BumpMap, _ORM;
        samplerCUBE _EnvCube, _EnvIrr;
        sampler2D _SgBake;
        float4 _SgSize;
        float _SgTime, _SgFps, _SgGroundOn;
        half _EnvK, _AOK;
        half4 _SgRimColor;      // 全局
        half _SgRimK;           // 全局

        UNITY_INSTANCING_BUFFER_START(SgProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _SgAnim)
            UNITY_DEFINE_INSTANCED_PROP(float4, _SgTeam)
        UNITY_INSTANCING_BUFFER_END(SgProps)

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            float3 sgTeam;
            INTERNAL_DATA
        };

        // 第 bi 根骨骼在烘焙贴图第 v 行（纹理坐标）的 3×4 矩阵
        float4x4 sgBone (float v, float bi)
        {
            float dx = 1.0 / _SgSize.x;
            float x = (floor(bi + 0.5) * 3.0 + 0.5) * dx;
            float4 r0 = tex2Dlod(_SgBake, float4(x, v, 0.0, 0.0));
            float4 r1 = tex2Dlod(_SgBake, float4(x + dx, v, 0.0, 0.0));
            float4 r2 = tex2Dlod(_SgBake, float4(x + dx * 2.0, v, 0.0, 0.0));
            return float4x4(r0, r1, r2, float4(0.0, 0.0, 0.0, 1.0));
        }

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);
            float4 an = UNITY_ACCESS_INSTANCED_PROP(SgProps, _SgAnim);
            float n = abs(an.y);
            float f = max(0.0, (_SgTime - an.z) * _SgFps * an.w);
            f = an.y > 0.0 ? fmod(f, n) : min(f, n - 1.0);
            float rv = (an.x + f + 0.5) / _SgSize.y;
            float4 si = v.texcoord2, sw = v.texcoord3;
            float4x4 m = sgBone(rv, si.x) * sw.x + sgBone(rv, si.y) * sw.y + sgBone(rv, si.z) * sw.z + sgBone(rv, si.w) * sw.w;
            v.vertex = float4(mul(m, float4(v.vertex.xyz, 1.0)).xyz, 1.0);
            v.normal = mul((float3x3)m, v.normal);
            v.tangent.xyz = mul((float3x3)m, v.tangent.xyz);
            if (_SgGroundOn > 0.5) v.vertex.y = max(v.vertex.y, 0.006);
            o.sgTeam = UNITY_ACCESS_INSTANCED_PROP(SgProps, _SgTeam).rgb;
        }

        // ---- three r158 PhysicalMaterial 的 BRDF（与 Figure.shader 逐行相同）
        #define SG_PI 3.14159265
        float3 sgFresnel (float3 f0, float dotVH)
        {
            float fr = exp2((-5.55473 * dotVH - 6.98316) * dotVH);
            return f0 * (1.0 - fr) + fr;
        }
        float sgSpecVD (float alpha, float dotNL, float dotNV, float dotNH)
        {
            float a2 = alpha * alpha;
            float gv = dotNL * sqrt(a2 + (1.0 - a2) * dotNV * dotNV);
            float gl = dotNV * sqrt(a2 + (1.0 - a2) * dotNL * dotNL);
            float vis = 0.5 / max(gv + gl, 1e-6);
            float dn = dotNH * dotNH * (a2 - 1.0) + 1.0;
            return vis * a2 / (SG_PI * dn * dn);
        }
        float2 sgDFG (float dotNV, float rough)
        {
            float4 r = rough * float4(-1.0, -0.0275, -0.572, 0.022) + float4(1.0, 0.0425, 1.04, -0.04);
            float a004 = min(r.x * r.x, exp2(-9.28 * dotNV)) * r.x + r.y;
            return float2(-1.04, 1.04) * a004 + r.zw;
        }
        float sgEnvLod (float r)
        {
            float pm;
            if (r >= 0.8) pm = (1.0 - r) * 5.0 - 2.0;
            else if (r >= 0.4) pm = (0.8 - r) * 7.5 - 1.0;
            else if (r >= 0.305) pm = (0.4 - r) / 0.095 + 2.0;
            else if (r >= 0.21) pm = (0.305 - r) / 0.095 + 3.0;
            else pm = -2.0 * log2(1.16 * r);
            pm = clamp(pm, -2.0, 6.0);
            return pm >= 3.0 ? 6.0 - pm : (pm >= 1.0 ? 3.0 + (3.0 - pm) * 0.5 : 4.0 + (1.0 - pm) / 3.0);
        }
        inline half4 LightingSgStandard (SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            float3 N = s.Normal, V = viewDir, L = gi.light.dir;
            float rough = 1.0 - s.Smoothness;
            float3 diffC = s.Albedo * (1.0 - s.Metallic);
            float3 specC = lerp(float3(0.04, 0.04, 0.04), s.Albedo, s.Metallic);
            float3 H = Unity_SafeNormalize(L + V);
            float dotNL = saturate(dot(N, L)), dotNV = saturate(dot(N, V)), dotNH = saturate(dot(N, H)), dotVH = saturate(dot(V, H));
            float3 spec = sgFresnel(specC, dotVH) * (sgSpecVD(rough * rough, dotNL, dotNV, dotNH) * SG_PI);
            half4 c;
            c.rgb = gi.light.color * (dotNL * (diffC + spec)) + gi.indirect.diffuse + gi.indirect.specular;
            c.a = s.Alpha;
            return c;
        }
        inline void LightingSgStandard_GI (SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
            float3 N = s.Normal, V = data.worldViewDir;
            float rough = 1.0 - s.Smoothness;
            float3 diffC = s.Albedo * (1.0 - s.Metallic);
            float3 specC = lerp(float3(0.04, 0.04, 0.04), s.Albedo, s.Metallic);
            float dotNV = saturate(dot(N, V));
            float3 irr = texCUBElod(_EnvIrr, float4(N, 0.0)).rgb * _EnvK;
            float3 R = normalize(lerp(reflect(-V, N), N, rough * rough));
            float3 rad = texCUBElod(_EnvCube, float4(R, sgEnvLod(rough))).rgb * _EnvK;
            float2 fab = sgDFG(dotNV, rough);
            float3 FssEss = specC * fab.x + fab.y;
            float Ems = 1.0 - (fab.x + fab.y);
            float3 Favg = specC + (1.0 - specC) * 0.047619;
            float3 ms = FssEss * Favg / (1.0 - Ems * Favg) * Ems;
            float3 tot = FssEss + ms;
            float3 diffE = diffC * (1.0 - max(max(tot.r, tot.g), tot.b));
            float ao = s.Occlusion;
            float specAO = saturate(pow(dotNV + ao, exp2(-16.0 * rough - 1.0)) - 1.0 + ao);
            gi.indirect.diffuse = (gi.indirect.diffuse * diffC + diffE * irr) * ao;
            gi.indirect.specular = (rad * FssEss + ms * irr) * specAO;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half4 tex = tex2D(_MainTex, IN.uv_MainTex);
            half3 vc = lerp(IN.color.rgb, IN.color.rgb * IN.sgTeam, IN.color.a);
            half3 albedo = tex.rgb * lerp(half3(1.0, 1.0, 1.0), vc, tex.a);
            half4 orm = tex2D(_ORM, IN.uv_MainTex);
            float3 tn = normalize(tex2D(_BumpMap, IN.uv_MainTex).rgb * 2.0 - 1.0);
            float3 gn = normalize(WorldNormalVector(IN, float3(0.0, 0.0, 1.0)));
            float3 dxy = max(abs(ddx(gn)), abs(ddy(gn)));
            float rough = min(max(orm.g, 0.0525) + max(max(dxy.x, dxy.y), dxy.z), 1.0);
            o.Normal = tn;
            o.Albedo = albedo;
            o.Metallic = orm.b;
            o.Smoothness = 1.0 - rough;
            o.Occlusion = 1.0 + (orm.r - 1.0) * _AOK;
            o.Alpha = 1.0;
            float3 wn = normalize(WorldNormalVector(IN, tn));
            float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
            half sgR = 1.0 - saturate(dot(wn, V));
            float3 rimDir = normalize(mul(float3(0.55, 0.62, -0.56), (float3x3)UNITY_MATRIX_V));
            half sgL = saturate(dot(wn, rimDir) * 0.5 + 0.5);
            half sgR2 = sgR * sgR;
            o.Emission = _SgRimColor.rgb * (sgR2 * sgR2 * _SgRimK * sgL * sgL * 1.6) * (0.2 + 0.6 * albedo) * lerp(1.0, 0.45, orm.g);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
