// 战场的实例化点缀（第三版：树、石、草丛、灌木、村舍、船以外的小件）← 网页版 js/battle-view.js 的 InstancedMesh + Gfx.lowPoly()
// 与 Sanguo/LowPoly 相同的半兰伯特光照与顶点色；实例色 _InstColor（Graphics.DrawMeshInstanced 的 MaterialPropertyBlock 数组）
// 乘在整个顶点色上（= three 的 instanceColor）。不透明。挡住部队时淡去的树用 Sanguo/BattlePropFade。
Shader "Sanguo/BattleProp"
{
    Properties
    {
        _InstColor ("Instance Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf HalfLambert vertex:vert fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 color : COLOR; };

        void vert (inout appdata_full v)
        {
            float4 ic = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
            v.color.rgb *= ic.rgb;
        }

        // 柔和的半兰伯特（与 LowPoly 相同）
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
            o.Albedo = IN.color.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
