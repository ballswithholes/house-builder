// 战略地图的水面（第三版）← 网页版 js/map-view.js 的 MAP_WATER_FS（逐行移植）
// 深浅按像素计算：采样地形高度（法线纹理 _MapN 的 A 通道 / 远景底色纹理 _OutC 的 A 通道，A = (h + 4) / 8），浅处青绿透亮、深处蓝；
// 岸边白色浪花与随时间推移的浪线，涟漪法线（三层滚动）+ 两道大涌，太阳高光、菲涅尔天空反射。网格本身是平的（y = 0，无接缝）。
// 坐标沿用网页版的 three 坐标（世界 z 取反），太阳方向与颜色取场景的主平行光（_WorldSpaceLightPos0 / _LightColor0），环境光取 unity_AmbientSky。
Shader "Sanguo/MapWater"
{
    Properties
    {
        _Ripple ("Ripple", 2D) = "gray" {}
        _MapN ("Map Normal/Height", 2D) = "gray" {}
        _OutC ("Outer Color/Height", 2D) = "gray" {}
        _XfN ("Xf N", Vector) = (0,0,1,1)
        _XfO ("Xf O", Vector) = (0,0,1,1)
        _Inner ("Inner Box", Vector) = (0,0,0,0)
        _Deep ("Deep", Color) = (0.05,0.24,0.45,1)
        _Shallow ("Shallow", Color) = (0.22,0.66,0.68,1)
        _Horizon ("Horizon", Color) = (0.86,0.87,0.84,1)
        _SkyColor ("Sky", Color) = (0.62,0.74,0.88,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            #include "Lighting.cginc"
            sampler2D _Ripple, _MapN, _OutC;
            float4 _XfN, _XfO, _Inner;
            fixed4 _Deep, _Shallow, _Horizon, _SkyColor;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 w : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert (appdata v)
            {
                v2f o;
                o.w = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            float sgTerrainH (float2 p)
            {
                float2 e = min(p - _Inner.xy, _Inner.zw - p);
                float wI = clamp(min(e.x, e.y) * 0.2, 0.0, 1.0);
                float ho = tex2D(_OutC, p * _XfO.zw + _XfO.xy).a * 8.0 - 4.0;
                float hn = tex2D(_MapN, p * _XfN.zw + _XfN.xy).a * 8.0 - 4.0;
                return ho * (1.0 - wI) + hn * wI;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 vW = float3(i.w.x, i.w.y, -i.w.z);
                float3 cam = float3(_WorldSpaceCameraPos.x, _WorldSpaceCameraPos.y, -_WorldSpaceCameraPos.z);
                float2 p = i.w.xz;
                float t = _Time.y;
                float depth = max(0.0, -sgTerrainH(p));
                float2 r1 = tex2D(_Ripple, p * 0.061 + float2(t * 0.011, t * 0.007)).rg * 2.0 - 1.0;
                float2 r2 = tex2D(_Ripple, p * 0.137 + float2(-t * 0.009, t * 0.013)).rg * 2.0 - 1.0;
                float2 r3 = tex2D(_Ripple, p * 0.019 + float2(t * 0.004, -t * 0.003)).rg * 2.0 - 1.0;
                float calm = lerp(0.45, 1.0, smoothstep(0.2, 2.5, depth));
                float2 sl = (r1 * 0.5 + r2 * 0.32 + r3 * 0.55) * 0.15 * calm;
                float a1 = t * 1.1 + p.x * 0.42 + p.y * 0.27, a2 = t * 0.8 - p.x * 0.21 + p.y * 0.55;
                sl += float2(cos(a1) * 0.42 - cos(a2) * 0.21, cos(a1) * 0.27 + cos(a2) * 0.55) * 0.03 * calm;
                float3 n = normalize(float3(-sl.x, 1.0, sl.y));
                float3 V = normalize(cam - vW);
                float3 Lu = normalize(_WorldSpaceLightPos0.xyz);
                float3 L = float3(Lu.x, Lu.y, -Lu.z);
                float3 sunC = _LightColor0.rgb;
                float3 amb = unity_AmbientSky.rgb;
                float dk = 1.0 - exp(-depth * 0.8);
                float3 water = lerp(_Shallow.rgb, _Deep.rgb, dk);
                float ndl = max(dot(n, L), 0.0) * 0.5 + 0.5;
                float3 col = water * (sunC * ndl * 0.8 + amb);
                float nv = max(dot(n, V), 0.0);
                float fres = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
                float3 R = reflect(-V, n);
                float3 refl = lerp(_Horizon.rgb, _SkyColor.rgb, clamp(R.y * 1.6, 0.0, 1.0));
                col = lerp(col, refl, clamp(fres * 0.85, 0.0, 0.8));
                float nh = max(dot(n, normalize(L + V)), 0.0);
                col += sunC * (pow(nh, 220.0) * 1.4 + pow(nh, 28.0) * 0.1) * lerp(0.4, 1.0, dk);
                // 岸边浪花
                float fN = 0.5 + 0.5 * r2.x;
                float shoreLine = 1.0 - smoothstep(0.0, 0.09 + 0.08 * fN, depth);
                float bands = smoothstep(0.6, 0.95, sin(depth * 15.0 - t * 1.7 + fN * 4.0)) * (1.0 - smoothstep(0.05, 0.55, depth));
                float foam = clamp(shoreLine + bands * 0.5, 0.0, 1.0) * (0.6 + 0.4 * fN);
                col = lerp(col, float3(0.95, 0.97, 0.96), foam * 0.82);
                float alpha = lerp(0.42, 0.93, smoothstep(0.0, 1.8, depth));
                alpha = max(alpha, foam * 0.9);
                fixed4 c = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
