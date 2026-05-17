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
			StructuredBuffer<int> Phases;
			StructuredBuffer<uint> BlobIDs;
			StructuredBuffer<float> Temperatures;
			StructuredBuffer<float2> DebugData;
			StructuredBuffer<float2> DensityData;

			float scale;
			float tempMin;
			float tempMax;
			float debugGradientMax;
			float debugCurvatureMax;
			float debugViscosityMax;
			float debugDensityMin;
			float debugDensityMax;
			float metaballSharpness;
			float metaballIntensity;
			int debugMode;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float tempT : TEXCOORD1;
				float2 csfDebug : TEXCOORD2;
				nointerpolation float phase : TEXCOORD3;
				float density : TEXCOORD5;
				nointerpolation float3 blobCol : TEXCOORD6;
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

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				float temp = Temperatures[instanceID];
				float tempT = saturate((temp - tempMin) / max(tempMax - tempMin, 0.001));
				float2 csfData = DebugData[instanceID];
				float density = DensityData[instanceID].x;

				v2f o;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.uv = v.texcoord;
				o.tempT = tempT;
				o.csfDebug = csfData;
				o.phase = Phases[instanceID];
				o.density = density;
				o.blobCol = HashBlobColor(BlobIDs[instanceID]);
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				float maxAbsValue = max(debugGradientMax, 0.0001);

				// Debug mode 6: blob id visualization uses RGB weighted colour + A weight.
				if (debugMode == 6)
				{
					return float4(i.blobCol * kernel, kernel);
				}
				
				// Debug mode 4: density visualization
				if (debugMode == 4)
				{
					float densityT = saturate((i.density - debugDensityMin) / max(debugDensityMax - debugDensityMin, 0.0001));
					float2 packed = float2(densityT * kernel, kernel);
					return i.phase < 0.5 ? float4(packed, 0, 0) : float4(0, 0, packed);
				}
				
				// Debug mode 5: temperature visualization
				if (debugMode == 5)
				{
					float2 packed = float2(i.tempT * kernel, kernel);
					return i.phase < 0.5 ? float4(packed, 0, 0) : float4(0, 0, packed);
				}
				
				if (debugMode != 0)
				{
					if (debugMode == 1)
					{
						float2 packed = float2(0, kernel);
						return i.phase < 0.5 ? float4(packed, 0, 0) : float4(0, 0, packed);
					}

					float2 debugData;
					if (debugMode == 2)
					{
						debugData = i.csfDebug.xx / max(debugCurvatureMax, 0.0001);
					}
					else if (debugMode == 3)
					{
						debugData = i.csfDebug.xx / max(debugViscosityMax, 0.0001);
					}
					else
					{
						debugData = i.csfDebug / maxAbsValue;
					}
					return float4(debugData.x * kernel, kernel, debugData.y * kernel, kernel);
				}

				float2 packed = float2(i.tempT * kernel, kernel);

				return i.phase < 0.5 ? float4(packed, 0, 0) : float4(0, 0, packed);
			}

			ENDCG
		}

		Pass {
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			StructuredBuffer<float2> Positions2D;
			StructuredBuffer<int> Phases;
			StructuredBuffer<float2> DebugData;

			float scale;
			float metaballSharpness;
			float metaballIntensity;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float2 normalXY : TEXCOORD1;
				nointerpolation float phase : TEXCOORD2;
			};

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.uv = v.texcoord;
				o.normalXY = DebugData[instanceID] / 7;
				o.phase = Phases[instanceID];
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				float2 packedNormal = saturate(i.normalXY * 0.5 + 0.5) * kernel;
				return i.phase < 0.5 ? float4(packedNormal, 0, 0) : float4(0, 0, packedNormal);
			}

			ENDCG
		}
	}
}
