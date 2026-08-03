Shader "Hidden/Lingmai/CinematicPostFx"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _TimeValue;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 color = tex2D(_MainTex, i.uv).rgb;
                // Keep enough shadow detail for navigation while retaining cinematic contrast.
                color = max(color, 0.0) * 1.025 + 0.008;
                color = (color - 0.5) * 1.07 + 0.5;
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(luminance.xxx, color, 1.035);
                float shadow = 1.0 - smoothstep(0.05, 0.55, luminance);
                float highlight = smoothstep(0.58, 1.0, luminance);
                color += shadow * float3(0.004, 0.007, 0.006);
                color += highlight * float3(0.018, 0.009, -0.006);
                float2 centered = i.uv * 2.0 - 1.0;
                float vignette = smoothstep(1.25, 0.34, dot(centered, centered));
                color *= lerp(0.88, 1.0, vignette);
                float grain = frac(sin(dot(i.uv * (_ScreenParams.xy + _TimeValue), float2(12.9898, 78.233))) * 43758.5453);
                color += (grain - 0.5) * 0.008;
                color = 1.0 - exp(-color * 1.08);
                color = pow(saturate(color), 0.98);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
