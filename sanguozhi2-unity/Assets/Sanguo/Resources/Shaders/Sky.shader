// 天空盒：三段渐变 + 太阳光晕
Shader "Sanguo/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.32,0.55,0.86,1)
        _Horizon ("Horizon", Color) = (0.93,0.88,0.78,1)
        _Bottom ("Bottom", Color) = (0.55,0.62,0.66,1)
        _SunDir ("Sun Direction", Vector) = (0.3,0.4,0.85,0)
        _SunColor ("Sun Color", Color) = (1,0.9,0.7,1)
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
            #include "UnityCG.cginc"
            fixed4 _Top, _Horizon, _Bottom, _SunColor;
            float4 _SunDir;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                fixed3 c = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), 0.55)) : lerp(_Horizon.rgb, _Bottom.rgb, saturate(-y * 3));
                float s = saturate(dot(d, normalize(_SunDir.xyz)));
                c += _SunColor.rgb * (pow(s, 64) * 0.9 + pow(s, 6) * 0.18);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
