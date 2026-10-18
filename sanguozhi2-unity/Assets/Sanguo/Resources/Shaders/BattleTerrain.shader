// 战场的平滑地形（第三版）← 网页版 js/battle-view.js 的 battleTerrainMaterial（= js/art.js terrainMaterial 的顶点版 + BT_GLSL + GRID_GLSL）
// 与 Sanguo/Terrain 的顶点版逐行相同（LowPoly 的半兰伯特光照之上，按坡度 / 高度 / 群落混合草地、干草、泥土、农田、沙、岩石、雪与海床，
// 再乘凹凸遮蔽、经高光肩部），之后：
//   棋盘内的地块外观（aBt = (山丘, 山岳, 寒冷度, 0)，TEXCOORD2）：山丘为干草色的赭黄草坡（丘顶亮、坡脚暗），
//   山岳为三平面投影的岩石（带层理）、坡脚的碎石，雪线以上积雪；
//   淡淡的格线（_GridK）与棋盘边框（_Board = (原点 x, 原点 z, W, H)，格坐标 = (世界 xz − 原点) / 2），棋盘外略暗、略褪色。
// 顶点：顶点色 = 群落底色，uv2（TEXCOORD1）= aMat（沙, 岩石倾向, 雪, 1 − 农田），uv3（TEXCOORD2）= aBt。
// 坐标：着色计算沿用网页版的 three 坐标（世界坐标 z 取反），以便公式逐行一致；光照前把法线换回 Unity 坐标。
// 纹理采样都放在分支外（避免分支里的梯度运算），按权重合成，结果与网页版的分支写法相同。
Shader "Sanguo/BattleTerrain"
{
    Properties
    {
        _DetA ("Detail A", 2D) = "gray" {}
        _DetB ("Detail B", 2D) = "gray" {}
        _Board ("Board (x0, z0, W, H)", Vector) = (400,0,18,13)
        _GridK ("Grid Strength", Float) = 0.16
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf TerrainHL vertex:vert finalcolor:shoulder fullforwardshadows addshadow noambient
        #pragma target 3.0
        sampler2D _DetA, _DetB;
        float4 _Board;
        float _GridK;
        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
            float4 mat;
            float4 bt;
        };
        struct SurfaceOutputTerrain
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;   // 世界坐标法线（Unity）
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.mat = v.texcoord1;
            o.bt = v.texcoord2;
        }

        // 与 LowPoly 相同的半兰伯特：ndl = (n·l·0.5 + 0.5)² × 1.15。环境光在 surf 里按同一法线自己加（自发光项）
        half4 LightingTerrainHL (SurfaceOutputTerrain s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }

        // 高光肩部：最终颜色的最大通道超过 0.8 后按指数曲线平滑趋近 1（保持色相）
        void shoulder (Input IN, SurfaceOutputTerrain o, inout fixed4 color)
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

        void surf (Input IN, inout SurfaceOutputTerrain o)
        {
            // three 坐标（z 取反）：vSgW、法线
            float3 W3 = float3(IN.worldPos.x, IN.worldPos.y, -IN.worldPos.z);
            float2 sgP = IN.worldPos.xz;
            float sgH = W3.y;
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
            // 草地：三级细节明暗 + 大尺度的干湿斑块 + 林下压暗
            float3 sgCol = sgTint * (0.76 + 0.5 * (sgDA.r * 0.36 + sgDF.r * 0.28 + sgDM.r * 0.36));
            if (sgNear > 0.01) sgCol *= 1.0 + (sgDN.r - 0.5) * 0.24 * sgNear;
            // 草色略降饱和，并按大尺度噪声做冷暖斑块
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
                    // 垄行：每块田一个方向（按田块编号），近处才画
                    float2 sgRD = float2(cos(sgDB.b * 6.2832), sin(sgDB.b * 6.2832));
                    sgCrop3 *= lerp(1.0, 0.86 + 0.14 * sin(dot(sgP, sgRD) * 38.0), sgNear * step(0.03, sgDB.b));
                }
                sgFw = smoothstep(0.3, 0.52, sgFarm + (sgDM.r - 0.5) * 0.55) * (1.0 - smoothstep(0.1, 0.26, sgSlope));
                sgCol = lerp(sgCol, sgCrop3, sgFw);
            }
            // 沙：海滩与河岸（按高度）、沙漠（按群落）
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
            // 棋盘山岳的岩石取样（BT_GLSL，比例 0.31；同样放在分支外）
            float btR = tex2D(_DetB, W3.zy * 0.31).r * sgBw.x + tex2D(_DetB, W3.xz * 0.31).r * sgBw.y + tex2D(_DetB, W3.xy * 0.31).r * sgBw.z;
            if (sgRockW > 0.004)
            {
                float3 sgRockC = lerp(float3(0.46, 0.44, 0.41), float3(0.56, 0.47, 0.37), clamp(sgMat.g, 0.0, 1.0)) * (0.55 + 0.75 * sgR);
                sgCol = lerp(sgCol, sgRockC, sgRockW);
                sgAO += (sgR - 0.5) * 0.25 * sgRockW;
            }
            // 雪：雪线以上、坡度不太陡处，边缘带噪声
            float sgSnowW = smoothstep(0.3, 0.62, sgMat.b + (sgDA.a - 0.5) * 0.5 + (sgDM.a - 0.5) * 0.3 - sgSlope * 0.7);
            sgCol = lerp(sgCol, float3(0.8, 0.83, 0.88) * (0.88 + 0.16 * sgDA.a), sgSnowW);
            // 水下的海床
            sgCol = lerp(sgCol, float3(0.6, 0.57, 0.45) * (0.85 + 0.2 * sgDA.b), smoothstep(-0.05, -0.7, sgH) * 0.75);
            sgCol *= 0.74 + 0.52 * clamp(sgAO, 0.0, 1.0);

            // ---- BT_GLSL：棋盘内的地块外观（山丘的干草坡、山岳的岩石与碎石、峰顶积雪）
            {
                float sgHl = clamp(IN.bt.x, 0.0, 1.0), sgMt = clamp(IN.bt.y, 0.0, 1.0);
                float3 c = sgCol;
                if (sgHl > 0.01)
                {
                    float lum = dot(c, float3(0.3, 0.59, 0.11));
                    float3 dry = float3(0.74, 0.69, 0.43) * (0.55 + 0.8 * lum) * (0.86 + 0.28 * sgDF.r) * (0.9 + 0.16 * smoothstep(0.95, 1.45, sgH));
                    c = lerp(c, dry, sgHl * (0.3 + 0.3 * sgDM.g));
                }
                if (sgMt > 0.01)
                {
                    float r = btR;
                    float strata = 0.9 + 0.12 * sin(sgH * 7.0 + r * 4.0);
                    float3 rock = lerp(float3(0.36, 0.34, 0.32), float3(0.47, 0.42, 0.36), sgDM.r) * (0.55 + 0.7 * r) * strata;
                    float3 scree = float3(0.52, 0.48, 0.42) * (0.75 + 0.4 * sgDA.g);
                    float base = 1.0 - smoothstep(1.05, 1.7, sgH + (sgDA.b - 0.5) * 0.3);
                    rock = lerp(rock, scree, base * 0.6);
                    c = lerp(c, rock, sgMt * (0.62 + 0.38 * smoothstep(1.0, 1.9, sgH)));
                    float snow = smoothstep(0.3, 0.6, IN.bt.z + (sgDA.a - 0.5) * 0.35 + (r - 0.5) * 0.3) * (1.0 - smoothstep(0.6, 0.88, sgSlope));
                    c = lerp(c, float3(0.86, 0.88, 0.92) * (0.9 + 0.12 * sgDA.a), snow);
                }
                sgCol = c;
            }
            // ---- GRID_GLSL：淡格线与棋盘边框（棋盘外略暗、略褪色；棋盘边缘一道细的暗边）
            {
                float2 bt = (IN.worldPos.xz - _Board.xy) * 0.5;
                float2 dOut = max(max(-bt, bt - _Board.zw), float2(0.0, 0.0));
                float outD = length(dOut);
                float ins = 1.0 - step(1e-4, outD);
                float2 g = abs(frac(bt) - 0.5);
                float2 fw = max(fwidth(bt), float2(1e-4, 1e-4));
                float2 ln2 = 1.0 - smoothstep(float2(0.0, 0.0), fw * 1.3 + 0.012, float2(0.5, 0.5) - g);
                float ln = max(ln2.x, ln2.y) * ins;
                float far = 1.0 - smoothstep(0.35, 0.9, max(fw.x, fw.y));
                sgCol *= 1.0 - _GridK * ln * far;
                float oo = smoothstep(0.0, 1.6, outD);
                float lum = dot(sgCol, float3(0.3, 0.59, 0.11));
                sgCol = lerp(sgCol, lerp(sgCol, lum.xxx, 0.25) * 0.84, oo);
                float2 e = min(bt, _Board.zw - bt);
                float edge = (1.0 - smoothstep(0.0, fw.x * 1.5 + 0.035, abs(min(e.x, e.y)))) * step(-0.05, min(e.x, e.y));
                sgCol *= 1.0 - 0.35 * edge;
            }

            o.Albedo = sgCol;
            o.WN = float3(sgN.x, sgN.y, -sgN.z);
            // 环境光（天空 / 地平 / 地面三色，Unity 换算成球谐）按同一法线取，与网页版的半球光相同；在肩部曲线之前加入
            o.Emission = sgCol * ShadeSH9(float4(o.WN, 1.0));
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
