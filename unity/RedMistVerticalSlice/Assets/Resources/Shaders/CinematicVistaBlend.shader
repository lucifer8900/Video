Shader "Lingmai/CinematicVistaBlend"
{
    Properties
    {
        _MainTex ("Vista", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0.25,2)) = 1
        _ShadowLift ("Shadow Lift", Range(0,0.2)) = 0.025
        _EdgeFeather ("Side / Top Feather", Range(0.001,0.25)) = 0.065
        _BottomFeather ("Bottom Feather", Range(0.001,0.35)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent-80"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Exposure;
            half _ShadowLift;
            half _EdgeFeather;
            half _BottomFeather;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 plate = tex2D(_MainTex, input.uv);
                half side = smoothstep(0.0h, _EdgeFeather, min(input.uv.x, 1.0h - input.uv.x));
                half bottom = smoothstep(0.0h, _BottomFeather, input.uv.y);
                half top = smoothstep(0.0h, _EdgeFeather, 1.0h - input.uv.y);
                half alpha = plate.a * _Color.a * side * bottom * top;

                // Keep the authored distant architecture readable. The feathered alpha and
                // matching grade join it to the valley without distance fog erasing the plate.
                fixed3 color = saturate(plate.rgb * _Color.rgb * _Exposure + _ShadowLift);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
