Shader "Hidden/RadianceCascadesSdf"
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

			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

			sampler2D _MainTex;
			sampler2D _UpperCascadeTex;
			sampler2D _ResultTex;
			sampler2D _PayloadTex;
			sampler2D _CausticTex;
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
			float _SdfBoundaryThicknessPixels;
			int _SdfBoundarySource;
			float3 _DirectionalLightDirection;
			float3 _DirectionalLightColor;
			float _DirectionalLightIntensity;
			float2 metaballWorldCenter;
			float2 metaballWorldSize;

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

			float2 UvDirectionFromAngle(float angle)
			{
				float2 worldDirection = float2(cos(angle), sin(angle));
				float2 uvScale = max(metaballWorldSize, float2(0.0001, 0.0001));
				float2 uvDirection = worldDirection / uvScale;
				float uvLengthSq = dot(uvDirection, uvDirection);
				return uvLengthSq > 0.0000001 ? uvDirection / sqrt(uvLengthSq) : float2(0.0, 0.0);
			}

			float RayWorldStepScale(float2 rayDirection)
			{
				return max(length(rayDirection * max(metaballWorldSize, float2(0.0001, 0.0001))), 0.0001);
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

			float3 SampleDirectionalLightSource(float2 rayDirection)
			{
				if (_DirectionalLightEnabled == 0 || _DirectionalLightStrength <= 0.000001 || _DirectionalLightIntensity <= 0.000001)
				{
					return 0.0;
				}

				int minDirectionalCascadeLevel = (int)round(saturate(_DirectionalLightCascadeStart) * max(_CascadeCount - 1, 0));
				if (_CascadeLevel < minDirectionalCascadeLevel)
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
				float directionalRadiance = pow(alignmentCenter, 16.0) * 0.5 + pow(alignmentMinus, 16.0) * 0.25 + pow(alignmentPlus, 16.0) * 0.25;
				return _DirectionalLightColor * (_DirectionalLightIntensity * _DirectionalLightStrength * max(_RadianceIntensity, 0.0) * directionalRadiance);
			}

			float4 SampleSdfField(float2 uv, out float3 payload)
			{
				float4 sdf = tex2Dlod(_ResultTex, float4(uv, 0.0, 0.0));
				payload = tex2Dlod(_PayloadTex, float4(uv, 0.0, 0.0)).rgb;
				return sdf;
			}

			float2 EstimateSdfNormal(float2 uv)
			{
				float2 texel = 1.0 / max(_CascadeResolution, float2(1.0, 1.0));
				float2 worldTexel = max(metaballWorldSize / max(_CascadeResolution, float2(1.0, 1.0)), float2(0.0001, 0.0001));
				float dx = tex2Dlod(_ResultTex, float4(uv + float2(texel.x, 0.0), 0.0, 0.0)).r - tex2Dlod(_ResultTex, float4(uv - float2(texel.x, 0.0), 0.0, 0.0)).r;
				float dy = tex2Dlod(_ResultTex, float4(uv + float2(0.0, texel.y), 0.0, 0.0)).r - tex2Dlod(_ResultTex, float4(uv - float2(0.0, texel.y), 0.0, 0.0)).r;
				float2 normal = float2(dx / worldTexel.x, dy / worldTexel.y);
				float normalLengthSq = dot(normal, normal);
				return normalLengthSq > 0.0000001 ? normal / sqrt(normalLengthSq) : float2(0.0, 1.0);
			}

			float3 SampleSkyLitBoundary(float2 uv, float3 albedo)
			{
				if (_DirectionalLightEnabled == 0 || _DirectionalLightStrength <= 0.000001 || _DirectionalLightIntensity <= 0.000001)
				{
					return 0.0;
				}

				float2 normal = EstimateSdfNormal(uv);
				float2 lightDirection = _DirectionalLightDirection.xy;
				float lightDirectionLengthSq = dot(lightDirection, lightDirection);
				lightDirection = lightDirectionLengthSq > 0.0000001 ? lightDirection / sqrt(lightDirectionLengthSq) : float2(0.0, -1.0);
				float lambert = saturate(dot(normal, lightDirection));
				return albedo * _DirectionalLightColor * (_DirectionalLightIntensity * _DirectionalLightStrength * lambert);
			}

			float3 SampleBoundaryRadiance(float2 uv, float3 albedo)
			{
				float sourceScale = max(_BlobEmissionStrength, 0.0) * max(_RadianceIntensity, 0.0);
				if (_SdfBoundarySource == 1)
				{
					return SampleSkyLitBoundary(uv, albedo) * max(_BlobEmissionStrength, 0.0) * max(_RadianceIntensity, 0.0);
				}
				if (_SdfBoundarySource == 2)
				{
					return tex2Dlod(_CausticTex, float4(uv, 0.0, 0.0)).rgb * albedo * sourceScale;
				}

				return albedo * sourceScale;
			}

			bool HasBoundaryHit(float2 rayOrigin, float2 rayDirection, float startT, float endT, float hitThresholdWorld)
			{
				float worldStepScale = RayWorldStepScale(rayDirection);
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

					float3 payload;
					float4 sdfSample = SampleSdfField(currentPosition, payload);
					if (sdfSample.w < 0.0)
					{
						break;
					}

					float distanceToBoundary = abs(sdfSample.r);
					if (distanceToBoundary <= hitThresholdWorld)
					{
						return true;
					}

					float advanceWorld = max(distanceToBoundary, hitThresholdWorld);
					t += advanceWorld / worldStepScale;
				}

				return false;
			}

			float3 SampleVisibleDirectionalLight(float2 rayOrigin, float2 rayDirection, float startT, float hitThresholdWorld)
			{
				if (_CascadeLevel != _CascadeCount - 1)
				{
					return 0.0;
				}

				float endT = DistanceToUvBounds(rayOrigin, rayDirection);
				if (startT < endT && HasBoundaryHit(rayOrigin, rayDirection, startT, endT, hitThresholdWorld))
				{
					return 0.0;
				}

				return SampleDirectionalLightSource(rayDirection);
			}

			float4 SampleRadianceField(float2 rayOrigin, float2 rayDirection, float2 rayRange)
			{
				float worldStepScale = RayWorldStepScale(rayDirection);
				float hitThresholdWorld = max(max(metaballWorldSize.x / max(_CascadeResolution.x, 1.0), metaballWorldSize.y / max(_CascadeResolution.y, 1.0)) * max(_SdfBoundaryThicknessPixels, 0.0), 0.0005);
				float t = rayRange.x;
				float endT = rayRange.y;

				for (int i = 0; i < 128; i++)
				{
					if (i >= _RaySteps || t >= endT)
					{
						break;
					}

					float2 currentPosition = rayOrigin + t * rayDirection;
					if (currentPosition.x < 0.0 || currentPosition.y < 0.0 || currentPosition.x > 1.0 || currentPosition.y > 1.0)
					{
						break;
					}

					float3 payload;
					float4 sdfSample = SampleSdfField(currentPosition, payload);
					if (sdfSample.w < 0.0)
					{
						break;
					}

					float sdf = sdfSample.r;
					float distanceToBoundary = abs(sdf);

					if (distanceToBoundary <= hitThresholdWorld)
					{
						return float4(SampleBoundaryRadiance(currentPosition, payload), 0.0);
					}

					float advanceWorld = max(distanceToBoundary, hitThresholdWorld);
					t += advanceWorld / worldStepScale;
				}

				return float4(SampleVisibleDirectionalLight(rayOrigin, rayDirection, rayRange.y, hitThresholdWorld), 1.0);
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
						float4 upperRadiance = tex2D(_UpperCascadeTex, upperUv);
						radiance.rgb += upperRadiance.rgb * radiance.a;
						radiance.a *= upperRadiance.a;
					}
					finalResult += radiance * 0.25;
				}

				return finalResult;
			}
			ENDCG
		}
	}
}
