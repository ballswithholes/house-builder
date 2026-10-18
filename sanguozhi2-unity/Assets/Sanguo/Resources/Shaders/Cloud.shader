// 战略地图的云（第三版：柔边公告板云团）← 网页版 js/map-view.js 的 CLOUD_VS / CLOUD_FS
// 每朵云 5–8 片朝向镜头的面片：顶点位置 = 云团中心（模型坐标），uv = 角点 (±1, ±1)，uv2.x = 半径、uv2.y = 明暗；
// 在视空间里把角点展开成方形，取共用的云团贴图（径向渐隐 + 两级噪声，上亮下灰）× _Tint × 明暗，不透明度 _Opacity（每朵云一份材质）。不投影。
Shader "Sanguo/Cloud"
{
    Properties
    {
        _MainTex ("Cloud", 2D) = "white" {}
        _Opacity ("Opacity", Float) = 1
        _Tint ("Tint", Color) = (1.0,0.99,0.96,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+21" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Opacity;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 corner : TEXCOORD0; float2 ss : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float shade : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert (appdata v)
            {
                v2f o;
                float4 vp = float4(UnityObjectToViewPos(v.vertex.xyz), 1.0);
                vp.xy += v.corner * v.ss.x;
                o.uv = float2(v.corner.x * 0.5 + 0.5, 0.5 - v.corner.y * 0.5);
                o.shade = v.ss.y;
                o.pos = mul(UNITY_MATRIX_P, vp);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed4 c = fixed4(t.rgb * _Tint.rgb * i.shade, t.a * _Opacity);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent"
}
