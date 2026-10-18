// 道路（第三版）：贴地的柔边路面 ← 网页版 js/map-view.js 的 roadMaterial（LowPoly + 路面纹理，半透明、深度偏移）
// 纹理 u 横跨路面（两侧透明渐隐、两道车辙），v 沿路重复；顶点色为路面土色。与 LowPoly 相同的半兰伯特光照。
// decal:blend：画在地形之上（AlphaTest 队列、不写深度），仍可接收阴影。
Shader "Sanguo/Road"
{
    Properties
    {
        _MainTex ("Road", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        LOD 200
        Offset -1, -3
        CGPROGRAM
        #pragma surface surf HalfLambert decal:blend
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
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
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = t.rgb * IN.color.rgb * _Color.rgb;
            o.Alpha = t.a;
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
