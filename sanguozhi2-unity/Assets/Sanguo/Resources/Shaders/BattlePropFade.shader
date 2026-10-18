// 战场上挡住部队时淡去的树（第三版）← 网页版 js/battle-view.js 的 propFadeMaterial（LowPoly + 逐实例 aFade）
// 不透明度 = 1 − 0.75 · _Fade（逐实例，Graphics.DrawMeshInstanced 的 MaterialPropertyBlock 数组），实例色 _InstColor 同 Sanguo/BattleProp。
// 网页版为透明材质且写深度（renderOrder 5：在水面、高亮、光环之后绘制）：这里先用一遍只写深度的实例化前置遍，
// 半透明的树只显示最外层的面（同 Sanguo/LowPolyFade），再按 alpha:fade 混合。BattleView 只把正在淡去（_Fade > 0）的树
// 交给本材质，其余的树仍用不透明的 Sanguo/BattleProp（接受阴影）。
Shader "Sanguo/BattlePropFade"
{
    Properties
    {
        _InstColor ("Instance Color", Color) = (1,1,1,1)
        _Fade ("Fade", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        // 只写深度的前置遍（实例化）
        Pass
        {
            ZWrite On
            ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return 0; }
            ENDCG
        }

        CGPROGRAM
        #pragma surface surf HalfLambert vertex:vert alpha:fade addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
            UNITY_DEFINE_INSTANCED_PROP(float, _Fade)
        UNITY_INSTANCING_BUFFER_END(Props)
        struct Input { float4 color : COLOR; };

        void vert (inout appdata_full v)
        {
            float4 ic = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
            v.color.rgb *= ic.rgb;
            // 不透明度放进顶点色的 a（几何本色的 a 为 1）
            v.color.a = 1.0 - 0.75 * saturate(UNITY_ACCESS_INSTANCED_PROP(Props, _Fade));
        }

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
            o.Alpha = IN.color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
