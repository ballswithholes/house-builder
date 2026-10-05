// Terrain (World / MapView): opaque, vertex-coloured, two painted textures mapped on the ground plane (world XY) and
// blended per vertex, lit like Lanternvale/LowPoly.
//   vertex colour rgb = tint over the painted ground (white = as painted); alpha = emission (normally 0)
//   uv0.x = blend: 0 = _MainTex (the map's ground) … 1 = _SideTex (the surroundings: meadow, hills)
//   uv0.y = detail: 0 = the textures' average colour (far hills) … 1 = the full painted texture
// _MainAvg / _SideAvg: the textures' average colours (measured by MapView), used where the detail fades out.
Shader "Lanternvale/Terrain"
{
    Properties
    {
        _MainTex ("Map ground", 2D) = "white" {}
        _SideTex ("Surroundings", 2D) = "white" {}
        _PlanarScale ("Ground planar scale (1 / metres per tile)", Float) = 0.125
        _SidePlanarScale ("Surroundings planar scale", Float) = 0.125
        _MainAvg ("Ground average colour", Color) = (0.6, 0.6, 0.5, 1)
        _SideAvg ("Surroundings average colour", Color) = (0.6, 0.69, 0.45, 1)
        _FogScale ("Fog scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Pass
        {
            Cull Back
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "LanternvaleCommon.cginc"

            sampler2D _MainTex;
            sampler2D _SideTex;
            float _PlanarScale;
            float _SidePlanarScale;
            fixed4 _MainAvg;
            fixed4 _SideAvg;
            float _FogScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv0 : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                half3 normal : TEXCOORD1;
                fixed4 color : COLOR;
                float2 blend : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = wp;
                o.pos = UnityWorldToClipPos(wp);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                o.blend = v.uv0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 g = tex2D(_MainTex, i.worldPos.xy * _PlanarScale).rgb;
                half3 s = tex2D(_SideTex, i.worldPos.xy * _SidePlanarScale).rgb;
                half b = saturate(i.blend.x);
                half3 tex = lerp(g, s, b);
                half3 avg = lerp(_MainAvg.rgb, _SideAvg.rgb, b);
                half3 albedo = LV_VertexColor(i.color.rgb) * lerp(avg, tex, saturate(i.blend.y));
                half3 n = normalize(i.normal);
                half3 col = LV_Shade(i.worldPos, n, albedo, i.color.a, 0.0, _FogScale);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
