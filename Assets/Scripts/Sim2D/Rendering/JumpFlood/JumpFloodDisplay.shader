Shader "Custom/JumpFloodDisplay"
{
    Properties
    {
        _PayloadTex ("Payload Texture", 2D) = "black" {}
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
            #include "../Lighting/Shared/ParticleFluidCommon.hlsl"

            Texture2D _PayloadTex;
            SamplerState sampler_PayloadTex;
            float4x4 _InverseViewProjection;
            float2 ellipseBoundsCenter;
            float2 ellipseBoundsSize;
            float2 boundsSize;
            float obstacleY;
            int useEllipticalBounds;

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

            float EllipseCutSignedDistance(float2 worldPos)
            {
                float2 radii = max(abs(ellipseBoundsSize) * 0.5, 0.0001);
                float2 rel = worldPos - ellipseBoundsCenter;
                float2 q = rel / radii;
                float qLen = max(length(q), 0.0001);
                float ellipseGradientLength = length(float2(q.x / radii.x, q.y / radii.y)) / qLen;
                float ellipseDistance = (1.0 - qLen) / max(ellipseGradientLength, 0.0001);
                float cutDistance = worldPos.y - obstacleY;
                return min(ellipseDistance, cutDistance);
            }

            float RectSignedDistance(float2 worldPos)
            {
                float2 halfSize = max(abs(boundsSize) * 0.5, 0.0001);
                float2 distanceToEdge = halfSize - abs(worldPos);
                return min(distanceToEdge.x, distanceToEdge.y);
            }

            float BoundsMask(float2 worldPos)
            {
                float signedDistance = useEllipticalBounds != 0
                    ? EllipseCutSignedDistance(worldPos)
                    : RectSignedDistance(worldPos);
                float aa = max(fwidth(signedDistance), 0.0001);
                return smoothstep(0.0, aa, signedDistance);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 seedUV = i.uv;
                float4 payload = _PayloadTex.Sample(sampler_PayloadTex, seedUV);
                if (payload.a < 0.0)
                    return float4(0,0,0,1);

                float mask = BoundsMask(ParticleFluidDomainWorldFromUv(seedUV));
                return float4(payload.rgb * mask, 1);
            }
            ENDCG
        }
    }
}
