// 单挑画面的地面（第三版）← 网页版 js/duel-game.js 的 groundMaterial：SG.Gfx.terrainMaterial()（js/art.js 的 TERRAIN_FS_ALBEDO）
// 的顶点版，只改一处——高度换算为「海拔」：sgH = 1.2 + y × 0.32（场地 y = 0 不当作海滩，远山山顶才见岩石）。
// 其余与 Sanguo/Terrain 的顶点版逐行相同：顶点色 = 底色，uv2（TEXCOORD1）= aMat（沙, 岩石倾向, 雪, 1 − 农田）；
// LowPoly 的半兰伯特光照（ndl = (n·l·0.5 + 0.5)² × 1.15，主光与轮廓光都按此）+ 球谐环境光（场景 Trilight）；最终颜色经高光肩部。
// 坐标：着色计算沿用网页版的 three 坐标（世界坐标 z 取反），单挑场地的根在世界原点。
Shader "Sanguo/DuelGround"
{
    Properties
    {
        _DetA ("Detail A", 2D) = "gray" {}
        _DetB ("Detail B", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf DuelGroundHL vertex:vert finalcolor:shoulder fullforwardshadows addshadow noambient
        #pragma target 3.0
        sampler2D _DetA, _DetB;
        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
            float4 mat;
        };
        struct SurfaceOutputDuelGround
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;   // 世界坐标法线（Unity）
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.mat = v.texcoord1;
        }

        // 与 LowPoly 相同的半兰伯特
        half4 LightingDuelGroundHL (SurfaceOutputDuelGround s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }

        // 高光肩部：最终颜色的最大通道超过 0.8 后按指数曲线平滑趋近 1（保持色相）
        void shoulder (Input IN, SurfaceOutputDuelGround o, inout fixed4 color)
        {
            float3 c = color.rgb;
            float peak = max(max(c.r, c.g), c.b);
            if (peak > 0.8) c *= (0.8 + 0.2 * (1.0 - exp((0.8 - peak) / 0.2))) / peak;
            color.rgb = c;
        }

        float3 sgCrop (float id)
        {
            float3 c = float3(0.42, 0.57, 0.23);
            c = lerp(c, float3(0.75, 0.66, 0.31), step(0.24, id));
            c = lerp(c, float3(0.53, 0.41, 0.28), step(0.42, id));
            c = lerp(c, float3(0.33, 0.49, 0.2), step(0.56, id));
            c = lerp(c, float3(0.64, 0.62, 0.3), step(0.72, id));
            c = lerp(c, float3(0.47, 0.6, 0.26), step(0.87, id));
            return c;
        }

        void surf (Input IN, inout SurfaceOutputDuelGround o)
        {
            float3 W3 = float3(IN.worldPos.x, IN.worldPos.y, -IN.worldPos.z);
            float2 sgP = IN.worldPos.xz;
            // 单挑：高度换算为「海拔」（网页版 groundMaterial 的唯一改动）
            float sgH = 1.2 + W3.y * 0.32;
            float3 sgN = normalize(float3(IN.worldNormal.x, IN.worldNormal.y, -IN.worldNormal.z));
            float3 sgTint = IN.color.rgb;
            float4 sgMat = IN.mat;
            float sgForest = 0.0;
            float sgAO = 0.5;
            float sgNearW = 1.0;
            float4 sgDA = tex2D(_DetA, sgP * 0.23);
            float4 sgDM = tex2D(_DetA, sgP * 0.047 + float2(0.31, 0.67));
            float4 sgDF = tex2D(_DetA, sgP * 0.93 + float2(0.53, 0.19));
            float4 sgDN = tex2D(_DetA, sgP * 3.1 + float2(0.7, 0.2));
            float4 sgDB = tex2D(_DetB, sgP * 0.11 + float2(0.13, 0.71));
            float sgSlope = 1.0 - sgN.y;
            // 近景（镜头 18 单位内满、40 单位外无）多一级细节（土粒、草簇）、田里的垄行
            float sgNear = 1.0 - smoothstep(18.0, 40.0, distance(_WorldSpaceCameraPos, IN.worldPos));
            // 草地：三级细节明暗 + 大尺度的干湿斑块
            float3 sgCol = sgTint * (0.76 + 0.5 * (sgDA.r * 0.36 + sgDF.r * 0.28 + sgDM.r * 0.36));
            if (sgNear > 0.01) sgCol *= 1.0 + (sgDN.r - 0.5) * 0.24 * sgNear;
            sgCol = lerp(dot(sgCol, float3(0.3, 0.59, 0.11)).xxx, sgCol, 0.9) * lerp(float3(1.035, 1.0, 0.93), float3(0.96, 1.0, 1.04), sgDM.b);
            sgCol = lerp(sgCol, sgCol * float3(1.1, 1.03, 0.8), smoothstep(0.5, 0.78, sgDM.g) * 0.55);
            sgCol = lerp(sgCol, sgCol * float3(0.78, 0.86, 0.74), smoothstep(0.55, 0.85, sgDM.a) * 0.6);
            sgCol *= 1.0 - 0.3 * sgForest;
            // 裸土：陡坡、干旱区的斑块
            float sgDry = clamp((sgTint.r - sgTint.g * 0.85) * 4.0 + 0.3, 0.0, 1.0);
            float sgDirtW = smoothstep(0.66, 0.9, sgDM.a * 0.62 + sgSlope * 0.95 + sgDry * 0.2 + (sgDA.g - 0.5) * 0.2);
            float3 sgDirt = lerp(float3(0.48, 0.39, 0.28), sgTint * float3(0.95, 0.85, 0.7), 0.35) * (0.78 + 0.44 * sgDA.g);
            sgCol = lerp(sgCol, sgDirt, sgDirtW * 0.85);
            // 农田：地块、犁沟、田埂
            float sgFarm = 1.0 - sgMat.a;
            float sgFw = 0.0;
            if (sgFarm > 0.02)
            {
                float3 sgCrop3 = sgCrop(sgDB.b) * (0.8 + 0.34 * sgDB.a);
                sgCrop3 = lerp(sgCrop3, float3(0.58, 0.5, 0.36), step(sgDB.b, 0.03));
                sgCrop3 = lerp(sgCrop3, sgTint, 0.15);
                if (sgNear > 0.01)
                {
                    float2 sgRD = float2(cos(sgDB.b * 6.2832), sin(sgDB.b * 6.2832));
                    sgCrop3 *= lerp(1.0, 0.86 + 0.14 * sin(dot(sgP, sgRD) * 38.0), sgNear * step(0.03, sgDB.b));
                }
                sgFw = smoothstep(0.3, 0.52, sgFarm + (sgDM.r - 0.5) * 0.55) * (1.0 - smoothstep(0.1, 0.26, sgSlope));
                sgCol = lerp(sgCol, sgCrop3, sgFw);
            }
            // 沙：海滩与河岸（按高度）、按材质（河岸）
            float sgBeach = (1.0 - smoothstep(0.2, 0.48, sgH + (sgDA.b - 0.5) * 0.14)) * max(sgNearW, 1.0 - smoothstep(-0.02, 0.05, sgH));
            float sgRip = lerp(0.08, 0.26, smoothstep(0.3, 0.8, sgMat.r));
            float3 sgSand = lerp(float3(0.87, 0.8, 0.62), sgTint * 1.05, clamp(sgMat.r * 0.7, 0.0, 0.6)) * (1.0 - sgRip * 0.55 + sgRip * sgDA.b + (sgDF.r - 0.5) * 0.08);
            sgSand *= lerp(0.8, 1.0, smoothstep(0.0, 0.22, sgH));
            sgCol = lerp(sgCol, sgSand, max(sgBeach, smoothstep(0.2, 0.7, sgMat.r + (sgDM.a - 0.5) * 0.25) * (1.0 - sgFw)));
            // 岩石：陡坡与高山（三平面投影）
            float sgRockW = smoothstep(0.4, 0.62, sgSlope + (sgDM.a - 0.5) * 0.22 - sgMat.g * 0.12);
            sgRockW = max(sgRockW, smoothstep(4.4, 6.6, sgH + (sgDM.r - 0.5) * 1.6 + sgMat.g * 1.0));
            float3 sgBw = sgN * sgN; sgBw *= sgBw; sgBw /= (sgBw.x + sgBw.y + sgBw.z);
            float sgR = tex2D(_DetB, W3.zy * 0.19).r * sgBw.x + tex2D(_DetB, W3.xz * 0.19).r * sgBw.y + tex2D(_DetB, W3.xy * 0.19).r * sgBw.z;
            if (sgRockW > 0.004)
            {
                float3 sgRockC = lerp(float3(0.46, 0.44, 0.41), float3(0.56, 0.47, 0.37), clamp(sgMat.g, 0.0, 1.0)) * (0.55 + 0.75 * sgR);
                sgCol = lerp(sgCol, sgRockC, sgRockW);
                sgAO += (sgR - 0.5) * 0.25 * sgRockW;
            }
            // 雪：雪线以上、坡度不太陡处，边缘带噪声
            float sgSnowW = smoothstep(0.3, 0.62, sgMat.b + (sgDA.a - 0.5) * 0.5 + (sgDM.a - 0.5) * 0.3 - sgSlope * 0.7);
            sgCol = lerp(sgCol, float3(0.8, 0.83, 0.88) * (0.88 + 0.16 * sgDA.a), sgSnowW);
            // 水下的河床
            sgCol = lerp(sgCol, float3(0.6, 0.57, 0.45) * (0.85 + 0.2 * sgDA.b), smoothstep(-0.05, -0.7, sgH) * 0.75);
            sgCol *= 0.74 + 0.52 * clamp(sgAO, 0.0, 1.0);
            o.Albedo = sgCol;
            o.WN = float3(sgN.x, sgN.y, -sgN.z);
            // 环境光（天空 / 地平 / 地面三色，Unity 换算成球谐）按同一法线取；自发光在肩部曲线之前加入，顺序与网页版一致
            o.Emission = sgCol * ShadeSH9(float4(o.WN, 1.0));
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
