// 必杀技特效（BattleView.SpecialFx）：网页版 battle-view.js 的 SP_ARC / SP_BOLT / SP_PILLAR / SP_WAVE / SP_DOME 片元着色器
//   _Mode 0 弧光（月牙形刀光，_Head / _Len 控制彗星头尾，_Soft > 0 为外层柔光）
//         1 闪电 / 拖尾（横向高斯，芯白）  2 光柱（自下而上渐隐，螺旋条纹流动）
//         3 水墙（浪身半透明、浪尖泛白）    4 护盾（菲涅耳边缘 + 上升扫描环 + 六角网格）
//   混合方式（_SrcBlend / _DstBlend）、深度测试（_ZTest）、剔除（_Cull）由脚本设定：叠加 = SrcAlpha One，普通 = SrcAlpha OneMinusSrcAlpha
Shader "Sanguo/SpecialFx"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Mode ("Mode", Float) = 0
        _Opacity ("Opacity", Float) = 1
        _Head ("Head", Float) = 0
        _Len ("Len", Float) = 0.75
        _Soft ("Soft", Float) = 0
        _FxTime ("Time", Float) = 0
        _Scan ("Scan", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest [_ZTest]
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Mode, _Opacity, _Head, _Len, _Soft, _FxTime, _Scan;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 n : TEXCOORD1;     // 视空间法线
                float3 v : TEXCOORD2;     // 视空间：指向相机
                float3 p : TEXCOORD3;     // 模型空间位置
            };

            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.uv = i.uv;
                float3 vp = UnityObjectToViewPos(i.vertex.xyz);
                o.n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, i.normal));
                o.v = normalize(-vp);
                o.p = i.vertex.xyz;
                return o;
            }

            // GLSL 语义的 smoothstep（允许 e0 > e1）
            float ss(float e0, float e1, float x) { float t = saturate((x - e0) / (e1 - e0)); return t * t * (3.0 - 2.0 * t); }

            float4 frag (v2f i) : SV_Target
            {
                float3 col = _Color.rgb;
                float2 uv = i.uv;
                if (_Mode < 0.5)
                {
                    // 弧光：月牙形刀光（两端尖、中段厚），外缘白热
                    float d = _Head - uv.x;
                    if (d < 0.0 || d > _Len) discard;
                    float rel = 1.0 - d / _Len;
                    float th = pow(max(sin(rel * 3.14159), 0.0), 0.75) * 0.92;
                    float y = uv.y;
                    float e = 0.04 + _Soft * 0.35;
                    float inside = ss(1.0 - th - e, 1.0 - th + e * 0.5, y) * ss(1.0, 1.0 - e - 0.02, y);
                    float hot = ss(1.0 - th * 0.4, 1.0, y) * (1.0 - _Soft);
                    float a = inside * _Opacity * (0.6 + 0.4 * rel);
                    return float4(lerp(col, float3(1.0, 1.0, 1.0), hot * 0.9), a);
                }
                if (_Mode < 1.5)
                {
                    // 闪电 / 拖尾：横向高斯，芯白
                    float x = (uv.y - 0.5) * 2.0;
                    float core = exp(-x * x * 26.0);
                    float glow = exp(-x * x * 3.0) * 0.55;
                    float along = ss(0.0, 0.08, uv.x) * ss(1.0, 0.92, uv.x);
                    float a = (core + glow) * _Opacity * lerp(0.6, 1.0, along);
                    return float4(lerp(col, float3(1.0, 1.0, 1.0), core), a);
                }
                if (_Mode < 2.5)
                {
                    // 光柱：自下而上渐隐，螺旋条纹流动
                    float fade = pow(max(1.0 - uv.y, 0.0), 1.5) * ss(0.0, 0.05, uv.y);
                    float st = 0.6 + 0.4 * sin(uv.x * 6.2831 * 5.0 + uv.y * 9.0 - _FxTime * 7.0);
                    float a = fade * st * _Opacity;
                    return float4(lerp(col, float3(1.0, 1.0, 1.0), 0.45 * fade), a);
                }
                if (_Mode < 3.5)
                {
                    // 水墙：浪身半透明、浪尖泛白
                    float y = uv.y + 0.06 * sin(uv.x * 40.0 + _FxTime * 9.0);
                    float crest = ss(0.62, 0.9, y) * (1.0 - ss(0.93, 1.0, y));
                    float body = ss(0.0, 0.2, y) * (1.0 - ss(0.85, 1.0, y)) * (0.55 + 0.25 * sin(uv.x * 70.0 - _FxTime * 6.0));
                    float edge = sin(saturate(uv.x) * 3.14159);
                    float a = (body * 0.85 + crest) * edge * _Opacity;
                    return float4(lerp(col, float3(1.0, 1.0, 1.0), crest * 0.85), a);
                }
                // 护盾：菲涅耳边缘 + 上升扫描环 + 六角网格
                float f = pow(1.0 - abs(dot(normalize(i.n), normalize(i.v))), 2.0);
                float bd = (i.p.y - _Scan) * 10.0;
                float band = exp(-bd * bd);
                float ang = atan2(i.p.z, i.p.x) * 6.0 / 3.14159;
                float lat = i.p.y * 9.0;
                float hex = max(ss(0.86, 1.0, abs(frac(ang + floor(lat) * 0.5) * 2.0 - 1.0)), ss(0.82, 1.0, abs(frac(lat) * 2.0 - 1.0)));
                float a = (0.08 + f * 0.85 + band * 0.9 + hex * 0.22 * (0.4 + f)) * _Opacity;
                return float4(lerp(col, float3(1.0, 1.0, 1.0), saturate(band * 0.7 + f * 0.3)), a);
            }
            ENDCG
        }
    }
}
