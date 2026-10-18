// 攻击画面的草丛 / 芦苇 / 野花（第三版，实例化）← 网页版 js/clash.js 的 grassMaterial
// Graphics.DrawMeshInstanced：实例颜色 _InstColor（MaterialPropertyBlock 数组）；顶点色 a = aTint——aTint 为 1 的顶点乘实例颜色
// （草叶、花瓣），为 0 的保持原色（花茎）。随风轻摆：按实例的世界位置（three 坐标）取相位，顶点按高度在局部坐标里摆动
// （= 网页版 transformed.x += sin(t·2.1 + ph)·y·0.2、transformed.z += cos(t·1.6 + ph·1.3)·y·0.12；three 局部 z = −Unity 局部 z）。
// 时间 _SgClashTime = 本画面开始后的实际秒数（全局，Clash.cs 每帧设定）。不投影（绘制时关掉），接受阴影。
// 光照：LowPoly 的半兰伯特 + 阴影 + 网页版的三色环境光（全局 _SgClashSky / Eq / Gnd）。
Shader "Sanguo/ClashGrass"
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
        #pragma surface surf ClashHL vertex:vert fullforwardshadows addshadow noambient
        #pragma target 3.0
        #pragma multi_compile_instancing
        float4 _SgClashOrigin;
        float _SgClashTime;
        half4 _SgClashSky, _SgClashEq, _SgClashGnd;
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 color : COLOR; float3 worldNormal; };
        struct SurfaceOutputClash
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);
            float4 ic = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
            v.color.rgb *= lerp(float3(1, 1, 1), ic.rgb, v.color.a);
            float3 inst = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23) - _SgClashOrigin.xyz;
            float ph = inst.x * 0.9 - inst.z * 0.6;          // three：instanceMatrix[3].x · 0.9 + instanceMatrix[3].z · 0.6（three z = −Unity z）
            float t = _SgClashTime;
            v.vertex.x += sin(t * 2.1 + ph) * v.vertex.y * 0.2;
            v.vertex.z -= cos(t * 1.6 + ph * 1.3) * v.vertex.y * 0.12;
        }
        half4 LightingClashHL (SurfaceOutputClash s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        half3 sgAmb (float ny)
        {
            return ny >= 0.0 ? lerp(_SgClashEq.rgb, _SgClashSky.rgb, ny) : lerp(_SgClashEq.rgb, _SgClashGnd.rgb, -ny);
        }
        void surf (Input IN, inout SurfaceOutputClash o)
        {
            float3 c = IN.color.rgb;
            float3 nU = normalize(IN.worldNormal);
            o.Albedo = c;
            o.WN = nU;
            o.Emission = c * sgAmb(nU.y);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
