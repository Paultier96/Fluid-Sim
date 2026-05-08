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
			float debugGradientMax;
			float debugCurvatureMax;
			float debugViscosityMax;
			float debugDensityMin;
			float debugDensityMax;
			int debugMode;


			float scale;
			float4 colA;
			Texture2D<float4> ColourMap;
			Texture2D<float4> ColourMap2;
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
				uint n = blobId * 1664525u + 1013904223u;
				n ^= (n >> 16);
				uint r = n * 2246822519u;
				uint g = (n ^ 3266489917u) * 668265263u;
				uint b = (n ^ 374761393u) * 2246822519u;
				return 0.25 + 0.75 * frac(float3(r, g, b) / 65535.0);
			}

			float3 HeatMapColor(float t)
			{
				t = saturate(t);
				float3 c0 = float3(0.0, 0.0, 0.0);
				float3 c1 = float3(0.125, 0.0, 0.549);
				float3 c2 = float3(0.8, 0.0, 0.466);
				float3 c3 = float3(1.0, 0.843, 0.0);
				float3 c4 = float3(1.0, 1.0, 1.0);

				if (t < 0.25) return lerp(c0, c1, t * 4.0);
				if (t < 0.5) return lerp(c1, c2, (t - 0.25) * 4.0);
				if (t < 0.75) return lerp(c2, c3, (t - 0.5) * 4.0);
				return lerp(c3, c4, (t - 0.75) * 4.0);
			}

			float3 SignedHeatMapColor(float t)
			{
				// old red green gradient
			    // float3 negCol  = float3(1.0, 0.0, 0.0);
			    // float3 zeroCol = float3(0.0, 0.0, 1.0);
			    // float3 posCol  = float3(0.0, 1.0, 0.0);
			    // return t < 0 ? lerp(zeroCol, negCol, -t) : lerp(zeroCol,
			    float a = saturate(abs(t));
			    float3 pos = lerp(float3(0.0, 0.0, 0.0),  // black
			                      float3(0.0, 0.2, 1.0),   // deep blue
			                      saturate(a * 2.0))
			               + lerp(float3(0.0, 0.0, 0.0),
			                      float3(0.0, 1.0, 1.0),   // cyan bloom
			                      saturate(a * 2.0 - 1.0));

			    float3 neg = lerp(float3(0.0, 0.0, 0.0),  // black
			                      float3(1.0, 0.1, 0.0),   // deep red
			                      saturate(a * 2.0))
			               + lerp(float3(0.0, 0.0, 0.0),
			                      float3(1.0, 0.6, 0.0),   // orange bloom
			                      saturate(a * 2.0 - 1.0));

			    return t < 0.0 ? neg : pos;
			}

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				//float speed = length(Velocities[instanceID]);
				//float speedT = saturate(speed / velocityMax);

				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.uv = v.texcoord;
				o.pos = UnityObjectToClipPos(objectVertPos);


				// else if (IsGhost[instanceID] != 0)
				// {
				// 	o.colour = float3(0.1, 0.1, 0.1);
				// }
				if (debugMode != 0)
				{
					float maxAbsValue = max(debugGradientMax, 0.0001);
					float2 debugData = DebugData[instanceID];

					// if (debugMode == 1) // gradient
					// {
					// 	float2 mapped = saturate(0.5 + debugData / 2.0);
					// 	o.colour = float3(mapped.x, mapped.y, 1);
					// }

					if (debugMode == 1) // gradient
					{
						// Reconstruct Z
					    float z = sqrt(saturate(1.0 - dot(debugData, debugData)));
						float3 normal = float3(debugData, z);
					    o.colour = saturate(0.5 + normal / 2.0);;
					}

					else if (debugMode == 2) // curvature
					{
						float t = debugData.x / max(debugCurvatureMax, 0.0001);
						o.colour = SignedHeatMapColor(t);
					}

					else if (debugMode == 3) // Force
					{
						float2 mapped = saturate(0.5 + debugData / (2.0 * maxAbsValue));
						float mag = saturate(length(debugData) / maxAbsValue);
						o.colour = float3(mapped, mag);
					}

					else if (debugMode == 4) // viscosity
					{
						float t = saturate(debugData.x / max(debugViscosityMax, 0.0001));
						o.colour = HeatMapColor(t);
					}

					if (debugMode == 5) //density
					{
						float density = DensityData[instanceID].x;
						float t = saturate((density - debugDensityMin) / max(debugDensityMax - debugDensityMin, 0.0001));
						o.colour = HeatMapColor(t);
					}

					else if (debugMode == 6) //temperature
					{
						float t = saturate((Temperatures[instanceID] - tempMin) / max(tempMax - tempMin, 0.001));
						o.colour = HeatMapColor(t);
					}

					else if (debugMode == 7) // blob ids
					{
						o.colour = HashBlobColor(BlobIDs[instanceID]);
					}
				}
				else
				{
					int pid = Phases[instanceID];
					float3 phaseCol = pid == 0 ? phase0Color.rgb : phase1Color.rgb;
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
