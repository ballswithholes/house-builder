// 攻击画面的地面（第三版）← 网页版 js/clash.js 的 groundMaterial（GROUND_VS / GROUND_FS 逐行移植）
// 在 LowPoly 光照（半兰伯特 ndl = (n·l·0.5 + 0.5)² × 1.15、阴影）之上加程序化细节（与战略地图同一套细节纹理 Art.TerrainDetailA / B）：
//   顶点色 = 底色；uv2（TEXCOORD1）= aGnd（泥土 / 踩踏 / 道路, 沙, 岩石倾向, 农田）。草地按三级细节纹理明暗、干湿斑块，
//   泥土与沙按噪声边缘混入，陡坡与高处为三平面投影的岩石（按主轴选一个投影面），雪线（_SnowH）以上积雪；
//   低频斑块噪声（周期约 14 单位）在顶点里采样后插值；近处（视距 < 30）才采样最细一级（_Lite = 1 时省掉，= 网页版触屏的 SG_LITE）。
// 环境光：网页版 LowPoly 补丁的三色半球光（天空 / 地平线 / 地面，按 n.y 插值）——全局 _SgClashSky / _SgClashEq / _SgClashGnd
//   （JS 颜色 × 强度 / π，由 Clash.cs 设定），noambient 关掉 Unity 自己的那份，在自发光里加。
// 坐标：着色计算沿用网页版的 three 坐标（本画面根 _SgClashOrigin 为原点，世界 z 取反），公式逐行一致。
Shader "Sanguo/ClashGround"
{
    Properties
    {
        _DetA ("Detail A", 2D) = "gray" {}
        _DetB ("Detail B", 2D) = "gray" {}
        _SnowH ("Snow Height", Float) = 99
        _Lite ("Lite (touch)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf ClashHL vertex:vert fullforwardshadows addshadow noambient
        #pragma target 3.0
        sampler2D _DetA, _DetB;
        float _SnowH, _Lite;
        float4 _SgClashOrigin;
        half4 _SgClashSky, _SgClashEq, _SgClashGnd;
        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
            float4 gnd;
            float4 cm;
            float vd;
        };
        struct SurfaceOutputClash
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.gnd = v.texcoord1;
            float3 w = mul(unity_ObjectToWorld, v.vertex).xyz - _SgClashOrigin.xyz;
            float3 p3 = float3(w.x, w.y, -w.z);
            o.cm = tex2Dlod(_DetA, float4(p3.xz * 0.071 + float2(0.31, 0.67), 0.0, 0.0));
            o.vd = -UnityObjectToViewPos(v.vertex).z;
        }
        // 与 LowPoly 相同的半兰伯特
        half4 LightingClashHL (SurfaceOutputClash s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        // 网页版 LowPoly 的三色环境光：dotNL = n·up；≥ 0 时 mix(地平线, 天空, dotNL)，否则 mix(地平线, 地面, −dotNL)
        half3 sgAmb (float ny)
        {
            return ny >= 0.0 ? lerp(_SgClashEq.rgb, _SgClashSky.rgb, ny) : lerp(_SgClashEq.rgb, _SgClashGnd.rgb, -ny);
        }
        float3 sgCropC (float id)
        {
            float3 c = float3(0.46, 0.6, 0.25);
            c = lerp(c, float3(0.78, 0.68, 0.33), step(0.24, id));
            c = lerp(c, float3(0.56, 0.44, 0.3), step(0.42, id));
            c = lerp(c, float3(0.36, 0.52, 0.22), step(0.56, id));
            c = lerp(c, float3(0.66, 0.64, 0.32), step(0.72, id));
            c = lerp(c, float3(0.5, 0.63, 0.28), step(0.87, id));
            return c;
        }
        void surf (Input IN, inout SurfaceOutputClash o)
        {
            float3 w = IN.worldPos - _SgClashOrigin.xyz;
            float3 vCW = float3(w.x, w.y, -w.z);
            float2 p = vCW.xz;
            float4 dA = tex2D(_DetA, p * 0.42);
            float4 dM = IN.cm;
            float dF = 0.5;
            if (_Lite < 0.5 && IN.vd < 30.0) dF = lerp(tex2D(_DetA, p * 1.6 + float2(0.53, 0.19)).r, 0.5, smoothstep(20.0, 30.0, IN.vd));
            float3 nU = normalize(IN.worldNormal);
            float3 n = float3(nU.x, nU.y, -nU.z);
            float slope = 1.0 - n.y;
            float3 base = IN.color.rgb;
            float3 col = base * (0.8 + 0.42 * (dA.r * 0.34 + dF * 0.3 + dM.r * 0.36));
            col = lerp(col, col * float3(1.1, 1.04, 0.8), smoothstep(0.5, 0.8, dM.g) * 0.5);
            col = lerp(col, col * float3(0.8, 0.88, 0.76), smoothstep(0.55, 0.85, dM.a) * 0.5);
            // 农田：田块、犁沟、田埂
            if (IN.gnd.w > 0.01)
            {
                float4 dB = tex2D(_DetB, p * 0.045 + float2(0.13, 0.71));
                float3 crop = sgCropC(dB.b) * (0.8 + 0.34 * dB.a);
                crop = lerp(crop, float3(0.6, 0.52, 0.38), step(dB.b, 0.03));
                col = lerp(col, crop, smoothstep(0.25, 0.6, IN.gnd.w + (dM.r - 0.5) * 0.4) * (1.0 - smoothstep(0.12, 0.26, slope)));
            }
            // 泥土：踩踏、道路
            float dW = smoothstep(0.3, 0.72, IN.gnd.x + (dM.a - 0.5) * 0.45 + (dA.g - 0.5) * 0.35);
            float3 dirt = lerp(float3(0.55, 0.46, 0.33), base * float3(0.95, 0.85, 0.7), 0.25) * (0.76 + 0.46 * dA.g);
            col = lerp(col, dirt, dW);
            // 沙：河岸、浅滩
            float sW = smoothstep(0.3, 0.7, IN.gnd.y + (dA.b - 0.5) * 0.3);
            float3 sand = float3(0.8, 0.74, 0.57) * (0.86 + 0.2 * dF + 0.08 * dA.b);
            col = lerp(col, sand, sW);
            // 岩石：陡坡与山巅（三平面投影：按主轴选一个投影面，一次采样，陡坡不拉伸）
            float rW = smoothstep(0.3, 0.52, slope + (dM.a - 0.5) * 0.22 + IN.gnd.z * 0.35);
            if (rW > 0.004)
            {
                float2 ruv = lerp(vCW.xy, vCW.zy, step(abs(n.z), abs(n.x)));
                ruv = lerp(ruv, vCW.xz, step(0.75, n.y));
                float r = tex2D(_DetB, ruv * 0.11).r;
                float3 rc = lerp(float3(0.5, 0.48, 0.45), float3(0.58, 0.5, 0.4), dM.r) * (0.58 + 0.72 * r);
                col = lerp(col, rc, rW);
            }
            // 雪
            float snW = smoothstep(_SnowH, _SnowH + 3.0, vCW.y + (dM.a - 0.5) * 4.0) * (1.0 - smoothstep(0.45, 0.75, slope));
            col = lerp(col, float3(0.86, 0.88, 0.92) * (0.9 + 0.12 * dA.a), snW);
            o.Albedo = col;
            o.WN = nU;
            o.Emission = col * sgAmb(nU.y);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
