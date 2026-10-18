// 远景树：朝向镜头的公告板（第三版，实例化）← 网页版 js/map-view.js 的 SPRITE_VS / SPRITE_FS
// 实例矩阵只含位置与缩放（宽 = 树种面片宽 × 实例缩放，高同理）；面片的右向量取镜头右方，上向量取镜头上方与竖直方向的平均。
// 图集 4 × 4（_Cell = 树种格），灰度树冠乘实例颜色 _InstColor，乘常量光照 _Light；alpha 测试（0.42）+ AlphaToMask（多重采样时边缘平滑）。
Shader "Sanguo/TreeSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Light ("Light", Color) = (1.02,1.0,0.94,1)
        _InstColor ("Instance Color", Color) = (1,1,1,1)
        _Cell ("Cell", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Pass
        {
            AlphaToMask On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Light;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _Cell)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 tint : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 c = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float3 R = UNITY_MATRIX_V[0].xyz;
                float3 U = normalize(lerp(UNITY_MATRIX_V[1].xyz, float3(0, 1, 0), 0.5));
                float3 p = c + R * v.vertex.x * sx + U * v.vertex.y * sy;
                float cellI = UNITY_ACCESS_INSTANCED_PROP(Props, _Cell);
                float2 cell = float2(fmod(cellI, 4.0), floor(cellI / 4.0));
                float2 uv = (cell + float2(v.uv.x, 1.0 - v.uv.y)) / 4.0;
                uv.y = 1.0 - uv.y;
                o.uv = uv;
                o.tint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor).rgb;
                o.pos = mul(UNITY_MATRIX_VP, float4(p, 1.0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                clip(t.a - 0.42);
                fixed4 c = fixed4(t.rgb * i.tint * _Light.rgb, t.a);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent Cutout"
}
