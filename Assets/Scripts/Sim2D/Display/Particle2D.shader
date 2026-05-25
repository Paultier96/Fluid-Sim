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
			int debugShowClipping;
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

			float3 ApplyHeatMapClipMarker(float t, float3 colour)
			{
				if (debugShowClipping == 0)
				{
					return colour;
				}

				float3 clipColour = t < 0.0 ? float3(0.0, 1.0, 1.0) : float3(1.0, 0.0, 1.0);
				float clipped = (t < 0.0 || t > 1.0) ? 1.0 : 0.0;
				#if defined(UNITY_COLORSPACE_GAMMA)
					return lerp(colour, clipColour, clipped);
				#else
					return lerp(colour, GammaToLinearSpace(clipColour), clipped);
				#endif
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

					o.colour = saturate(o.colour);
				}
				else
				{
					float2 debugData = DebugData[instanceID];

					if (debugMode == 1) // gradient
					{
						// Reconstruct Z
						float2 normalizedDebugData = debugData / 7;
						float normalLenSq = dot(normalizedDebugData, normalizedDebugData);
						float3 normal = float3(normalizedDebugData, 1);
						float3 encodedNormal = saturate(0.5 + normal / 2.0);
						float clipped = debugShowClipping != 0 ? step(1.0, normalLenSq) : 0.0;
						float3 debugColour = lerp(encodedNormal, float3(0.0, 1.0, 0.0), clipped);
						#if defined(UNITY_COLORSPACE_GAMMA)
							o.colour = debugColour;
						#else
							o.colour = GammaToLinearSpace(debugColour);
						#endif
					}

					else if (debugMode == 2) // curvature
					{
						float t = debugData.x / max(debugCurvatureMax, 0.0001);
						float heatT = 0.5 + t * 0.5;
						o.colour = ApplyHeatMapClipMarker(heatT, DebugSignedHeatMap.SampleLevel(linear_clamp_sampler, float2(saturate(heatT), 0.5), 0).rgb);
					}

					else if (debugMode == 3) // viscosity
					{
						float t = debugData.x / max(debugViscosityMax, 0.0001);
						o.colour = ApplyHeatMapClipMarker(t, DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(saturate(t), 0.5), 0).rgb);
					}

					if (debugMode == 4) //density
					{
						float density = DensityData[instanceID].x;
						float t = (density - debugDensityMin) / max(debugDensityMax - debugDensityMin, 0.0001);
						o.colour = ApplyHeatMapClipMarker(t, DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(saturate(t), 0.5), 0).rgb);
					}

					else if (debugMode == 5) //temperature
					{
						float t = (Temperatures[instanceID] - tempMin) / max(tempMax - tempMin, 0.001);
						o.colour = ApplyHeatMapClipMarker(t, DebugHeatMap.SampleLevel(linear_clamp_sampler, float2(saturate(t), 0.5), 0).rgb);
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
