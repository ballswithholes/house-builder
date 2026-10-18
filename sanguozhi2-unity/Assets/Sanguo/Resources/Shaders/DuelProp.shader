// 单挑画面的城墙砖石与帅旗 ← 网页版 js/duel-game.js 的 brickMaterial（SG.Gfx.newLowPoly() + 砖纹补丁）与
// 帅旗的 MeshLambertMaterial({ map, side: DoubleSide })。
//   反照率 = 顶点色 × _Color × _MainTex；
//   _Brick = 1：竖直面为错缝砖（0.44 × 0.21 米，每块明暗略异、灰缝压暗、风化起伏、下暗上亮、墙根青苔），平顶为 0.8 米方砖；
//              只作用于低饱和度的石色（stone 权重），按世界坐标（three：z 取反）程序化——与 JS 的 color_fragment 补丁逐行相同；
//   _HL = 1：LowPoly 的半兰伯特 ndl = (n·l·0.5 + 0.5)² × 1.15；_HL = 0：Lambert ndl = max(n·l, 0)（three MeshLambertMaterial）；
//   _Cull：剔除方式（帅旗双面：背面按 VFACE 翻转法线）。环境光：球谐（场景 Trilight），按同一法线在自发光里加。
Shader "Sanguo/DuelProp"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Brick ("Brick (0/1)", Float) = 0
        _HL ("Half Lambert (0/1)", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf DuelPropHL fullforwardshadows addshadow noambient
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Brick, _HL;
        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            fixed facing : VFACE;
        };
        struct SurfaceOutputDuelProp
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;   // 世界坐标法线（Unity；背面已翻转）
        };

        half4 LightingDuelPropHL (SurfaceOutputDuelProp s, half3 lightDir, half atten)
        {
            half d = dot(s.WN, lightDir);
            half hl = d * 0.5 + 0.5;
            half ndl = _HL > 0.5 ? hl * hl * 1.15 : max(d, 0.0);
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten);
            c.a = s.Alpha;
            return c;
        }

        float sgBH (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        void surf (Input IN, inout SurfaceOutputDuelProp o)
        {
            float3 c = IN.color.rgb * _Color.rgb * tex2D(_MainTex, IN.uv_MainTex).rgb;
            float3 wn = normalize(IN.worldNormal);
            if (_Brick > 0.5)
            {
                // three 坐标（z 取反）：vSgBW、vSgBN（几何法线，不随正反面翻转）
                float3 p = float3(IN.worldPos.x, IN.worldPos.y, -IN.worldPos.z);
                float3 n = float3(wn.x, wn.y, -wn.z);
                float mx = max(max(c.r, c.g), c.b), mn = min(min(c.r, c.g), c.b);
                float stone = clamp(1.0 - ((mx - mn) / max(mx, 0.001) - 0.22) * 3.0, 0.0, 1.0);
                float k = 1.0;
                if (abs(n.y) < 0.5)
                {
                    float u = abs(n.x) > abs(n.z) ? p.z : p.x;
                    float row = floor(p.y / 0.21);
                    float uu = u / 0.44 + (row - 2.0 * floor(row * 0.5)) * 0.5;      // GLSL mod(row, 2.0)（负数时也取非负余数）
                    float fy = frac(p.y / 0.21), fu = frac(uu);
                    float mort = 1.0 - smoothstep(0.03, 0.09, min(fy, 1.0 - fy)) * smoothstep(0.015, 0.045, min(fu, 1.0 - fu));
                    float h = sgBH(float2(floor(uu), row));
                    float wear = 0.5 + 0.5 * sin(p.x * 0.7 + sin(p.y * 1.3) * 2.0) * sin(p.y * 0.9 + p.x * 0.21);
                    k = (0.84 + 0.22 * h) * (1.0 - 0.32 * mort) * (0.9 + 0.14 * wear) * (0.74 + 0.26 * smoothstep(0.0, 2.2, p.y));
                    // 墙根青苔（第三版修订）
                    float moss = (1.0 - smoothstep(0.0, 1.4, p.y + wear * 0.6)) * stone;
                    c = lerp(c, c * float3(0.72, 0.86, 0.6), moss * 0.6);
                }
                else
                {
                    float2 g = p.xz / 0.8; float2 f = abs(frac(g) - 0.5);
                    k = (0.9 + 0.12 * sgBH(floor(g))) * (0.84 + 0.16 * (1.0 - smoothstep(0.44, 0.49, max(f.x, f.y))));
                }
                c *= lerp(1.0, k, stone);
            }
            o.Albedo = c;
            o.WN = IN.facing < 0 ? -wn : wn;
            o.Emission = c * ShadeSH9(float4(o.WN, 1.0));
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
