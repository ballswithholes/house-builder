// 水面：顶点波动 + 深浅渐变 + 高光
Shader "Sanguo/Water"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.07,0.27,0.48,1)
        _Shallow ("Shallow", Color) = (0.25,0.7,0.75,1)
        _Amp ("Wave Amplitude", Float) = 0.08
        _Alpha ("Alpha", Range(0,1)) = 0.86
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" }
        LOD 200
        CGPROGRAM
        #pragma surface surf BlinnPhong alpha:fade vertex:vert
        #pragma target 3.0
        fixed4 _Deep, _Shallow;
        float _Amp;
        half _Alpha;
        struct Input { float4 color : COLOR; float3 worldPos; };

        void vert (inout appdata_full v)
        {
            float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
            float t = _Time.y;
            float h = sin(t * 1.3 + w.x * 0.55 + w.z * 0.35) * 0.6 + sin(t * 0.9 - w.x * 0.27 + w.z * 0.71) * 0.4;
            v.vertex.y += h * _Amp;
            float dx = cos(t * 1.3 + w.x * 0.55 + w.z * 0.35) * 0.33 - cos(t * 0.9 - w.x * 0.27 + w.z * 0.71) * 0.11;
            float dz = cos(t * 1.3 + w.x * 0.55 + w.z * 0.35) * 0.21 + cos(t * 0.9 - w.x * 0.27 + w.z * 0.71) * 0.28;
            v.normal = normalize(float3(-dx * _Amp * 4, 1, -dz * _Amp * 4));
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            float shore = saturate(IN.color.r);
            float sparkle = sin(IN.worldPos.x * 3.1 + _Time.y * 2.0) * sin(IN.worldPos.z * 2.7 - _Time.y * 1.7);
            o.Albedo = lerp(_Deep.rgb, _Shallow.rgb, shore) + saturate(sparkle - 0.85) * 0.6;
            o.Specular = 0.35;
            o.Gloss = 0.8;
            o.Alpha = lerp(_Alpha, _Alpha * 0.75, shore);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
