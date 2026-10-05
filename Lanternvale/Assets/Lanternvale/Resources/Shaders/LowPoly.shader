// Opaque world surfaces: vertex-coloured low-poly models (characters, creatures, props, terrain).
//   vertex colour rgb = albedo, alpha = emission (0 lit … 1 unlit glow, boosted at night)
//   uv0 = texture coordinates (optional painted texture, white by default); uv1.x = wind weight
//   _PlanarScale > 0 maps _MainTex onto the ground plane by world XY instead (terrain)
// Per-renderer (MaterialPropertyBlock): _Tint, _Flash (rgb + amount), _Fade (dither dissolve), _Rim, _FogScale, _WindScale.
Shader "Lanternvale/LowPoly"
{
    Properties
    {
        _MainTex ("Painted texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _PlanarScale ("Planar UV scale (0 = mesh UVs)", Float) = 0
        _TexStrength ("Texture strength", Range(0,1)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash (rgb, a = amount)", Color) = (1,1,1,0)
        _Fade ("Fade (1 = solid)", Range(0,1)) = 1
        _Rim ("Rim boost", Float) = 0
        _FogScale ("Fog scale", Float) = 1
        _WindScale ("Wind scale", Float) = 1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Pass
        {
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "LanternvaleCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _PlanarScale;
            float _TexStrength;
            fixed4 _Tint;
            fixed4 _Flash;
            float _Fade;
            float _Rim;
            float _FogScale;
            float _WindScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                half3 normal : TEXCOORD1;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp = LV_Wind(wp, v.uv1.x * _WindScale);
                o.worldPos = wp;
                o.pos = UnityWorldToClipPos(wp);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.uv = _PlanarScale > 0.0 ? wp.xy * _PlanarScale : TRANSFORM_TEX(v.uv0, _MainTex);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                if (_Fade < 0.999)
                {
                    float2 pixel = (i.screenPos.xy / max(i.screenPos.w, 0.0001)) * _ScreenParams.xy;
                    clip(_Fade - LV_Dither(pixel) - 0.001);
                }
                half3 n = normalize(i.normal) * (facing > 0 ? 1.0 : -1.0);
                half3 albedo = LV_VertexColor(i.color.rgb) * _Color.rgb * _Tint.rgb;
                half3 tex = tex2D(_MainTex, i.uv).rgb;
                albedo *= lerp(half3(1.0, 1.0, 1.0), tex, _TexStrength);
                half3 col = LV_Shade(i.worldPos, n, albedo, i.color.a, _Rim, _FogScale);
                col = lerp(col, _Flash.rgb, saturate(_Flash.a));
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
