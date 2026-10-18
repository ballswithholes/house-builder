// 城池 / 关隘 / 桥的砖石材质（第三版）← 网页版 js/art.js 的 masonryMaterial（MASONRY_FS 逐行移植）
// LowPoly 的半兰伯特光照之上按世界坐标程序化生成：竖直墙面为错缝砖石层（每块明暗略异、灰缝压暗；只作用于低饱和度的墙色，
// 旗帜、门窗等彩色部分基本不变），坡面为筒瓦（沿檐方向的瓦垄 + 逐行的瓦缝），平顶为方砖。随镜头距离淡出（_MFade.x → _MFade.y）。
// 坐标沿用网页版的 three 坐标（世界 z 取反）。
Shader "Sanguo/Masonry"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MFade ("Fade near/far", Vector) = (16,30,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf MasonryHL vertex:vert fullforwardshadows addshadow
        #pragma target 3.0
        fixed4 _Color;
        float4 _MFade;
        struct Input { float4 color : COLOR; float3 worldPos; float3 worldNormal; float viewDepth; };
        struct SurfaceOutputMasonry
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.viewDepth = -UnityObjectToViewPos(v.vertex).z;
        }
        half4 LightingMasonryHL (SurfaceOutputMasonry s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        float sgMHash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        // GLSL mod（向下取整）
        float sgMod (float x, float y) { return x - y * floor(x / y); }
        void surf (Input IN, inout SurfaceOutputMasonry o)
        {
            float3 wn = normalize(IN.worldNormal);
            float3 c = IN.color.rgb * _Color.rgb;
            float sgFd = 1.0 - smoothstep(_MFade.x, _MFade.y, IN.viewDepth);
            if (sgFd > 0.0)
            {
                float3 n = float3(wn.x, wn.y, -wn.z);
                float3 p = float3(IN.worldPos.x, IN.worldPos.y, -IN.worldPos.z);
                float mx = max(max(c.r, c.g), c.b), mn = min(min(c.r, c.g), c.b);
                float sat = (mx - mn) / max(mx, 0.001);
                float ay = abs(n.y);
                float k = 1.0;
                // 墙面：错缝砖石
                float wW = (1.0 - smoothstep(0.25, 0.4, ay)) * clamp(1.0 - (sat - 0.25) * 2.2, 0.0, 1.0);
                if (wW > 0.0)
                {
                    float u = abs(n.x) > abs(n.z) ? p.z : p.x;
                    float row = floor(p.y / 0.052);
                    float uu = u / 0.12 + sgMod(row, 2.0) * 0.5;
                    float fy = frac(p.y / 0.052), fu = frac(uu);
                    float mort = 1.0 - smoothstep(0.05, 0.16, min(fy, 1.0 - fy)) * smoothstep(0.03, 0.08, min(fu, 1.0 - fu));
                    float h = sgMHash(float2(floor(uu), row));
                    k *= lerp(1.0, (0.9 + 0.16 * h) * (1.0 - 0.24 * mort), wW);
                }
                // 坡面：筒瓦
                float rW = smoothstep(0.25, 0.4, ay) * (1.0 - smoothstep(0.9, 0.97, ay));
                if (rW > 0.0)
                {
                    float2 tt = normalize(float2(-n.z, n.x) + float2(1e-4, 0.0));
                    float u = dot(p.xz, tt);
                    float roll = 0.5 + 0.5 * sin(u / 0.04 * 6.2832);
                    float fy = frac(p.y / 0.03);
                    k *= lerp(1.0, (0.84 + 0.22 * roll * roll) * (1.0 - 0.18 * (1.0 - smoothstep(0.0, 0.22, fy))), rW);
                }
                // 平顶：方砖
                float fW = smoothstep(0.9, 0.97, ay) * clamp(1.0 - (sat - 0.25) * 2.2, 0.0, 1.0);
                if (fW > 0.0)
                {
                    float2 g = p.xz / 0.14;
                    float2 f = abs(frac(g) - 0.5);
                    float j = 1.0 - smoothstep(0.42, 0.48, max(f.x, f.y));
                    k *= lerp(1.0, (0.93 + 0.1 * sgMHash(floor(g))) * (0.86 + 0.14 * j), fW * 0.8);
                }
                c *= lerp(1.0, k, sgFd);
            }
            o.Albedo = c;
            o.WN = wn;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
