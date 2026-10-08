// 低多边形半透明（战略地图的云：离镜头越近越透明）。与 LowPoly 相同的半兰伯特光照与顶点色，
// _Color.a 为不透明度。先只写深度，半透明的云只显示最外层的面，不会透出内部互相穿插的云团。
// 阴影：addshadow 生成不透明的投影；淡出时由 MapView 按不透明度开关 shadowCastingMode。
Shader "Sanguo/LowPolyFade"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        // 只写深度的前置遍
        Pass
        {
            ZWrite On
            ColorMask 0
        }

        CGPROGRAM
        #pragma surface surf HalfLambert alpha:fade addshadow
        #pragma target 3.0
        fixed4 _Color;
        half _Emission;
        struct Input { float4 color : COLOR; };

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
            o.Albedo = c;
            o.Emission = c * _Emission;
            o.Alpha = _Color.a;
        }
        ENDCG
    }
    FallBack Off
}
