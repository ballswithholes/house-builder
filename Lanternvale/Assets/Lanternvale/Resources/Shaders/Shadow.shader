// Soft contact (blob) shadow on the ground: a quad whose UVs span 0..1; darkness falls off radially. Drawn just above
// the terrain (polygon offset) under units and props. _Color.a = darkness.
Shader "Lanternvale/Shadow"
{
    Properties
    {
        _Color ("Shadow colour (a = strength)", Color) = (0.12, 0.09, 0.16, 0.42)
        _Softness ("Softness", Range(0.05, 1)) = 0.65
    }
    SubShader
    {
        // after the painted ground decals (LitTransparent, Transparent-50), before the ground previews (Transparent-44…)
        Tags { "Queue"="Transparent-45" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Softness;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv);
                float a = 1.0 - smoothstep(1.0 - _Softness, 1.0, d);
                return fixed4(_Color.rgb, _Color.a * a * i.color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
