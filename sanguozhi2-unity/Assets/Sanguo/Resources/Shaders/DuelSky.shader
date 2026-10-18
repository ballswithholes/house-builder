// 单挑画面的天空（天空盒）← 网页版 js/duel-game.js 的 SKY_VS / SKY_FS（逐行移植）：
// 地平线 → 天顶 / 地面的渐变、太阳的三层光晕（指数 220 / 12 / 3）、地平线上方一道淡淡的云带（按方向的正弦起伏）。
// 方向换成 three 坐标（z 取反）后计算，_SunDir 为 three 坐标（与 JS 调色板的 sunDir 同值）。不受雾影响。
Shader "Sanguo/DuelSky"
{
    Properties
    {
        _Top ("Top", Color) = (0.12,0.21,0.37,1)
        _Hor ("Horizon", Color) = (0.91,0.6,0.36,1)
        _Low ("Low", Color) = (0.35,0.26,0.2,1)
        _Sun ("Sun", Color) = (1,0.81,0.54,1)
        _SunDir ("Sun Direction (three)", Vector) = (-0.33,0.13,-0.94,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            fixed4 _Top, _Hor, _Low, _Sun;
            float4 _SunDir;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 du = normalize(i.dir);
                float3 d = float3(du.x, du.y, -du.z);
                float y = d.y;
                float3 c = y > 0.0 ? lerp(_Hor.rgb, _Top.rgb, pow(clamp(y * 1.6, 0.0, 1.0), 0.6)) : lerp(_Hor.rgb, _Low.rgb, clamp(-y * 4.0, 0.0, 1.0));
                float s = max(dot(d, normalize(_SunDir.xyz)), 0.0);
                c += _Sun.rgb * (pow(s, 220.0) * 1.2 + pow(s, 12.0) * 0.28 + pow(s, 3.0) * 0.1);
                float band = sin(d.x * 9.0 + d.z * 4.0) * 0.5 + 0.5;
                c = lerp(c, c * 1.06 + float3(0.02, 0.02, 0.02), smoothstep(0.08, 0.2, y) * (1.0 - smoothstep(0.2, 0.45, y)) * band * 0.6);
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
