Shader "Instanced/Particle2DVectorField" {
	Properties {
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass {
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			StructuredBuffer<float2> Positions2D;
			StructuredBuffer<float2> DebugVectorData;
			StructuredBuffer<float> DebugVectorSign;
			StructuredBuffer<uint> IsGhost;

			int vectorUseSignedColor;
			int vectorUseLogScale;
			float vectorScale;
			float vectorMaxMagnitude;
			float vectorLogScaleStrength;
			float vectorWidth;

			struct v2f {
				float4 pos : SV_POSITION;
				float4 colour : COLOR0;
				nointerpolation float visible : TEXCOORD0;
			};

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float2 vectorValue = DebugVectorData[instanceID];
				float magnitude = length(vectorValue);
				float magnitudeT = saturate(magnitude / max(vectorMaxMagnitude, 0.0001));
				if (vectorUseLogScale != 0)
				{
					float logStrength = max(vectorLogScaleStrength, 1.0);
					magnitudeT = log(1.0 + magnitudeT * logStrength) / log(1.0 + logStrength);
				}
				float visible = 1.0;
				if (IsGhost[instanceID] != 0 || magnitude <= 0.000001)
				{
					visible = 0.0;
				}

				float2 direction = magnitude > 0.000001 ? vectorValue / magnitude : float2(1, 0);
				float2 normal = float2(-direction.y, direction.x);
				float lengthWorld = vectorScale * magnitudeT;
				float2 local = float2(v.vertex.x * lengthWorld, v.vertex.y * vectorWidth);
				float2 worldPos = Positions2D[instanceID] + direction * local.x + normal * local.y;

				v2f o;
				o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 0, 1));
				float alpha =  saturate((lengthWorld - 0.01) / (0.03 - 0.01)) * 0.35;
				if (vectorUseSignedColor != 0)
				{
					float signValue = DebugVectorSign[instanceID];
					float3 positiveColour = lerp(float3(0.0, 0.2, 1.0), float3(0.0, 1.0, 1.0), magnitudeT);
					float3 negativeColour = lerp(float3(1.0, 0.1, 0.0), float3(1.0, 0.6, 0.0), magnitudeT);
					o.colour = float4(signValue < 0.0 ? negativeColour : positiveColour, alpha);
				}
				else
				{
					o.colour = float4(1, 1, 1, alpha);
				}
				o.visible = visible;
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				if (i.visible < 0.5) discard;
				return i.colour;
			}

			ENDCG
		}
	}
}
