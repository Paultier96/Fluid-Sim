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
			StructuredBuffer<float> Curvatures;
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
			float metaballBlurRadius;
			float convexCurvatureMetaballBoost;
			float convexCurvatureBoostMax;
			float convexCurvatureBoostStartBlurRadius;
			float convexCurvatureBoostBlurRange;
			int debugMode;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float tempT : TEXCOORD1;
				float2 csfDebug : TEXCOORD2;
				nointerpolation float phase : TEXCOORD3;
				float density : TEXCOORD5;
				nointerpolation float3 blobCol : TEXCOORD6;
				float curvature : TEXCOORD7;
			};

			float GetCurvatureMetaballBoost(float curvature)
			{
				float convexT = saturate(max(curvature, 0.0) / max(convexCurvatureBoostMax, 0.0001));
				float blurT = saturate((metaballBlurRadius - convexCurvatureBoostStartBlurRadius) / max(convexCurvatureBoostBlurRange, 0.0001));
				return 1.0 + convexT * convexCurvatureMetaballBoost * blurT;
			}

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
				float tempT = (temp - tempMin) / max(tempMax - tempMin, 0.001);
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
				o.curvature = Curvatures[instanceID];
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				kernel *= GetCurvatureMetaballBoost(i.curvature);
				float maxAbsValue = max(debugGradientMax, 0.0001);

				// Debug mode 6: non-water blob IDs contribute colour; water/ignored ID contributes black weight.
				if (debugMode == 6)
				{
					return max(max(i.blobCol.r, i.blobCol.g), i.blobCol.b) > 0
						? float4(i.blobCol * kernel, 0)
						: float4(0, 0, 0, kernel);
				}
				
				// Debug mode 4: density visualization
				if (debugMode == 4)
				{
					float densityT = (i.density - debugDensityMin) / max(debugDensityMax - debugDensityMin, 0.0001);
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
			StructuredBuffer<float> Curvatures;
			StructuredBuffer<uint> IsGhost;

			float scale;
			float metaballSharpness;
			float metaballIntensity;
			float metaballBlurRadius;
			float convexCurvatureMetaballBoost;
			float convexCurvatureBoostMax;
			float convexCurvatureBoostStartBlurRadius;
			float convexCurvatureBoostBlurRange;
			float2 ellipseBoundsCenter;
			float2 ellipseBoundsSize;
			float obstacleY;
			float metaballGhostBoundaryNormalStrength;
			float metaballGhostBoundaryCornerBlendWidth;
			float metaballGhostBoundaryNormalWidth;
			int useEllipticalBounds;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float2 normalXY : TEXCOORD1;
				nointerpolation float phase : TEXCOORD2;
				float curvature : TEXCOORD3;
				float2 worldPos : TEXCOORD4;
				nointerpolation float isGhost : TEXCOORD5;
			};

			float GetCurvatureMetaballBoost(float curvature)
			{
				float convexT = saturate(max(curvature, 0.0) / max(convexCurvatureBoostMax, 0.0001));
				float blurT = saturate((metaballBlurRadius - convexCurvatureBoostStartBlurRadius) / max(convexCurvatureBoostBlurRange, 0.0001));
				return 1.0 + convexT * convexCurvatureMetaballBoost * blurT;
			}

			float2 BoundaryDistances(float2 worldPos)
			{
				float2 radii = max(abs(ellipseBoundsSize), 0.0001);
				float2 rel = worldPos - ellipseBoundsCenter;
				float2 q = rel / radii;
				float qLen = max(length(q), 0.0001);
				float ellipseGradientLength = length(float2(q.x / radii.x, q.y / radii.y)) / qLen;
				float ellipseDistance = (qLen - 1.0) / max(ellipseGradientLength, 0.0001);
				float insideEllipseX = step(abs(q.x), 1.0);
				float cutDistance = (obstacleY - worldPos.y) * insideEllipseX;
				return float2(ellipseDistance, cutDistance);
			}

			float AnalyticBoundaryDistance(float2 worldPos)
			{
				float2 distances = BoundaryDistances(worldPos);
				return max(distances.x, distances.y);
			}

			float AnalyticBoundaryFillet(float2 worldPos)
			{
				float outsideDistance = AnalyticBoundaryDistance(worldPos);
				return smoothstep(0.0, 1.0, saturate(outsideDistance / metaballGhostBoundaryNormalWidth));
			}

			float3 NormalFromXY(float2 normalXY)
			{
				float lenSq = dot(normalXY, normalXY);
				if (lenSq > 0.999)
				{
					normalXY *= rsqrt(lenSq) * 0.999;
					lenSq = dot(normalXY, normalXY);
				}
				return normalize(float3(normalXY, sqrt(saturate(1.0 - lenSq))));
			}

			float2 ReorientedNormalXY(float2 particleNormalXY, float2 analyticNormalXY)
			{
				float3 baseNormal = NormalFromXY(analyticNormalXY);
				float3 detailNormal = NormalFromXY(particleNormalXY);
				float3 combinedNormal = normalize(float3(
					baseNormal.xy + detailNormal.xy,
					baseNormal.z * detailNormal.z - dot(baseNormal.xy, detailNormal.xy)
				));
				return combinedNormal.xy;
			}

			float2 AnalyticBoundaryNormal(float2 worldPos)
			{
				float2 radii = max(abs(ellipseBoundsSize), 0.0001);
				float2 rel = worldPos - ellipseBoundsCenter;
				float2 q = rel / radii;
				float2 ellipseNormal = length(q) > 0.0001
					? normalize(float2(q.x / radii.x, q.y / radii.y))
					: float2(0.0, 1.0);
				float insideEllipseX = step(abs(q.x), 1.0);
				float2 distances = BoundaryDistances(worldPos);
				float boundaryDelta = distances.y - distances.x;
				float blendWidth = max(metaballGhostBoundaryCornerBlendWidth, 0.0001);
				float cutT = smoothstep(-blendWidth, blendWidth, boundaryDelta) * insideEllipseX;
				float2 cutNormal = float2(0.0, -1.0);
				return normalize(lerp(ellipseNormal, cutNormal, cutT));
			}

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float2 centre = Positions2D[instanceID];
				float3 centreWorld = float3(centre, 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.uv = v.texcoord;
				float2 debugData = DebugData[instanceID];
				o.normalXY = debugData / 7;
				o.phase = Phases[instanceID];
				o.curvature = Curvatures[instanceID];
				o.worldPos = worldVertPos.xy;
				o.isGhost = IsGhost[instanceID] != 0 ? 1.0 : 0.0;
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				kernel *= GetCurvatureMetaballBoost(i.curvature);
				float2 normalXY = i.normalXY;
				if (useEllipticalBounds != 0 && abs(metaballGhostBoundaryNormalStrength) > 0.0001)
				{
					float2 analyticNormal = AnalyticBoundaryNormal(i.worldPos) * sign(metaballGhostBoundaryNormalStrength);
					float fillet = AnalyticBoundaryFillet(i.worldPos);
					float filletT = fillet;
					float analyticT = filletT * saturate(abs(metaballGhostBoundaryNormalStrength));
					float analyticMagnitude = sin(filletT * 1.57079633);
					normalXY = lerp(normalXY, ReorientedNormalXY(normalXY, analyticNormal * analyticMagnitude), analyticT);
				}
				float2 packedNormal = saturate(normalXY * 0.5 + 0.5) * kernel;
				return i.phase < 0.5 ? float4(packedNormal, 0, 0) : float4(0, 0, packedNormal);
			}

			ENDCG
		}
	}
}
