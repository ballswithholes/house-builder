// 海路虚线（第三版修订）← 网页版 js/map-view.js 的 LANE_VS / LANE_FS
// 顶点只存中线位置，半宽方向放在 uv（地图坐标 x / z）：半宽 _W × clamp(镜头距离 / 60, 0.35, 1)（近看变细），
// 镜头距离 12–28 内淡出；略带蓝的白色、不透明度 _Opacity。双面、深度偏移，不写深度。
Shader "Sanguo/SeaLane"
{
    Properties
    {
        _Color ("Color", Color) = (0.88,0.93,0.98,1)
        _Opacity ("Opacity", Float) = 0.62
        _W ("Half Width", Float) = 0.16
    }
    SubShader
    {
        Tags { "Queue"="Transparent+3" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -4
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _Opacity, _W;
            struct appdata { float4 vertex : POSITION; float2 off : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float a : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert (appdata v)
            {
                v2f o;
                float4 wp = mul(unity_ObjectToWorld, v.vertex);
                float d = distance(_WorldSpaceCameraPos, wp.xyz);
                wp.xz += v.off * _W * clamp(d / 60.0, 0.35, 1.0);
                o.a = smoothstep(12.0, 28.0, d);
                o.pos = mul(UNITY_MATRIX_VP, wp);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = fixed4(_Color.rgb, _Opacity * i.a);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent"
}
