Shader "Lingmai/SanctuaryWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.09,0.31,0.30,0.58)
        _DeepColor ("Deep Color", Color) = (0.015,0.09,0.11,0.78)
        _Smoothness ("Smoothness", Range(0,1)) = 0.88
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 260
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert
        #pragma target 3.0
        #include "UnityCG.cginc"
        fixed4 _ShallowColor;
        fixed4 _DeepColor;
        half _Smoothness;
        struct Input { float3 worldPos; float3 viewDir; };
        void vert(inout appdata_full v)
        {
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            v.vertex.y += sin(world.z * 0.72 + _Time.y * 1.25) * 0.035;
            v.vertex.y += sin(world.x * 1.18 - _Time.y * 0.83) * 0.022;
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float waveA = sin(IN.worldPos.z * 0.72 + _Time.y * 1.25);
            float waveB = cos(IN.worldPos.x * 1.18 - _Time.y * 0.83);
            o.Normal = normalize(float3(waveB * 0.15, 1.0, waveA * 0.16));
            half fresnel = pow(1.0h - saturate(dot(normalize(IN.viewDir), float3(0,1,0))), 3.0h);
            fixed4 water = lerp(_ShallowColor, _DeepColor, 0.35h + fresnel * 0.5h);
            o.Albedo = water.rgb;
            o.Smoothness = _Smoothness;
            o.Metallic = 0.05h;
            o.Alpha = saturate(water.a + fresnel * 0.18h);
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
