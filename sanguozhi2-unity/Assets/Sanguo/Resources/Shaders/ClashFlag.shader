// 攻击画面的旗面（第三版）← 网页版 js/clash.js 的 flagMaterial（LowPoly 材质 + 贴图 + alphaTest 0.5 + 双面）
// 贴图 = 旗面（势力色底、犬牙边、圆徽与姓氏）；alpha < _Cutoff 的像素丢弃（犬牙边之间的空隙），投影同样按 alpha 裁剪。
// 网页版双面渲染、背面法线取反：Clash.cs 的 ClashFlag 给正反两面各建一份顶点（背面法线取反），这里只画正面（Cull Back）。
// 光照：LowPoly 的半兰伯特 + 网页版的三色环境光（全局 _SgClashSky / Eq / Gnd）；旗面不接受阴影（渲染器关闭）。
Shader "Sanguo/ClashFlag"
{
    Properties
    {
        _MainTex ("Banner", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        LOD 200
        CGPROGRAM
        #pragma surface surf ClashHL alphatest:_Cutoff fullforwardshadows addshadow noambient
        #pragma target 3.0
        sampler2D _MainTex;
        half4 _SgClashSky, _SgClashEq, _SgClashGnd;
        struct Input { float2 uv_MainTex; float4 color : COLOR; float3 worldNormal; };
        struct SurfaceOutputClash
        {
            fixed3 Albedo; fixed3 Normal; fixed3 Emission; half Specular; fixed Gloss; fixed Alpha;
            float3 WN;
        };
        half4 LightingClashHL (SurfaceOutputClash s, half3 lightDir, half atten)
        {
            half ndl = dot(s.WN, lightDir) * 0.5 + 0.5;
            ndl = ndl * ndl;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (ndl * atten * 1.15);
            c.a = s.Alpha;
            return c;
        }
        half3 sgAmb (float ny)
        {
            return ny >= 0.0 ? lerp(_SgClashEq.rgb, _SgClashSky.rgb, ny) : lerp(_SgClashEq.rgb, _SgClashGnd.rgb, -ny);
        }
        void surf (Input IN, inout SurfaceOutputClash o)
        {
            fixed4 tx = tex2D(_MainTex, IN.uv_MainTex);
            float3 c = tx.rgb * IN.color.rgb;
            float3 nU = normalize(IN.worldNormal);
            o.Albedo = c;
            o.WN = nU;
            o.Emission = c * sgAmb(nU.y);
            o.Alpha = tx.a * IN.color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
