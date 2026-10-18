// 人物 / 士兵 / 战马 / 兵器（第三版）← 网页版 js/figure.js 的 kitMaterial / faceMaterial / patchMaterial
// （three r158 MeshStandardMaterial + onBeforeCompile 补丁）。光照照 three 的 PhysicalMaterial 逐项移植（SgStandard，见下），
// 不用 Unity 的 Standard：Gamma 色彩空间下它的非金属 F0 是 0.22（three 0.04）、高光再开平方，人物会发白发亮。
//   反照率 = 贴图 RGB × mix(1, 顶点色（A = 势力色遮罩时乘 _Team）, 贴图 A)            ← JS map_fragment / color_fragment 补丁
//   法线贴图手动解码（运行时生成的 RGBA 纹理不是 DXT5nm）；ORM：R = AO（强度 _AOK）、G = 粗糙度、B = 金属度
//   粗糙度 = min(max(G, 0.0525) + 几何粗糙度（法线的屏幕导数）, 1)                    ← three lights_physical_fragment
//   直接光：Lambert + GGX（V_GGX_SmithCorrelated、D_GGX、F_Schlick，F0 = mix(0.04, 反照率, 金属度)，F90 = 1）
//   环境光：Unity 的球谐环境光（场景 Trilight = JS 半球光）× 漫反射色
//   环境贴图（= JS envMap，强度 _EnvK = 0.85）：_EnvIrr 辐照度、_EnvCube 反射（按 three roughnessToMip 取级别），
//          DFGApprox 单次 + 多次散射、漫反射扣除散射能量、AO 乘漫反射、镜面遮蔽 computeSpecularOcclusion ← three RE_IndirectSpecular_Physical
//   自发光 = 有方向的轮廓光（镜头空间右上后方，按粗糙度减弱；全局 _SgRimColor / _SgRimK，Figure.SetRim）
//          + 受击 / 格开 / 反击着色 _Tint × (0.2 + 0.8·(1 − N·V)^1.6)                ← JS RIM_GLSL / TINT_GLSL
//   地面贴合：_SgGround.x > 0.5 时蒙皮后的顶点在物体空间（= 人物 root 的局部坐标：SkinnedMeshRenderer 的 rootBone 为人物 root）
//            不低于 0.006                                                          ← JS skinning_vertex 补丁（GROUND_SLOT）
// 光照量的约定（与本工程其他着色器相同）：Unity 不带 three 的 π——漫反射 = 反照率 × 光色 × N·L（three 的 BRDF_Lambert 除以 π，
// 网页版的光源强度里乘了 π），镜面 = π × F·V·D × 光色 × N·L。即 Unity 灯光强度 = JS 强度 / π，Trilight 的天空 / 地面色 =
// JS 半球光颜色 × 强度 / π（赤道取两者平均）。环境贴图是绝对亮度，与 three 相同。
Shader "Sanguo/Figure"
{
    Properties
    {
        _MainTex ("Albedo (RGB) Tint Mask (A)", 2D) = "white" {}
        _BumpMap ("Normal (RGB)", 2D) = "bump" {}
        _ORM ("AO (R) Roughness (G) Metal (B)", 2D) = "white" {}
        _EnvCube ("Env Radiance", CUBE) = "" {}
        _EnvIrr ("Env Irradiance", CUBE) = "" {}
        _Team ("Team Color", Color) = (1,1,1,1)
        _Tint ("Hit Tint", Color) = (0,0,0,0)
        _EnvK ("Env Intensity", Float) = 0.85
        _AOK ("AO Intensity", Float) = 0.8
        _SgGround ("Ground Clamp (on)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf SgStandard vertex:vert fullforwardshadows addshadow
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex, _BumpMap, _ORM;
        samplerCUBE _EnvCube, _EnvIrr;
        fixed4 _Team;
        half4 _Tint;
        half _EnvK, _AOK;
        float4 _SgGround;
        half4 _SgRimColor;      // 全局
        half _SgRimK;           // 全局

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void vert (inout appdata_full v)
        {
            if (_SgGround.x > 0.5) v.vertex.y = max(v.vertex.y, 0.006);
        }

        // ---- three r158 PhysicalMaterial 的 BRDF（与 FigureCrowd.shader 相同；CPU 参照实现：Tests~/figure/FigureRender.cs 的 Shade）
        #define SG_PI 3.14159265
        // F_Schlick（F90 = 1）
        float3 sgFresnel (float3 f0, float dotVH)
        {
            float fr = exp2((-5.55473 * dotVH - 6.98316) * dotVH);
            return f0 * (1.0 - fr) + fr;
        }
        // V_GGX_SmithCorrelated × D_GGX（alpha = 粗糙度²）
        float sgSpecVD (float alpha, float dotNL, float dotNV, float dotNH)
        {
            float a2 = alpha * alpha;
            float gv = dotNL * sqrt(a2 + (1.0 - a2) * dotNV * dotNV);
            float gl = dotNV * sqrt(a2 + (1.0 - a2) * dotNL * dotNL);
            float vis = 0.5 / max(gv + gl, 1e-6);
            float dn = dotNH * dotNH * (a2 - 1.0) + 1.0;
            return vis * a2 / (SG_PI * dn * dn);
        }
        // DFGApprox（环境 BRDF 的解析拟合）
        float2 sgDFG (float dotNV, float rough)
        {
            float4 r = rough * float4(-1.0, -0.0275, -0.572, 0.022) + float4(1.0, 0.0425, 1.04, -0.04);
            float a004 = min(r.x * r.x, exp2(-9.28 * dotNV)) * r.x + r.y;
            return float2(-1.04, 1.04) * a004 + r.zw;
        }
        // roughnessToMip（PMREM 的 mip，−2..6）→ _EnvCube 的级别（FigAtlas.MakeEnvCubes：0–3 级 = PMREM mip 6–3，4 级 = mip 1，5 级 = mip −2）
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
        // 直接光（RE_Direct_Physical）+ _GI 里算好的间接光（ForwardAdd 通道的 gi.indirect 为 0）
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
        // 主光与阴影衰减、球谐环境光取自 Unity（UnityGlobalIllumination）；环境贴图两项照 three 算好放进 gi.indirect
        inline void LightingSgStandard_GI (SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
            float3 N = s.Normal, V = data.worldViewDir;
            float rough = 1.0 - s.Smoothness;
            float3 diffC = s.Albedo * (1.0 - s.Metallic);
            float3 specC = lerp(float3(0.04, 0.04, 0.04), s.Albedo, s.Metallic);
            float dotNV = saturate(dot(N, V));
            // getIBLIrradiance / π 与 getIBLRadiance（反射向量按 粗糙度² 偏向法线）
            float3 irr = texCUBElod(_EnvIrr, float4(N, 0.0)).rgb * _EnvK;
            float3 R = normalize(lerp(reflect(-V, N), N, rough * rough));
            float3 rad = texCUBElod(_EnvCube, float4(R, sgEnvLod(rough))).rgb * _EnvK;
            // computeMultiscattering
            float2 fab = sgDFG(dotNV, rough);
            float3 FssEss = specC * fab.x + fab.y;
            float Ems = 1.0 - (fab.x + fab.y);
            float3 Favg = specC + (1.0 - specC) * 0.047619;
            float3 ms = FssEss * Favg / (1.0 - Ems * Favg) * Ems;
            float3 tot = FssEss + ms;
            float3 diffE = diffC * (1.0 - max(max(tot.r, tot.g), tot.b));
            // aomap_fragment：间接漫反射 × AO，间接镜面 × computeSpecularOcclusion
            float ao = s.Occlusion;
            float specAO = saturate(pow(dotNV + ao, exp2(-16.0 * rough - 1.0)) - 1.0 + ao);
            gi.indirect.diffuse = (gi.indirect.diffuse * diffC + diffE * irr) * ao;
            gi.indirect.specular = (rad * FssEss + ms * irr) * specAO;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half4 tex = tex2D(_MainTex, IN.uv_MainTex);
            half3 vc = lerp(IN.color.rgb, IN.color.rgb * _Team.rgb, IN.color.a);
            half3 albedo = tex.rgb * lerp(half3(1.0, 1.0, 1.0), vc, tex.a);
            half4 orm = tex2D(_ORM, IN.uv_MainTex);
            float3 tn = normalize(tex2D(_BumpMap, IN.uv_MainTex).rgb * 2.0 - 1.0);
            // 几何粗糙度：未经法线贴图的法线在屏幕上的变化率（three geometryRoughness）
            float3 gn = normalize(WorldNormalVector(IN, float3(0.0, 0.0, 1.0)));
            float3 dxy = max(abs(ddx(gn)), abs(ddy(gn)));
            float rough = min(max(orm.g, 0.0525) + max(max(dxy.x, dxy.y), dxy.z), 1.0);
            o.Normal = tn;
            o.Albedo = albedo;
            o.Metallic = orm.b;
            o.Smoothness = 1.0 - rough;
            o.Occlusion = 1.0 + (orm.r - 1.0) * _AOK;
            o.Alpha = 1.0;
            // 轮廓光与受击着色：法线贴图后的世界法线、指向镜头的方向
            float3 wn = normalize(WorldNormalVector(IN, tn));
            float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);
            half sgR = 1.0 - saturate(dot(wn, V));
            // 轮廓光方向：镜头空间 (0.55, 0.62, −0.56)（右上后方）→ 世界空间（视图矩阵为正交矩阵，转置即逆）
            float3 rimDir = normalize(mul(float3(0.55, 0.62, -0.56), (float3x3)UNITY_MATRIX_V));
            half sgL = saturate(dot(wn, rimDir) * 0.5 + 0.5);
            half sgR2 = sgR * sgR;
            half3 rim = _SgRimColor.rgb * (sgR2 * sgR2 * _SgRimK * sgL * sgL * 1.6) * (0.2 + 0.6 * albedo) * lerp(1.0, 0.45, orm.g);
            half sgT = pow(max(sgR, 1e-4), 1.6);
            o.Emission = rim + _Tint.rgb * (0.2 + 0.8 * sgT);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
