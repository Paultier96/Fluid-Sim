Shader "Instanced/Particle2DMetaball" {
	Properties {
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Blend One One
		ZWrite Off
		Cull Off

		Pass {
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			StructuredBuffer<float2> Positions2D;
			StructuredBuffer<float2> Velocities;
			StructuredBuffer<float2> DensityData;
			StructuredBuffer<int> Phases;
			StructuredBuffer<float> Temperatures;
			StructuredBuffer<float2> CSFGradients;

			Texture2D<float4> ColourMap;
			Texture2D<float4> ColourMap2;
			SamplerState linear_clamp_sampler;

			float scale;
			float tempMin;
			float tempMax;
			float debugGradientMax;
			float metaballSharpness;
			float metaballIntensity;
			int renderPhase;
			int debugMode;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float phase : TEXCOORD2;
			};

			float3 SampleParticleColour(uint instanceID)
			{
				if (debugMode != 0)
				{
					float2 grad = CSFGradients[instanceID];
					float2 gradNorm = saturate(abs(grad) / max(debugGradientMax, 0.0001));
					return float3(gradNorm, 0);
				}

				int phase = Phases[instanceID];
				float temp = Temperatures[instanceID];
				float tempT = saturate((temp - tempMin) / max(tempMax - tempMin, 0.001));
				if (phase == 0)
				{
					return ColourMap.SampleLevel(linear_clamp_sampler, float2(tempT, 0.5), 0).rgb;
				}
				return ColourMap2.SampleLevel(linear_clamp_sampler, float2(tempT, 0.5), 0).rgb;
			}

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.uv = v.texcoord;
				o.colour = SampleParticleColour(instanceID);
				o.phase = Phases[instanceID];
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;
				if (debugMode == 0 && abs(i.phase - renderPhase) > 0.1) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				return float4(i.colour * kernel, kernel);
			}

			ENDCG
		}
	}
}
