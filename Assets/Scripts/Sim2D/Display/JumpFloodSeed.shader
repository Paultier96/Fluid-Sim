Shader "Custom/JumpFloodSeed"
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

            StructuredBuffer<float2> Positions2D;
            StructuredBuffer<float> Temperatures;
            StructuredBuffer<int> Phases;
            int _ParticleCount;
            float2 _SimMin;
            float2 _SimMax;
            matrix _VP;
            float _TempMin;
            float _TempMax;

            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float2 uv : TEXCOORD0;
                nointerpolation float tnorm : TEXCOORD1;
                nointerpolation float phase : TEXCOORD2;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                if (id >= _ParticleCount)
                {
                    // off-screen
                    o.pos = float4(0,0,0,1);
                    o.uv = float2(-1,-1);
                    o.tnorm = 0.0;
                    o.phase = -1.0;
                    return o;
                }

                float2 p = Positions2D[id];
                float4 clip = mul(_VP, float4(p, 0, 1));
                float2 ndc = clip.xy / clip.w;        // -1 to 1
                float2 uv = ndc * 0.5 + 0.5;          // 0 to 1

                // clamp
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                {
                    o.pos = float4(0,0,0,1);
                    o.uv = float2(-1,-1);
                    o.tnorm = 0.0;
                    o.phase = -1.0;
                    return o;
                }

                // account for platform UV origin if necessary
#ifdef UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
#endif
                // map to clip space
                float x = uv.x * 2.0 - 1.0;
                float y = uv.y * 2.0 - 1.0;
                o.pos = float4(x, y, 0.0, 1.0);
                o.uv = uv;

                float tnorm = 0.0;
                if (_TempMax > _TempMin) tnorm = saturate((Temperatures[id] - _TempMin) / (_TempMax - _TempMin));
                o.tnorm = tnorm;
                o.phase = (float)Phases[id];

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // if off-screen or invalid
                if (i.uv.x < 0.0) return float4(-1.0, -1.0, 0.0, -1.0);

                return float4(i.uv.x, i.uv.y, i.tnorm, i.phase);
            }
            ENDCG
        }
    }
}
