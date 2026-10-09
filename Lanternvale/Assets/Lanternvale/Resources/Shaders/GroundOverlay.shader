// Unlit overlays lying on the ground plane: targeting previews (move range, AoE circles/cones/lines, paths), ground
// rings, bursts, aura pulses and heal glows. Drawn after the opaque world and the painted decals (Transparent-40),
// without depth writes and pulled towards the camera (polygon offset), so they sit on the terrain and decals but stay
// hidden behind units and props standing on them. Unlit on purpose: they must read the same by day and by night.
// Works on SpriteRenderers (vertex colour = sprite colour, _MainTex = sprite texture) and meshes.
// _SrcBlend/_DstBlend: SrcAlpha/OneMinusSrcAlpha (alpha, default) or SrcAlpha/One (additive glow).
Shader "Lanternvale/GroundOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _FogScale ("Fog scale", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent-40" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
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
            float _FogScale;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.worldPos);
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= 1.0 - LV_FogAmount(i.worldPos, _FogScale);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
