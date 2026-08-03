Shader "Lingmai/NaturalPBR"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _FallbackSmoothness ("Fallback Smoothness", Range(0,1)) = 0.1
        _NormalStrength ("Normal Strength", Range(0,2)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        sampler2D _NormalMap;
        sampler2D _RoughnessMap;
        fixed4 _Tint;
        half _FallbackSmoothness;
        half _NormalStrength;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Tint;
            half3 normal = UnpackNormal(tex2D(_NormalMap, IN.uv_MainTex));
            normal.xy *= _NormalStrength;
            o.Albedo = albedo.rgb;
            o.Normal = normalize(normal);
            half roughness = tex2D(_RoughnessMap, IN.uv_MainTex).r;
            o.Smoothness = saturate(max(_FallbackSmoothness, 1.0h - roughness));
            o.Metallic = 0.0h;
            o.Occlusion = 1.0h;
            o.Emission = albedo.rgb * 0.07h;
        }
        ENDCG
    }
    FallBack "Standard"
}
