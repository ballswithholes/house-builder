// 低多边形：顶点色 + Lambert 光照 + 阴影 + 雾
Shader "Sanguo/LowPoly"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0,2)) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf HalfLambert fullforwardshadows addshadow
        #pragma target 3.0
        fixed4 _Color;
        half _Emission;
        fixed4 _FlashColor;
        half _Flash;
        struct Input { float4 color : COLOR; };

        // 柔和的半兰伯特，让低多边形的明暗过渡更好看
        half4 LightingHalfLambert (SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed3 c = IN.color.rgb * _Color.rgb;
            o.Albedo = lerp(c, _FlashColor.rgb, _Flash);
            o.Emission = c * _Emission + _FlashColor.rgb * _Flash * 0.6;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
