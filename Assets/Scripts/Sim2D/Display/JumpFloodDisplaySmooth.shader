Shader "Custom/JumpFloodDisplaySmooth_FIXED"
{
    Properties
    {
        _SeedTex ("Seed Texture", 2D) = "white" {}
        _AAWidth ("AA Width (pixels)", Float) = 1.5
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

            float _AAWidth;

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
                    ? tex2D(ColourMap,  float2(t, 0.5)).rgb
                    : tex2D(ColourMap2, float2(t, 0.5)).rgb;
            }

            // Correct Voronoi edge distance (UV space, stable + symmetric)
            float VoronoiEdgeDist(float2 pos, float2 seedA, float2 seedB)
            {
                float dA = length(pos - seedA);
                float dB = length(pos - seedB);
                return dA - dB; // signed!
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

                // Convert AA width from pixels → UV
                float aaUV = _AAWidth * (texelSize.x + texelSize.y) * 0.5;

                float4 seed = _SeedTex.Sample(sampler_SeedTex, seedUV);

                if (seed.w < 0.0)
                    return float4(0, 0, 0, 1);

                int    phase  = (int)round(seed.w);
                float  t      = seed.z;
                float2 mySeed = seed.xy;

                float3 color = PhaseColor(phase, t);

                float sameEps = 0.5 * max(texelSize.x, texelSize.y);

                // 8-neighbour sampling
                float2 offsets[8] = {
                    float2( 0,  1),
                    float2( 0, -1),
                    float2( 1,  0),
                    float2(-1,  0),
                    float2( 1,  1),
                    float2(-1,  1),
                    float2( 1, -1),
                    float2(-1, -1)
                };

                float  closestEdgeDist = 1e9;
                float3 closestOtherColor = color;

                [unroll]
                for (int n = 0; n < 8; n++)
                {
                    float2 uvOffset = seedUV + offsets[n] * texelSize;
                    float4 nb = _SeedTex.Sample(sampler_SeedTex, uvOffset);

                    if (nb.w < 0.0)
                        continue;

                    float2 nbSeed = nb.xy;

                    // same cell → skip
                    if (length(nbSeed - mySeed) < sameEps)
                        continue;

                    float d = VoronoiEdgeDist(seedUV, mySeed, nbSeed);
                    if (d < closestEdgeDist)
                    {
                        closestEdgeDist = d;

                        int   otherPhase = (int)round(nb.w);
                        float otherT     = nb.z;

                        closestOtherColor = PhaseColor(otherPhase, otherT);
                    }
                }

                if (closestEdgeDist < 1e8)
                {
                    float w = max(aaUV, fwidth(closestEdgeDist));

                    // symmetric transition across edge
                    float blend = smoothstep(-w, w, closestEdgeDist);

                    // IMPORTANT: order matters now
                    color = lerp(closestOtherColor, color, blend);
                }

                return float4(color, 1);
            }

            ENDCG
        }
    }
}