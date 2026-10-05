// Lit, alpha-blended textured surfaces: painted ground decals (paths, flower beds, blight), foliage cards, water.
// Texture × vertex colour × _Color, lit like Lanternvale/LowPoly; drawn after opaque geometry without depth writes.
Shader "Lanternvale/LitTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _Emission ("Emission", Range(0,1)) = 0
        _FogScale ("Fog scale", Float) = 1
        _WindScale ("Wind scale", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            #include "LanternvaleCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Emission;
            float _FogScale;
            float _WindScale;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; half3 normal : TEXCOORD1; fixed4 color : COLOR; float2 uv : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp = LV_Wind(wp, v.uv1.x * _WindScale);
                o.worldPos = wp;
                o.pos = UnityWorldToClipPos(wp);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                half3 n = normalize(i.normal) * (facing > 0 ? 1.0 : -1.0);
                half3 albedo = t.rgb * LV_VertexColor(i.color.rgb) * _Color.rgb;
                half3 col = LV_Shade(i.worldPos, n, albedo, _Emission, 0.0, _FogScale);
                return fixed4(col, t.a * i.color.a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
