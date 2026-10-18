// 单挑画面的植被与石块（第三版，实例化）← 网页版 js/duel-game.js 的 vegMaterial（SG.Gfx.newLowPoly() + VEG_VS / VEG_FS 补丁）。
// 与 Sanguo/Foliage（世界地图）同一套叶簇 / 石纹公式，单挑版的差别：
//   · 随风摆动的幅度按种类（_WindK：草丛 / 野花 / 芦苇 0.22、树冠与灌丛 0.012、石块 0），x 方向 sin、z 方向 cos × 0.55；
//     相位 = 实例位置（three 坐标）x × 0.7 + z × 0.5；
//   · 环境光为 LowPoly 补丁的三色环境光：天空 / 地面 = 本场的半球光，地平线 = 主场景的地平线色 × 0.56
//     （全局 _DuelAmbSky / _DuelAmbEq / _DuelAmbGnd，由 DuelScreen 设定），按起伏后的法线取。
// 顶点色 rgb = 几何本色，a = sgLeaf：sgLeaf = 1 的部分乘以实例颜色 _InstColor（MaterialPropertyBlock 数组）并带细节，树干保持原色。
// _Kind：1 = 叶簇明暗 + 法线起伏，2 = 石块（SG_ROCK）；_LeafF / _LeafN = SG_LEAF_F / SG_LEAF_N。
// 坐标沿用网页版的 three 坐标（z 取反）；法线起伏与网页版一样加在视空间法线上。
Shader "Sanguo/DuelFoliage"
{
    Properties
    {
        _InstColor ("Instance Color", Color) = (1,1,1,1)
        _Kind ("Kind", Float) = 1
        _LeafF ("Detail Frequency", Float) = 3.6
        _LeafN ("Normal Strength", Float) = 0.42
        _WindK ("Wind", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf DuelFoliageHL vertex:vert fullforwardshadows addshadow noambient
        #pragma target 3.0
        #pragma multi_compile_instancing
        float _Kind, _LeafF, _LeafN, _WindK;
        float4 _DuelAmbSky, _DuelAmbEq, _DuelAmbGnd;    // 全局（Shader.SetGlobalColor）
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 color : COLOR; float3 worldNormal; float3 leafP; float leaf; };
        struct SurfaceOutputDuelFoliage
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float leaf = v.color.a;
            float4 ic = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
            v.color.rgb *= lerp(float3(1, 1, 1), ic.rgb, leaf);
            // 实例位置（Unity 世界）→ three（z 取反）
            float3 inst = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
            float3 inst3 = float3(inst.x, inst.y, -inst.z);
            o.leafP = float3(v.vertex.x, v.vertex.y, -v.vertex.z) + inst3 * 0.37;
            o.leaf = leaf;
            // 随风摆动（网页版只在实例化绘制时摆动；单挑的植被都是实例化的）：three 的 z += cos(…) → Unity 的 z −= cos(…)
            float ph = inst3.x * 0.7 + inst3.z * 0.5;
            float t = _Time.y;
            v.vertex.x += sin(t * 1.9 + ph) * v.vertex.y * _WindK;
            v.vertex.z -= cos(t * 1.4 + ph * 1.3) * v.vertex.y * _WindK * 0.55;
        }
        half4 LightingDuelFoliageHL (SurfaceOutputDuelFoliage s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        float sgLN (float3 p) { return sin(p.x + sin(p.y * 1.3) * 1.2) * sin(p.y * 1.1 + sin(p.z * 1.2) * 1.2) * sin(p.z * 0.9 + sin(p.x * 1.4) * 1.2); }
        void surf (Input IN, inout SurfaceOutputDuelFoliage o)
        {
            float3 c = IN.color.rgb;
            float3 wn = normalize(IN.worldNormal);
            float3 q = IN.leafP * _LeafF;
            float l1 = sgLN(q), l2 = sgLN(q * 2.63 + 5.3), l3 = sgLN(q * 6.1 + 1.7);
            if (_Kind < 1.5)
            {
                float lc = l1 * 0.5 + l2 * 0.32 + l3 * 0.18;
                c *= lerp(1.0, 0.84 + 0.34 * lc + 0.16 * smoothstep(0.35, 0.85, l2 * 0.6 + l3 * 0.4), IN.leaf);
            }
            else
            {
                float lc = l1 * 0.25 + l2 * 0.35 + l3 * 0.4;
                c *= lerp(1.0, 0.86 + 0.22 * lc + 0.1 * sin(IN.leafP.y * 41.0 + l1 * 2.0), IN.leaf);
            }
            // 视空间法线 + 起伏，再换回世界坐标
            float3 vn = mul((float3x3)UNITY_MATRIX_V, wn);
            vn = normalize(vn + IN.leaf * _LeafN * float3(l2 + l3 * 0.5, l1 * 0.6, l3 - l1 * 0.4));
            wn = normalize(mul(vn, (float3x3)UNITY_MATRIX_V));
            o.Albedo = c;
            o.WN = wn;
            float ny = wn.y;
            o.Emission = c * (ny >= 0.0 ? lerp(_DuelAmbEq.rgb, _DuelAmbSky.rgb, ny) : lerp(_DuelAmbEq.rgb, _DuelAmbGnd.rgb, -ny));
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
