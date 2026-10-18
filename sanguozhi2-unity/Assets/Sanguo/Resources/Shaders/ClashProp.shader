// 攻击画面的道具（第三版）← 网页版 js/clash.js 的 propMaterial（PROP_VS / PROP_FS / PROP_FS_NORMAL 逐行移植）
// 树、石、城墙、屋顶、木件合并为一个网格（一次绘制 + 一次投影）；uv2.x（TEXCOORD1）= aKind：
//   0 原色 / 1 树叶（叶簇明暗 + 法线起伏）/ 2 石块（颗粒、层理）/ 3 砖石墙（按世界坐标的错缝砖、灰缝；平面为方砖）/ 4 筒瓦屋面。
// 叶与石的法线起伏与网页版一样加在视空间法线上（Unity 本画面的视空间坐标与网页版相同，见 Clash.cs 的坐标说明），再换回世界坐标。
// 也用于箭与帅旗杆（aKind = 0）；_Color 为逐个绘制的染色（网页版箭的 instanceColor，必杀箭雨 > 1 发亮）。
// 光照：LowPoly 的半兰伯特 + 阴影；环境光为网页版 LowPoly 补丁的三色半球光（全局 _SgClashSky / Eq / Gnd，按起伏后的法线取）。
// 坐标：着色计算沿用网页版的 three 坐标（本画面根 _SgClashOrigin 为原点，世界 z 取反）。
Shader "Sanguo/ClashProp"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf ClashHL vertex:vert fullforwardshadows addshadow noambient
        #pragma target 3.0
        fixed4 _Color;
        float4 _SgClashOrigin;
        half4 _SgClashSky, _SgClashEq, _SgClashGnd;
        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
            float kind;
        };
        struct SurfaceOutputClash
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.kind = v.texcoord1.x;
        }
        half4 LightingClashHL (SurfaceOutputClash s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        half3 sgAmb (float ny)
        {
            return ny >= 0.0 ? lerp(_SgClashEq.rgb, _SgClashSky.rgb, ny) : lerp(_SgClashEq.rgb, _SgClashGnd.rgb, -ny);
        }
        float sgPN (float3 p) { return sin(p.x + sin(p.y * 1.3) * 1.2) * sin(p.y * 1.1 + sin(p.z * 1.2) * 1.2) * sin(p.z * 0.9 + sin(p.x * 1.4) * 1.2); }
        float sgPH (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        // GLSL 的 mod（结果与除数同号；HLSL 的 fmod 与被除数同号，负数行号会算错）
        float sgMod (float x, float y) { return x - y * floor(x / y); }
        void surf (Input IN, inout SurfaceOutputClash o)
        {
            float3 c = IN.color.rgb * _Color.rgb;
            float3 nU = normalize(IN.worldNormal);
            float3 n = float3(nU.x, nU.y, -nU.z);
            float3 w = IN.worldPos - _SgClashOrigin.xyz;
            float3 p = float3(w.x, w.y, -w.z);
            float sgL1 = 0.0, sgL2 = 0.0, sgL3 = 0.0, sgLeafW = 0.0;
            float k = 1.0;
            float kd = IN.kind;
            if (kd > 0.5 && kd < 2.5)
            {
                float3 q = p * (kd < 1.5 ? 4.2 : 3.0);
                sgL1 = sgPN(q); sgL2 = sgPN(q * 2.63 + 5.3); sgL3 = sgPN(q * 6.1 + 1.7);
                if (kd < 1.5)
                {
                    float cc = sgL1 * 0.5 + sgL2 * 0.32 + sgL3 * 0.18;
                    k = 0.82 + 0.36 * cc + 0.16 * smoothstep(0.35, 0.85, sgL2 * 0.6 + sgL3 * 0.4);
                    sgLeafW = 0.45;
                }
                else
                {
                    float cc = sgL1 * 0.25 + sgL2 * 0.35 + sgL3 * 0.4;
                    k = 0.84 + 0.24 * cc + 0.1 * sin(p.y * 13.0 + sgL1 * 2.0);
                    sgLeafW = 0.28;
                }
            }
            else if (kd > 2.5 && kd < 3.5)
            {
                float ay = abs(n.y);
                if (ay < 0.6)
                {
                    float u = abs(n.x) > abs(n.z) ? p.z : p.x;
                    float row = floor(p.y / 0.21);
                    float uu = u / 0.46 + sgMod(row, 2.0) * 0.5;
                    float fy = frac(p.y / 0.21), fu = frac(uu);
                    float mort = 1.0 - smoothstep(0.04, 0.12, min(fy, 1.0 - fy)) * smoothstep(0.02, 0.06, min(fu, 1.0 - fu));
                    float h = sgPH(float2(floor(uu), row));
                    k = (0.88 + 0.2 * h) * (1.0 - 0.3 * mort) * (0.94 + 0.08 * sgPN(p * 3.1));
                }
                else
                {
                    float2 g = p.xz / 0.5;
                    float2 f = abs(frac(g) - 0.5);
                    k = (0.9 + 0.12 * sgPH(floor(g))) * (0.84 + 0.16 * (1.0 - smoothstep(0.42, 0.48, max(f.x, f.y))));
                }
            }
            else if (kd > 3.5)
            {
                float2 t = normalize(float2(-n.z, n.x) + float2(1e-4, 0.0));
                float u = dot(p.xz, t);
                float roll = 0.5 + 0.5 * sin(u / 0.16 * 6.2832);
                float fy = frac(p.y / 0.11);
                k = (0.8 + 0.26 * roll * roll) * (1.0 - 0.2 * (1.0 - smoothstep(0.0, 0.25, fy)));
            }
            c *= k;
            // 叶 / 石的法线起伏：视空间法线 + 起伏，再换回世界坐标
            if (sgLeafW > 0.0)
            {
                float3 vn = mul((float3x3)UNITY_MATRIX_V, nU);
                vn = normalize(vn + sgLeafW * float3(sgL2 + sgL3 * 0.5, sgL1 * 0.6, sgL3 - sgL1 * 0.4));
                nU = normalize(mul(vn, (float3x3)UNITY_MATRIX_V));
            }
            o.Albedo = c;
            o.WN = nU;
            o.Emission = c * sgAmb(nU.y);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
