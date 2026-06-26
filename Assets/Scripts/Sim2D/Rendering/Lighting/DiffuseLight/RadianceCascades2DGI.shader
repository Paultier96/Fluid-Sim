Shader "Hidden/RadianceCascades"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "black" {}
	}

	SubShader
	{
		Cull Off
		ZWrite Off
		ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0

			#include "UnityCG.cginc"

			#define TAU 6.28318530718

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			sampler2D _UpperCascadeTex;
			sampler2D _CausticTex;
			sampler2D CombinedTex;
			sampler2D _ResultTex;
			sampler2D ColourMap;
			sampler2D ColourMap2;
			float _RayRange;
			float2 _CascadeResolution;
			int _CascadeLevel;
			int _CascadeCount;
			int _RaySteps;
			float _RadianceIntensity;
			float _BlobEmissionStrength;
			int _DirectionalLightEnabled;
			float _DirectionalLightStrength;
			float _DirectionalLightCascadeStart;
			int _DirectionalLightSdfVisibility;
			float _SdfBoundaryThicknessPixels;
			float3 _DirectionalLightDirection;
			float3 _DirectionalLightColor;
			float _DirectionalLightIntensity;
			int _UseSdfSkipping;
			int radianceCascadeAbsorption;

			float densityThreshold;
			float edgeSoftness;
			float phase0RenderBias;
			float scatterStrengthA;
			float scatterStrengthB;
			float lightIntensity;
			float causticsPhase0Absorption;
			float causticsPhase1Absorption;
			float3 causticsPhase0AbsorptionTint;
			float3 causticsPhase1AbsorptionTint;
			float causticsPhase0AbsorptionTintBlend;
			float causticsPhase1AbsorptionTintBlend;
			float causticsAbsorptionAlbedoBrightnessInfluence;
			float causticsAbsorptionAlbedoSaturationInfluence;
			int useEllipticalBounds;
			float2 ellipseBoundsCenter;
			float2 ellipseBoundsSize;
			float obstacleY;
			float analyticBoundaryExpansion;
			float2 metaballWorldCenter;
			float2 metaballWorldSize;
			float4 metaballSourceUvRect;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float2 CalculateRayRange(int index, int count)
			{
				float maxValue = (1 << (count * 2)) - 1;
				float start = (1 << (index * 2)) - 1;
				float end = (1 << (index * 2 + 2)) - 1;
				return float2(start, end) / max(maxValue, 1.0) * _RayRange;
			}

			float2 WorldPosFromUv(float2 uv)
			{
				return metaballWorldCenter + (uv - 0.5) * max(metaballWorldSize, float2(0.0001, 0.0001));
			}

			float2 SourceUvFromLocalUv(float2 uv)
			{
				return metaballSourceUvRect.xy + uv * metaballSourceUvRect.zw;
			}

			float2 UvDirectionFromAngle(float angle)
			{
				float2 worldDirection = float2(cos(angle), sin(angle));
				float2 uvScale = max(metaballWorldSize, float2(0.0001, 0.0001));
				float2 uvDirection = worldDirection / uvScale;
				float uvLengthSq = dot(uvDirection, uvDirection);
				return uvLengthSq > 0.0000001 ? uvDirection / sqrt(uvLengthSq) : float2(0.0, 0.0);
			}

			float DistanceToUvBounds(float2 rayOrigin, float2 rayDirection)
			{
				float tx = 1e20;
				float ty = 1e20;
				if (rayDirection.x > 0.000001)
				{
					tx = (1.0 - rayOrigin.x) / rayDirection.x;
				}
				else if (rayDirection.x < -0.000001)
				{
					tx = -rayOrigin.x / rayDirection.x;
				}

				if (rayDirection.y > 0.000001)
				{
					ty = (1.0 - rayOrigin.y) / rayDirection.y;
				}
				else if (rayDirection.y < -0.000001)
				{
					ty = -rayOrigin.y / rayDirection.y;
				}

				return max(min(tx, ty), 0.0);
			}

			float2 BoundaryDistances(float2 worldPos)
			{
				float2 radii = max(abs(ellipseBoundsSize), 0.0001);
				float2 rel = worldPos - ellipseBoundsCenter;
				float2 q = rel / radii;
				float qLen = max(length(q), 0.0001);
				float ellipseGradientLength = length(float2(q.x / radii.x, q.y / radii.y)) / qLen;
				float ellipseDistance = (qLen - 1.0) / max(ellipseGradientLength, 0.0001);
				float cutDistance = obstacleY - worldPos.y;
				return float2(ellipseDistance, cutDistance);
			}

			float OuterAnalyticBoundaryDistance(float2 worldPos)
			{
				float2 distances = BoundaryDistances(worldPos);
				float outsideDistance = length(max(distances, 0.0)) + min(max(distances.x, distances.y), 0.0);
				return outsideDistance - max(analyticBoundaryExpansion, 0.0);
			}

			float FluidMask(float2 uv, float density)
			{
				float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0.0), densityThreshold + edgeSoftness, density);
				if (useEllipticalBounds == 0)
				{
					return particleAlpha;
				}

				float boundsDistance = OuterAnalyticBoundaryDistance(WorldPosFromUv(uv));
				float2 worldTexel = max(metaballWorldSize / max(_CascadeResolution, float2(1.0, 1.0)), float2(0.0001, 0.0001));
				float boundsAA = max(max(worldTexel.x, worldTexel.y), 0.0001);
				float boundsAlpha = smoothstep(boundsAA, -boundsAA, boundsDistance);
				return min(particleAlpha, boundsAlpha);
			}

			float3 RayColourFromCombined(float4 combined, int phase)
			{
				float3 colour = 1.0;
				if (phase == 0)
				{
					float data = combined.r / max(combined.g, 0.0001);
					colour = tex2Dlod(ColourMap, float4(saturate(data), 0.5, 0.0, 0.0)).rgb;
				}
				else if (phase == 1)
				{
					float data = combined.b / max(combined.a, 0.0001);
					colour = tex2Dlod(ColourMap2, float4(saturate(data), 0.5, 0.0, 0.0)).rgb;
				}

				float maxChannel = max(max(colour.r, colour.g), colour.b);
				if (maxChannel <= 0.0001)
				{
					return float3(1.0, 1.0, 1.0);
				}

				float targetMax = sqrt(saturate(maxChannel));
				float3 boostedColour = colour * (targetMax / maxChannel);
				return max(boostedColour, targetMax * 0.03);
			}

			float3 AbsorptionColourFromCombined(float4 combined, int phase)
			{
				float3 albedoColour = RayColourFromCombined(combined, phase);
				float maxChannel = max(max(albedoColour.r, albedoColour.g), albedoColour.b);
				float3 hueColour = maxChannel > 0.0001 ? albedoColour / maxChannel : float3(1.0, 1.0, 1.0);
				float brightness = lerp(1.0, maxChannel, saturate(causticsAbsorptionAlbedoBrightnessInfluence));
				float luminance = dot(hueColour, float3(0.2126, 0.7152, 0.0722));
				float3 controlledHue = lerp(float3(luminance, luminance, luminance), hueColour, saturate(causticsAbsorptionAlbedoSaturationInfluence));
				float3 brightnessControlledAlbedo = controlledHue * brightness;

				if (phase == 0)
				{
					return lerp(brightnessControlledAlbedo, max(causticsPhase0AbsorptionTint, 0.0), saturate(causticsPhase0AbsorptionTintBlend));
				}
				if (phase == 1)
				{
					return lerp(brightnessControlledAlbedo, max(causticsPhase1AbsorptionTint, 0.0), saturate(causticsPhase1AbsorptionTintBlend));
				}

				return albedoColour;
			}

			float3 SpectralAbsorptionFromCombined(float4 combined, int phase)
			{
				float absorption = phase == 0 ? causticsPhase0Absorption : causticsPhase1Absorption;
				if (absorption <= 0.0)
				{
					return 0.0;
				}

				float3 transmissionColour = max(AbsorptionColourFromCombined(combined, phase), float3(0.001, 0.001, 0.001));
				return -log(transmissionColour) * absorption + absorption * 0.1;
			}

			float Phase0MaskFromCombined(float2 uv, float4 combined)
			{
				float density0 = combined.g;
				float density1 = combined.a;
				float density = max(density0, density1);
				float mask = FluidMask(uv, density);
				if (mask <= 0.0001)
				{
					return 0.0;
				}

				float phaseRatio = density1 / max(density0 + density1, 0.0001);
				float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
				float phaseT = step(phaseBoundary, phaseRatio);
				return mask * (1.0 - phaseT);
			}

			float Phase0Mask(float2 uv)
			{
				return Phase0MaskFromCombined(uv, tex2Dlod(CombinedTex, float4(SourceUvFromLocalUv(uv), 0.0, 0.0)));
			}

			float Phase1MaskFromCombined(float2 uv, float4 combined)
			{
				float density0 = combined.g;
				float density1 = combined.a;
				float density = max(density0, density1);
				float mask = FluidMask(uv, density);
				if (mask <= 0.0001)
				{
					return 0.0;
				}

				float phaseRatio = density1 / max(density0 + density1, 0.0001);
				float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
				float phaseT = step(phaseBoundary, phaseRatio);
				return mask * phaseT;
			}

			float3 SampleCausticSource(float2 uv, float phase0Mask, float phase1Mask)
			{
				float scatterStrength = phase0Mask * max(scatterStrengthA, 0.0) + phase1Mask * max(scatterStrengthB, 0.0);
				return tex2Dlod(_CausticTex, float4(uv, 0.0, 0.0)).rgb * scatterStrength * max(lightIntensity, 0.0) * max(_BlobEmissionStrength, 0.0);
			}

			int DirectionalLightCascadeLevel()
			{
				return (int)round(saturate(_DirectionalLightCascadeStart) * max(_CascadeCount - 1, 0));
			}

			bool ShouldInjectDirectionalLight()
			{
				return _DirectionalLightEnabled != 0
					&& _DirectionalLightStrength > 0.000001
					&& _DirectionalLightIntensity > 0.000001
					&& _CascadeLevel == DirectionalLightCascadeLevel();
			}

			float3 SampleDirectionalLightSource(float2 rayDirection)
			{
				if (!ShouldInjectDirectionalLight())
				{
					return 0.0;
				}

				float2 worldRayDirection = normalize(rayDirection * max(metaballWorldSize, float2(0.0001, 0.0001)));
				float2 lightDirection = _DirectionalLightDirection.xy;
				float lightDirectionLengthSq = dot(lightDirection, lightDirection);
				lightDirection = lightDirectionLengthSq > 0.0000001 ? lightDirection / sqrt(lightDirectionLengthSq) : float2(0.0, -1.0);
				float angularStep = TAU / max(4.0 * pow(2.0, (float)_CascadeLevel * 2.0), 1.0);
				float halfStep = angularStep * 0.5;

				float sinStep;
				float cosStep;
				sincos(halfStep, sinStep, cosStep);
				float2 rayDirectionMinus = float2(
					worldRayDirection.x * cosStep + worldRayDirection.y * sinStep,
					-worldRayDirection.x * sinStep + worldRayDirection.y * cosStep
				);
				float2 rayDirectionPlus = float2(
					worldRayDirection.x * cosStep - worldRayDirection.y * sinStep,
					worldRayDirection.x * sinStep + worldRayDirection.y * cosStep
				);

				float alignmentCenter = saturate(dot(worldRayDirection, lightDirection));
				float alignmentMinus = saturate(dot(rayDirectionMinus, lightDirection));
				float alignmentPlus = saturate(dot(rayDirectionPlus, lightDirection));
				float lobeCenter = pow(alignmentCenter, 16.0);
				float lobeMinus = pow(alignmentMinus, 16.0);
				float lobePlus = pow(alignmentPlus, 16.0);
				float directionalRadiance = (lobeCenter * 0.5) + (lobeMinus * 0.25) + (lobePlus * 0.25);
				return _DirectionalLightColor * (_DirectionalLightIntensity * _DirectionalLightStrength * directionalRadiance);
			}

			float SdfHitThresholdWorld()
			{
				float2 worldTexel = max(metaballWorldSize / max(_CascadeResolution, float2(1.0, 1.0)), float2(0.0001, 0.0001));
				return max(max(worldTexel.x, worldTexel.y) * max(_SdfBoundaryThicknessPixels, 0.0), 0.0005);
			}

			bool HasSdfBoundaryHit(float2 rayOrigin, float2 rayDirection, float startT, float endT)
			{
				if (_UseSdfSkipping == 0)
				{
					return false;
				}

				float worldStepScale = max(length(rayDirection * max(metaballWorldSize, float2(0.0001, 0.0001))), 0.0001);
				float hitThresholdWorld = SdfHitThresholdWorld();
				float t = startT;
				[loop]
				for (int i = 0; i < 128; i++)
				{
					if (t >= endT)
					{
						break;
					}

					float2 currentPosition = rayOrigin + t * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						break;
					}

					float4 sdfSample = tex2Dlod(_ResultTex, float4(currentPosition, 0.0, 0.0));
					if (sdfSample.w < 0.0)
					{
						break;
					}

					float distanceToBoundary = abs(sdfSample.r);
					if (distanceToBoundary <= hitThresholdWorld)
					{
						return true;
					}

					t += max(distanceToBoundary, hitThresholdWorld) / worldStepScale;
				}

				return false;
			}

			float3 TraceVisibleDirectionalLight(float2 rayOrigin, float2 rayDirection)
			{
				if (!ShouldInjectDirectionalLight())
				{
					return 0.0;
				}

				float endT = DistanceToUvBounds(rayOrigin, rayDirection);
				if (_DirectionalLightSdfVisibility != 0 && _UseSdfSkipping != 0 && HasSdfBoundaryHit(rayOrigin, rayDirection, 0.0, endT))
				{
					return 0.0;
				}

				float3 transmittance = 1.0;
				int steps = min(max(_RaySteps * max(_CascadeCount, 1), 16), 128);
				float stepLength = endT / max((float)steps, 1.0);
				if (stepLength <= 0.000001)
				{
					return SampleDirectionalLightSource(rayDirection);
				}

				[loop]
				for (int i = 0; i < 128; i++)
				{
					if (i >= steps)
					{
						break;
					}

					float sampleDistance = (i + 0.5) * stepLength;
					float2 currentPosition = rayOrigin + sampleDistance * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						break;
					}

					float4 combined = tex2Dlod(CombinedTex, float4(SourceUvFromLocalUv(currentPosition), 0.0, 0.0));
					float phase0Mask = Phase0MaskFromCombined(currentPosition, combined);
					float phase1Mask = Phase1MaskFromCombined(currentPosition, combined);
					float outsideMask = phase0Mask <= 0.0001 && phase1Mask <= 0.0001 ? 1.0 : 0.0;

					if (radianceCascadeAbsorption != 0)
					{
						float stepWorldDistance = length(rayDirection * stepLength * metaballWorldSize);
						float3 spectralAbsorption = 0.0;
						if (phase0Mask > 0.0001)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 0) * phase0Mask;
						}
						if (phase1Mask > 0.0001)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 1) * phase1Mask;
						}
						if (outsideMask > 0.5)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 1);
						}
						transmittance *= exp(-spectralAbsorption * stepWorldDistance);
					}
				}

				return SampleDirectionalLightSource(rayDirection) * transmittance;
			}

			float SdfAcceleratedStepLength(float2 rayOrigin, float2 rayDirection, float t, float baseStepLength, float remainingDistance)
			{
				if (_UseSdfSkipping == 0)
				{
					return baseStepLength;
				}

				float2 probePosition = rayOrigin + (t + baseStepLength * 0.5) * rayDirection;
				if (probePosition.x < 0.0 || probePosition.y < 0.0 || probePosition.x > 1.0 || probePosition.y > 1.0)
				{
					return baseStepLength;
				}

				float4 sdfSample = tex2Dlod(_ResultTex, float4(probePosition, 0.0, 0.0));
				if (sdfSample.w < 0.0 || sdfSample.r <= 0.0)
				{
					return baseStepLength;
				}

				float worldStepScale = max(length(rayDirection * max(metaballWorldSize, float2(0.0001, 0.0001))), 0.0001);
				float2 worldTexel = max(metaballWorldSize / max(_CascadeResolution, float2(1.0, 1.0)), float2(0.0001, 0.0001));
				float boundarySafetyWorld = max(max(worldTexel.x, worldTexel.y) * 2.0, 0.0005);
				float skipDistance = max(sdfSample.r - boundarySafetyWorld, 0.0) / worldStepScale;
				return min(max(baseStepLength, skipDistance), remainingDistance);
			}

			float4 SampleRadianceField(float2 rayOrigin, float2 rayDirection, float2 rayRange)
			{
				int steps = max(_RaySteps, 1);
				float segmentLength = max(rayRange.y - rayRange.x, 0.0);
				float stepLength = segmentLength / steps;
				float3 radiance = TraceVisibleDirectionalLight(rayOrigin, rayDirection);
				float3 transmittance = 1.0;
				float visibility = 1.0;
				float t = rayRange.x;

				[loop]
				for (int i = 0; i < 64; i++)
				{
					if (i >= steps || t >= rayRange.y)
					{
						break;
					}

					float remainingDistance = rayRange.y - t;
					float advanceLength = SdfAcceleratedStepLength(rayOrigin, rayDirection, t, min(stepLength, remainingDistance), remainingDistance);
					float sampleDistance = t + advanceLength * 0.5;
					float2 currentPosition = rayOrigin + sampleDistance * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						visibility = 0.0;
						break;
					}

					float4 combined = tex2Dlod(CombinedTex, float4(SourceUvFromLocalUv(currentPosition), 0.0, 0.0));
					float phase0Mask = Phase0MaskFromCombined(currentPosition, combined);
					float phase1Mask = Phase1MaskFromCombined(currentPosition, combined);
					float outsideMask = phase0Mask <= 0.0001 && phase1Mask <= 0.0001 ? 1.0 : 0.0;
					float advanceWorldDistance = length(rayDirection * advanceLength * metaballWorldSize);

					if (phase0Mask > 0.0001 || phase1Mask > 0.0001)
					{
						radiance += SampleCausticSource(currentPosition, phase0Mask, phase1Mask) * transmittance * advanceLength;
					}

					if (radianceCascadeAbsorption != 0)
					{
						float3 spectralAbsorption = 0.0;
						if (phase0Mask > 0.0001)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 0) * phase0Mask;
						}
						if (phase1Mask > 0.0001)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 1) * phase1Mask;
						}
						if (outsideMask > 0.5)
						{
							spectralAbsorption += SpectralAbsorptionFromCombined(combined, 1);
						}
						transmittance *= exp(-spectralAbsorption * advanceWorldDistance);
					}

					t += advanceLength;
				}

				return float4(radiance * max(_RadianceIntensity, 0.0), visibility);
			}

			float RayBoxExitDistance(float2 origin, float2 dir)
			{
			    float2 invDir = 1.0 / max(abs(dir), 1e-6) * sign(dir);

			    float2 t0 = (float2(0.0, 0.0) - origin) * invDir;
			    float2 t1 = (float2(1.0, 1.0) - origin) * invDir;

			    float2 tMax = max(t0, t1);
			    return min(tMax.x, tMax.y);
			}

			float4 frag(v2f input) : SV_Target
			{
				float2 pixelIndex = floor(input.uv.xy * _CascadeResolution);
				int blockSqrtCount = 1 << _CascadeLevel;
				float2 blockDim = _CascadeResolution / blockSqrtCount;
				float2 block2DIndex = floor(pixelIndex / blockDim);
				float blockIndex = block2DIndex.x + block2DIndex.y * blockSqrtCount;
				float2 coordsInBlock = fmod(pixelIndex, blockDim);
				float2 rayOrigin = (coordsInBlock + 0.5) * blockSqrtCount / _CascadeResolution;
				float2 rayRange = CalculateRayRange(_CascadeLevel, _CascadeCount);
				float4 finalResult = 0.0;

				for (int i = 0; i < 4; i++)
				{
					float angleStep = TAU / (blockSqrtCount * blockSqrtCount * 4);
					float angleIndex = blockIndex * 4 + i;
					float angle = (angleIndex + 0.5) * angleStep;
					float2 rayDirection = UvDirectionFromAngle(angle);
					float4 radiance = SampleRadianceField(rayOrigin, rayDirection, rayRange);

					if (_CascadeLevel != _CascadeCount - 1)
					{
						float2 position = coordsInBlock * 0.5 + 0.25;
						float2 positionOffset = float2(fmod(angleIndex, blockSqrtCount * 2), floor(angleIndex / (blockSqrtCount * 2)));
						position = clamp(position, 0.5, blockDim * 0.5 - 0.5);
						float2 upperUv = (position + positionOffset * blockDim * 0.5) / _CascadeResolution;
						radiance.rgb += tex2D(_UpperCascadeTex, upperUv).rgb * radiance.a;
					}

					finalResult += radiance * 0.25;
				}

				return finalResult;
			}
			ENDCG
		}
	}
}
