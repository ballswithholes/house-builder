// 单挑画面（河川）的水面 ← 网页版 js/art.js 的 Gfx.water()（WATER_VS / WATER_FS 逐行移植；单挑直接用 Gfx.water()，
// 参数取其缺省：uAmp 0.08、uAlpha 0.86、主场景太阳与环境光的常量）。
// 顶点：两道正弦波起伏（顶点色 g = 1 的部分不起伏；单挑的水面 g = 0），顶点色 r = 离岸的远近（浅色、浪花、透明度）。
// 片元：深浅插值 + 闪光点（远处一个周期不足几个像素时淡出），BlinnPhong 高光（法线 = 正弦波 + 两层滚动的涟漪贴图），
// 掠射角的天空反射、朝天空盒太阳方向的柔和光带与碎光、岸边浪花；半透明、不写深度、受雾影响。
// 太阳、环境光、光带都是网页版写死的常量（与场景灯光无关）：_SunDir3 / _SheenDir3 为 three 坐标与 JS 数值。
// 坐标：位置换成 three 世界坐标（单挑场地的根在世界原点，z 取反），波纹相位用 Unity 坐标 (ux, uz)，与网页版相同。
Shader "Sanguo/DuelWater"
{
    Properties
    {
        _Ripple ("Ripple", 2D) = "gray" {}
        _Amp ("Wave Amplitude", Float) = 0.08
        _Alpha ("Alpha", Range(0,1)) = 0.86
        _Deep ("Deep", Color) = (0.07,0.27,0.48,1)
        _Shallow ("Shallow", Color) = (0.25,0.7,0.75,1)
        _SunDir3 ("Sun Dir (three)", Vector) = (0.383797,0.743145,0.54812,0)
        _SunColor ("Sun Color", Color) = (0.8625,0.81075,0.7245,1)
        _Ambient ("Ambient", Color) = (0.3472,0.3808,0.448,1)
        _SheenDir3 ("Sheen Dir (three)", Vector) = (0.30421,0.40562,-0.86194,0)
        _SheenColor ("Sheen Color", Color) = (1,0.93,0.78,1)
        _SkyColor ("Sky Color", Color) = (0.8,0.84,0.86,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _Ripple;
            float _Amp, _Alpha;
            fixed4 _Deep, _Shallow, _SunColor, _Ambient, _SheenColor, _SkyColor;
            float4 _SunDir3, _SheenDir3;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 w3 : TEXCOORD0; float shore : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert (appdata v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float ux = w.x, uz = w.z;           // 网页版 ux = w.x、uz = −w.z（three）= Unity 坐标
                float t = _Time.y;
                float h = sin(t * 1.3 + ux * 0.55 + uz * 0.35) * 0.6 + sin(t * 0.9 - ux * 0.27 + uz * 0.71) * 0.4;
                o.shore = v.color.r;
                float waveK = 1.0 - v.color.g;
                w.y += h * _Amp * waveK;
                o.w3 = float3(w.x, w.y, -w.z);
                o.pos = mul(UNITY_MATRIX_VP, float4(w, 1.0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float ux = i.w3.x;
                float uz = -i.w3.z;
                float t = _Time.y;
                float a1 = t * 1.3 + ux * 0.55 + uz * 0.35;
                float a2 = t * 0.9 - ux * 0.27 + uz * 0.71;
                float dx = cos(a1) * 0.33 - cos(a2) * 0.11;
                float dz = cos(a1) * 0.21 + cos(a2) * 0.28;
                // Unity 法线 (−dx·A·4, 1, −dz·A·4)，换到 three（z 取反）
                float3 n = normalize(float3(-dx * _Amp * 4.0, 1.0, dz * _Amp * 4.0));
                float shore = saturate(i.shore);
                // 闪光点；远处一个周期不足几个像素时淡出，避免摩尔纹
                float fw = max(fwidth(ux * 3.1), fwidth(uz * 2.7));
                float sparkle = sin(ux * 3.1 + t * 2.0) * sin(uz * 2.7 - t * 1.7);
                float3 albedo = lerp(_Deep.rgb, _Shallow.rgb, shore) + saturate(sparkle - 0.85) * 0.6 * (1.0 - smoothstep(0.3, 0.9, fw));
                float3 camU = _WorldSpaceCameraPos;
                float3 cam3 = float3(camU.x, camU.y, -camU.z);
                float3 V = normalize(cam3 - i.w3);
                // 高光用的法线：正弦波 + 两层滚动的涟漪贴图（不规则，远处 mip 自动变平）
                float2 r1 = tex2D(_Ripple, float2(ux, uz) * 0.045 + float2(t * 0.012, t * 0.008)).rg * 2.0 - 1.0;
                float2 r2 = tex2D(_Ripple, float2(ux, uz) * 0.093 + float2(-t * 0.010, t * 0.015)).rg * 2.0 - 1.0;
                float2 rip = (r1 + r2 * 0.7) * 0.17;
                float3 ns = normalize(float3(-dx * _Amp * 2.0 + rip.x, 1.0, dz * _Amp * 2.0 - rip.y));
                // BlinnPhong（Specular 0.35 → 指数 44.8，Gloss 0.8，_SpecColor 0.5）
                float3 L = normalize(_SunDir3.xyz);
                float ndl = max(dot(n, L), 0.0);
                float spec = pow(max(dot(ns, normalize(L + V)), 0.0), 44.8) * 0.8;
                float3 col = albedo * (_SunColor.rgb * ndl + _Ambient.rgb) + _SunColor.rgb * 0.5 * spec;
                // 风格化：掠射角天空反射 + 朝天空盒太阳方向的柔和光带与碎光
                float3 up = float3(0.0, 1.0, 0.0);
                float fres = pow(1.0 - max(dot(up, V), 0.0), 5.0);
                float3 S = normalize(_SheenDir3.xyz);
                float glow = pow(max(dot(reflect(-V, up), S), 0.0), 10.0);
                float glint = pow(max(dot(ns, normalize(S + V)), 0.0), 60.0);
                col = lerp(col, _SkyColor.rgb, fres * 0.45) + _SheenColor.rgb * (glow * 0.16 + glint * 0.26);
                // 岸边浪花
                float foam = smoothstep(0.86, 0.99, shore) * (0.55 + 0.45 * sin(t * 1.6 + (ux + uz) * 0.9 + shore * 18.0));
                col = lerp(col, float3(0.93, 0.95, 0.92), foam * 0.22);
                float alpha = lerp(_Alpha, _Alpha * 0.75, shore);
                fixed4 c = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
