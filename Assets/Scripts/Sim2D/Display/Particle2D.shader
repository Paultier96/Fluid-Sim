Shader "Instanced/Particle2D" {
	Properties {

	}
	SubShader {

		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

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
			StructuredBuffer<uint> IsGhost;
			StructuredBuffer<uint> BlobIDs;
			StructuredBuffer<float> Temperatures;
			StructuredBuffer<float2> DebugData;
			float debugCurvatureMax;
			float debugViscosityMax;
			float debugDensityMin;
			float debugDensityMax;
			int debugMode;


			float scale;
			float4 colA;
			Texture2D<float4> ColourMap;
			Texture2D<float4> ColourMap2;
			Texture2D<float4> DebugHeatMap;
			Texture2D<float4> DebugSignedHeatMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;

			// Phase colours (set from C#)
			float4 phase0Color;
			float4 phase1Color;
			float tempMin;
			float tempMax;
			float3 particleLightDirection;
			float4 particleLightColor;
			float particleAmbientLight;
			float particleDirectionalLightIntensity;
			float4 particleSpecularColor;
			float particleSpecularIntensity;
			float particleSpecularPower;
			float4 particleFresnelColor;
			float particleFresnelIntensity;
			float particleFresnelPower;
			float2 particleGlowDirection;
			float4 particleGlowColor;
			float particleGlowIntensity;
			float particleGlowPower;
			float particleTransmissionIntensity;
			float particleTransmissionPower;
			float particleEdgeDarkening;
			float particleEdgeDarkeningPower;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
			};

			float3 HashBlobColor(uint blobId)
			{
				if (blobId == 0xFFFFFFFFu)
				{
					return float3(0, 0, 0);
				}

				uint hueSlot = blobId * 7u;
				uint tier = blobId / 12u;
				float hue = frac((hueSlot % 12u) / 12.0 + (tier + 1u) * 0.0527864045);
				float saturation = 0.86 + 0.10 * frac(tier * 0.318309886);
				float value = 0.82 + 0.18 * frac(tier * 0.754877666 + 0.31);
				float3 rgb = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
				return value * lerp(float3(1.0, 1.0, 1.0), rgb, saturation);
			}

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.uv = v.texcoord;
				o.pos = UnityObjectToClipPos(objectVertPos);

				if (debugMode == 0)
				{
					int pid = Phases[instanceID];
					float temp = Temperatures[instanceID];
					float tempT = saturate((temp - tempMin) / max(tempMax - tempMin, 0.001));

					if (pid == 0)
					{
						o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(tempT, 0.5), 0);
					}
					else
					{
						o.colour= ColourMap2.SampleLevel(linear_clamp_sampler, float2(tempT, 0.5), 0);
					}

					float2 normalizedDebugData = DebugData[instanceID] / 7;
					float z = sqrt(saturate(1.0 - dot(normalizedDebugData, normalizedDebugData)));
					float3 normal = float3(normalizedDebugData, z);
					float lightDirLength = max(length(particleLightDirection), 0.0001);
					float3 lightDir = particleLightDirection / lightDirLength;
					float directionalLight = saturate(dot(normal, lightDir)) * particleDirectionalLightIntensity;
					float3 lighting = particleAmbientLight + particleLightColor.rgb * directionalLight;
					float3 viewDir = float3(0.0, 0.0, 1.0);
					float3 halfVector = lightDir + viewDir;
					float3 halfDir = halfVector / max(length(halfVector), 0.0001);
					float specular = pow(saturate(dot(normal, halfDir)), max(particleSpecularPower, 1.0)) * particleSpecularIntensity;
					float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
					float2 glowDir = particleGlowDirection / max(length(particleGlowDirection), 0.0001);
					float directionalGlow = pow(saturate(dot(normal.xy, glowDir)), max(particleGlowPower, 0.1)) * saturate(1.0 - normal.z) * particleGlowIntensity;
					float transmission = pow(saturate(dot(-normal, lightDir)), max(particleTransmissionPower, 0.1)) * particleTransmissionIntensity;
					float edgeT = pow(saturate(1.0 - normal.z), max(particleEdgeDarkeningPower, 0.1)) * particleEdgeDarkening;
					o.colour *= 1.0 - edgeT;
					o.colour = saturate(
						o.colour * lighting
						+ particleSpecularColor.rgb * specular
						+ particleFresnelColor.rgb * fresnel
						+ particleGlowColor.rgb * directionalGlow
						+ o.colour * transmission
					);
				}
				else
				{
					float2 debugData = DebugData[instanceID];

					if (debugMode == 1) // gradient
					{
						// Reconstruct Z
						float2 normalizedDebugData = debugData / 7;
					    float z = sqrt(saturate(1.0 - dot(normalizedDebugData, normalizedDebugData)));
						float3 normal = float3(normalizedDebugData, 1);
					    float3 encodedNormal = saturate(0.5 + normal / 2.0);
						#if defined(UNITY_COLORSPACE_GAMMA)
							o.colour = encodedNormal;
						#else
							o.colour = GammaToLinearSpace(encodedNormal);
						#endif
					}

					else if (debugMode == 2) // curvature
					{
						float t = debugData.x / max(debugCurvatureMax, 0.0001);
						o.colour = DebugSignedHeatMap.SampleLevel(linear_clamp_sampler, float2(saturate(0.5 + t * 0.5), 0.5), 0).rgb;
					}

					else if (debugMode == 3) // viscosity
					{
						float t = saturate(debugData.x / max(debugViscosityMax, 0.0001));
						o.colour = DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(t, 0.5), 0).rgb;
					}

					if (debugMode == 4) //density
					{
						float density = DensityData[instanceID].x;
						float t = saturate((density - debugDensityMin) / max(debugDensityMax - debugDensityMin, 0.0001));
						o.colour = DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(t, 0.5), 0).rgb;
					}

					else if (debugMode == 5) //temperature
					{
						float t = saturate((Temperatures[instanceID] - tempMin) / max(tempMax - tempMin, 0.001));
						o.colour = DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(t, 0.5), 0).rgb;
					}

					else if (debugMode == 6) // blob ids
					{
						o.colour = HashBlobColor(BlobIDs[instanceID]);
					}
				}
				return o;
			}


			float4 frag (v2f i) : SV_Target
			{
				float2 centreOffset = (i.uv.xy - 0.5) * 2;
				float sqrDst = dot(centreOffset, centreOffset);
				float delta = fwidth(sqrt(sqrDst));
				float alpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);

				float3 colour = i.colour;
				return float4(colour, alpha);
			}

			ENDCG
		}
	}
}
