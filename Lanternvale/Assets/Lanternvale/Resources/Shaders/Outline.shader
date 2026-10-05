// Ink outline (inverted hull) of constant screen width, drawn as a second material on the same mesh:
//   renderer.sharedMaterials = { LowPoly, Outline }  → Unity draws the (last) submesh again with this material.
// The hull is pushed out along the smoothed normal that MeshBuilder stores in the tangent (so flat-shaded models get
// a closed outline); wind and dither fade match Lanternvale/LowPoly.
// Per-renderer: _OutlineColor, _OutlineWidth (pixels at 1080p), _Fade, _WindScale, _Cut0.._Cut3 (as LowPoly).
Shader "Lanternvale/Outline"
{
    Properties
    {
        _OutlineColor ("Outline colour", Color) = (0.13, 0.09, 0.12, 1)
        _OutlineWidth ("Width (px at 1080p)", Float) = 2.2
        _Fade ("Fade (1 = solid)", Range(0,1)) = 1
        _WindScale ("Wind scale", Float) = 1
        _FogScale ("Fog scale", Float) = 1
        _Cut0 ("Cut-out 0 (xyz centre, w radius m)", Vector) = (0,0,0,0)
        _Cut1 ("Cut-out 1", Vector) = (0,0,0,0)
        _Cut2 ("Cut-out 2", Vector) = (0,0,0,0)
        _Cut3 ("Cut-out 3", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "IgnoreProjector"="True" }
        Pass
        {
            Cull Front
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "LanternvaleCommon.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _Fade;
            float _WindScale;
            float _FogScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 n = dot(v.tangent.xyz, v.tangent.xyz) > 0.01 ? v.tangent.xyz : v.normal;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                wp = LV_Wind(wp, v.uv1.x * _WindScale);
                o.worldPos = wp;
                float4 pos = UnityWorldToClipPos(wp);
                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, n));
                float2 nc = mul((float3x3)UNITY_MATRIX_VP, nw).xy;
                float len = length(nc);
                if (len > 0.00001)
                {
                    // pixels → clip space: 2 / screen size; scaled with resolution so the line looks the same at any size
                    float px = _OutlineWidth * (_ScreenParams.y / 1080.0);
                    pos.xy += (nc / len) * (px * 2.0 / _ScreenParams.xy) * pos.w;
                }
                o.pos = pos;
                o.screenPos = ComputeScreenPos(pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half cover = _Fade * LV_CutCoverage(i.worldPos, false);
                if (cover < 0.999)
                {
                    float2 pixel = (i.screenPos.xy / max(i.screenPos.w, 0.0001)) * _ScreenParams.xy;
                    clip(cover - LV_Dither(pixel) - 0.001);
                }
                half3 col = _OutlineColor.rgb;
                col = lerp(col, _LV_FogColor.rgb, LV_FogAmount(i.worldPos, _FogScale));
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
