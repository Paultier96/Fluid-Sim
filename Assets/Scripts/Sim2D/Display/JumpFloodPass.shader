Shader "Custom/JumpFloodPass"
{
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
            #pragma target 4.5
            #include "UnityCG.cginc"

            Texture2D<float4> _SrcTex;
            SamplerState sampler_SrcTex;
            int _Step;
            int _Width;
            int _Height;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                // fullscreen triangle vertices (NDC)
                float2 verts[3];
                verts[0] = float2(-1.0, -1.0);
                verts[1] = float2(3.0, -1.0);
                verts[2] = float2(-1.0, 3.0);
                float2 pos2 = verts[id];
                o.pos = float4(pos2, 0.0, 1.0);
                o.uv = pos2 * 0.5 + 0.5;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
#ifdef UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
#endif
                int2 pix = int2(uv * float2(_Width, _Height));
                float2 sampleUV = (float2(pix) + 0.5) / float2(_Width, _Height);

                float4 best = _SrcTex.Load(int3(pix, 0));
                float bestDist = 1e20;
                if (best.w >= 0.0)
                {
                    bestDist = distance(sampleUV, best.xy);
                }

                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int2 n = pix + int2(ox * _Step, oy * _Step);
                        if (n.x < 0 || n.y < 0 || n.x >= _Width || n.y >= _Height) continue;
                        float4 cand = _SrcTex.Load(int3(n, 0));
                        if (cand.w < 0.0) continue; // no seed here
                        float d = distance(uv, cand.xy);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = cand;
                        }
                    }
                }

                return best;
            }
            ENDCG
        }
    }
}
