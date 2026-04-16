Shader "Custom/JumpFloodDisplay"
{
    Properties
    {
        _SeedTex ("Seed Texture", 2D) = "white" {}
        _EdgeWidth ("Edge Width", Float) = 0.003
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

            sampler2D _SeedTex;
            sampler2D ColourMap;
            sampler2D ColourMap2;
            float _EdgeWidth;
            float tempMin;
            float tempMax;

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

            float4 frag(v2f i) : SV_Target
            {
                float4 seed = tex2D(_SeedTex, i.uv);
                if (seed.w < 0.0)
                    return float4(0,0,0,1);

                float t = seed.z; // normalized temperature
                int phase = (int)round(seed.w);

                float3 color = (phase == 0) ? tex2D(ColourMap, float2(t,0.5)).rgb : tex2D(ColourMap2, float2(t,0.5)).rgb;
                return float4(color, 1);
            }
            ENDCG
        }
    }
}