// Sky dome / backdrop: unlit vertex colours (gradient baked by the builder), drawn first, never fogged, no depth writes.
// _Tint multiplies (time of day). Vertex alpha is ignored.
Shader "Lanternvale/Sky"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "IgnoreProjector"="True" "PreviewType"="Skybox" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "LanternvaleCommon.cginc"

            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(LV_VertexColor(i.color.rgb) * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
