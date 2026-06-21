Shader "Hidden/Particle2DMetaballRadianceCascades"
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
			float3 _DirectionalLightDirection;
			float3 _DirectionalLightColor;
			float _DirectionalLightIntensity;
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
					colour = tex2D(ColourMap, float2(saturate(data), 0.5)).rgb;
				}
				else if (phase == 1)
				{
					float data = combined.b / max(combined.a, 0.0001);
					colour = tex2D(ColourMap2, float2(saturate(data), 0.5)).rgb;
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
				return Phase0MaskFromCombined(uv, tex2D(CombinedTex, SourceUvFromLocalUv(uv)));
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

			float3 SampleCausticSource(float2 uv, float phase0Mask)
			{
				return tex2D(_CausticTex, uv).rgb * phase0Mask * max(scatterStrengthA, 0.0) * max(lightIntensity, 0.0) * max(_BlobEmissionStrength, 0.0);
			}

			float3 SampleDirectionalLightSource(float2 rayDirection)
			{
				if (_DirectionalLightEnabled == 0 || _DirectionalLightStrength <= 0.000001 || _DirectionalLightIntensity <= 0.000001)
				{
					return 0.0;
				}

				float2 worldRayDirection = normalize(rayDirection * max(metaballWorldSize, float2(0.0001, 0.0001)));
				float2 lightDirection = _DirectionalLightDirection.xy;
				float lightDirectionLengthSq = dot(lightDirection, lightDirection);
				lightDirection = lightDirectionLengthSq > 0.0000001 ? lightDirection / sqrt(lightDirectionLengthSq) : float2(0.0, -1.0);
				float alignment = saturate(dot(worldRayDirection, lightDirection));
				float lobe = pow(alignment, 16.0);
				return _DirectionalLightColor * (_DirectionalLightIntensity * _DirectionalLightStrength * lobe);
			}

			float3 ApplyDirectionalLightContinuation(float2 rayOrigin, float2 rayDirection, float startDistance, float stepLength, float stepWorldDistance, float3 transmittance, float visibility)
			{
				if (visibility <= 0.0 || _DirectionalLightEnabled == 0)
				{
					return 0.0;
				}

				float continuationStepLength = max(stepLength * 2.0, 0.0005);
				float continuationWorldDistance = max(stepWorldDistance * 2.0, 0.0005);
				float t = startDistance;
				for (int i = 0; i < 64; i++)
				{
					t += continuationStepLength;
					float2 currentPosition = rayOrigin + t * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						return SampleDirectionalLightSource(rayDirection) * transmittance;
					}

					float4 combined = tex2D(CombinedTex, SourceUvFromLocalUv(currentPosition));
					float phase0Mask = Phase0MaskFromCombined(currentPosition, combined);
					float phase1Mask = Phase1MaskFromCombined(currentPosition, combined);
					if (phase0Mask <= 0.0001 && phase1Mask <= 0.0001)
					{
						return SampleDirectionalLightSource(rayDirection) * transmittance;
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
						transmittance *= exp(-spectralAbsorption * continuationWorldDistance);
					}
				}

				return 0.0;
			}

			float4 SampleRadianceField(float2 rayOrigin, float2 rayDirection, float2 rayRange)
			{
				int steps = max(_RaySteps, 1);
				float segmentLength = max(rayRange.y - rayRange.x, 0.0);
				float stepLength = segmentLength / steps;
				float stepWorldDistance = length(rayDirection * stepLength * metaballWorldSize);
				float3 radiance = 0.0;
				float3 transmittance = 1.0;
				float visibility = 1.0;
				float4 originCombined = tex2D(CombinedTex, SourceUvFromLocalUv(rayOrigin));
				float originPhase0Mask = Phase0MaskFromCombined(rayOrigin, originCombined);
				float originPhase1Mask = Phase1MaskFromCombined(rayOrigin, originCombined);
				if (originPhase0Mask <= 0.0001 && originPhase1Mask <= 0.0001)
				{
					return 0.0;
				}

				for (int i = 0; i < 64; i++)
				{
					if (i >= steps)
					{
						break;
					}

					float t = rayRange.x + (i + 0.5) * stepLength;
					float2 currentPosition = rayOrigin + t * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						radiance += SampleDirectionalLightSource(rayDirection) * transmittance;
						visibility = 0.0;
						break;
					}

					float4 combined = tex2D(CombinedTex, SourceUvFromLocalUv(currentPosition));
					float phase0Mask = Phase0MaskFromCombined(currentPosition, combined);
					float phase1Mask = Phase1MaskFromCombined(currentPosition, combined);
					if (phase0Mask <= 0.0001 && phase1Mask <= 0.0001)
					{
						radiance += SampleDirectionalLightSource(rayDirection) * transmittance;
						visibility = 0.0;
						break;
					}

					if (phase0Mask > 0.0001)
					{
						radiance += SampleCausticSource(currentPosition, phase0Mask) * transmittance * stepLength;
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
						transmittance *= exp(-spectralAbsorption * stepWorldDistance);
					}
				}

				radiance += ApplyDirectionalLightContinuation(rayOrigin, rayDirection, rayRange.y, stepLength, stepWorldDistance, transmittance, visibility);

				return float4(radiance * max(_RadianceIntensity, 0.0), visibility);
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
