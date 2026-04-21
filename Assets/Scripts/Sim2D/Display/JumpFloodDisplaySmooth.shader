Shader "Custom/JumpFloodDisplaySmooth_FIXED"
{
    Properties
    {
        _SeedTex ("Seed Texture", 2D) = "white" {}
        _BlurStrength ("Blur Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            Cull Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            Texture2D _SeedTex;
            SamplerState sampler_SeedTex;

            sampler2D ColourMap;
            sampler2D ColourMap2;

            float _BlurStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 PhaseColor(int phase, float t)
            {
                return (phase == 0)
                    ? tex2D(ColourMap, float2(t, 0.5)).rgb
                    : tex2D(ColourMap2, float2(t, 0.5)).rgb;
            }

            float3 SampleResolvedColor(float2 uv)
            {
                float4 seed = _SeedTex.Sample(sampler_SeedTex, uv);
                if (seed.w < 0.0)
                    return float3(0, 0, 0);

                int phase = (int)round(seed.w);
                float t = seed.z;
                return PhaseColor(phase, t);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 seedUV = i.uv;

                #ifdef UNITY_UV_STARTS_AT_TOP
                    seedUV.y = 1.0 - seedUV.y;
                #endif

                uint w, h;
                _SeedTex.GetDimensions(w, h);
                float2 texelSize = float2(1.0 / w, 1.0 / h);
                float3 baseColor = SampleResolvedColor(seedUV);

                if (_BlurStrength <= 0.0)
                    return float4(baseColor, 1);

                float3 blurColor = float3(0, 0, 0);

                [unroll]
                for (int oy = -1; oy <= 1; oy++)
                {
                    [unroll]
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        float2 uvOffset = clamp(seedUV + float2(ox, oy) * texelSize, 0.0, 1.0);
                        blurColor += SampleResolvedColor(uvOffset);
                    }
                }

                blurColor *= (1.0 / 9.0);
                float3 color = lerp(baseColor, blurColor, saturate(_BlurStrength));
                return float4(color, 1);
            }

            ENDCG
        }
    }
}
