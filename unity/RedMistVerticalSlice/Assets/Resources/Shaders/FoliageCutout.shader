Shader "Lingmai/FoliageCutout"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _AlphaTex ("Alpha", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        sampler2D _AlphaTex;
        sampler2D _NormalMap;
        sampler2D _RoughnessMap;
        fixed4 _Tint;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Tint;
            o.Albedo = albedo.rgb;
            o.Alpha = 1.0h - tex2D(_AlphaTex, IN.uv_MainTex).r;
            o.Normal = UnpackNormal(tex2D(_NormalMap, IN.uv_MainTex));
            o.Smoothness = saturate(1.0h - tex2D(_RoughnessMap, IN.uv_MainTex).r);
            o.Metallic = 0.0h;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/VertexLit"
}
