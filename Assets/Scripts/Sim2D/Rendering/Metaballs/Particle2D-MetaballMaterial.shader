Shader "Hidden/Particle2DMetaballMaterial" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		#include "../Lighting/Shared/ParticleFluidCommon.hlsl"

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D CombinedTex;
float4 CombinedTex_TexelSize;
sampler2D NormalTex;
sampler2D MaterialAlbedoTex;
sampler2D MaterialTransportTex;
sampler2D ColourMap;
sampler2D ColourMap2;
sampler2D DebugHeatMap;
sampler2D DebugSignedHeatMap;
sampler2D _MainTex;
float4 _MainTex_TexelSize;
float causticMotionDilationRadius;
float densityThreshold;
float edgeSoftness;
float phaseBlendWidth;
float transportPhaseBlendWidth;
float phase0RenderBias;
float phaseBiasNormalStrength;
#include "../Lighting/Shared/ParticleFluidAnalyticBoundary.hlsl"
float metaballGhostBoundaryNormalStrength;
float metaballRefractionStrength;
float metaballRefractionEdgeFade;
int screenSpaceRefractionCanCrossPhases;
int debugMode;
int debugShowClipping;
float debugGradientMax;
float motionDebugDeltaTime;
float particleNormalStrength;
float particleNormalProfileCurve;
v2f vert(appdata v)
{
	v2f o;
	o.vertex = TransformObjectToHClip(v.vertex.xyz);
	o.uv = v.uv;
	return o;
}

float InterleavedGradientNoise(float2 pixel)
{
	float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	return frac(magic.z * frac(dot(pixel, magic.xy)));
}

float3 HeatMapClipColour(float t)
{
	float3 clipColour = t < 0.0 ? float3(0.0, 1.0, 1.0) : float3(1.0, 0.0, 1.0);
	#if defined(UNITY_COLORSPACE_GAMMA)
		return clipColour;
	#else
		return SRGBToLinear(clipColour);
	#endif
}

float3 SampleDebugHeatMap(sampler2D gradientTex, float rawT, float sampleT)
{
	float3 colour = tex2D(gradientTex, float2(saturate(sampleT), 0.5)).rgb;
	if (debugShowClipping != 0 && (rawT < 0.0 || rawT > 1.0))
	{
		return HeatMapClipColour(rawT);
	}
	return colour;
}

float2 ApplyNormalStrength(float2 normalXY, float normalStrengthMultiplier)
{
	return normalXY * particleNormalStrength * max(normalStrengthMultiplier, 0.0);
}

float2 ApplyNormalProfileCurve(float2 normalXY)
{
	float curve = max(particleNormalProfileCurve, 0.0001);
	float magnitude = length(normalXY);
	if (magnitude <= 0.000001)
	{
		return 0.0;
	}

	float curvedMagnitude = pow(saturate(magnitude), curve);
	return normalXY * (curvedMagnitude / magnitude);
}

float3 NormalFromXY(float2 normalXY, float normalStrengthMultiplier)
{
	normalXY = ApplyNormalStrength(normalXY, normalStrengthMultiplier);
	normalXY = ApplyNormalProfileCurve(normalXY);
	float lenSq = dot(normalXY, normalXY);
	if (lenSq > 0.999)
	{
		normalXY *= rsqrt(lenSq) * 0.999;
		lenSq = dot(normalXY, normalXY);
	}
	float normalZ = sqrt(saturate(1.0 - lenSq));
	return normalize(float3(normalXY, normalZ));
}

float3 NormalFromStrengthenedXY(float2 normalXY)
{
	float lenSq = dot(normalXY, normalXY);
	if (lenSq > 0.999)
	{
		normalXY *= rsqrt(lenSq) * 0.999;
		lenSq = dot(normalXY, normalXY);
	}
	return normalize(float3(normalXY, sqrt(saturate(1.0 - lenSq))));
}

float3 ReorientedNormal(float3 baseNormal, float3 detailNormal)
{
	return normalize(float3(
		baseNormal.xy + detailNormal.xy,
		baseNormal.z * detailNormal.z - dot(baseNormal.xy, detailNormal.xy)
	));
}

bool TryGetAnalyticBoundaryNormalParams(float2 worldPos, out float filletT, out float strength)
{
	strength = abs(metaballGhostBoundaryNormalStrength);
	if (useEllipticalBounds == 0 || strength <= 0.0001)
	{
		filletT = 0.0;
		return false;
	}

	float width = max(analyticBoundaryExpansion, 0.0001);
	float shellDistance = analyticBoundaryExpansion > 0.0001
		? OuterAnalyticBoundaryDistance(worldPos) + analyticBoundaryExpansion
		: AnalyticBoundaryDistance(worldPos);
	filletT = smoothstep(0.0, 1.0, saturate(shellDistance / width));
	return true;
}

float3 ApplyAnalyticBoundaryNormal(float3 particleNormal, float2 worldPos)
{
	float filletT;
	float strength;
	if (!TryGetAnalyticBoundaryNormalParams(worldPos, filletT, strength))
	{
		return particleNormal;
	}

	float analyticT = filletT * saturate(strength);
	float analyticMagnitude = saturate(sin(filletT * 1.57079633) * max(strength, 0.0));
	float2 analyticXY = OuterAnalyticBoundaryNormal(worldPos) * sign(metaballGhostBoundaryNormalStrength) * analyticMagnitude;
	float3 analyticNormal = NormalFromStrengthenedXY(analyticXY);
	float3 combinedNormal = ReorientedNormal(analyticNormal, particleNormal);
	float edgeT = smoothstep(0.95, 1.0, filletT);
	combinedNormal = normalize(lerp(combinedNormal, analyticNormal, edgeT));
	return normalize(lerp(particleNormal, combinedNormal, analyticT));
}

float AnalyticBoundaryNormalClipAmount(float2 worldPos)
{
	float filletT;
	float strength;
	if (debugShowClipping == 0 || !TryGetAnalyticBoundaryNormalParams(worldPos, filletT, strength))
	{
		return 0.0;
	}

	float analyticMagnitude = sin(filletT * 1.57079633) * strength;
	return step(1.0, analyticMagnitude * analyticMagnitude);
}

float GetPhaseNormalStrength(bool usePhase1)
{
	float bias = clamp(phase0RenderBias, -1.0, 1.0);
	float biasAmount = abs(bias) * max(phaseBiasNormalStrength, 0.0);
	if (biasAmount <= 0.0001)
	{
		return 1.0;
	}

	bool phase0Expanded = bias > 0.0;
	bool phaseExpanded = usePhase1 ? !phase0Expanded : phase0Expanded;
	float compressedStrength = 1.0 + biasAmount;
	float expandedStrength = rcp(1.0 + biasAmount * 0.25);
	return phaseExpanded ? expandedStrength : compressedStrength;
}

float BlobColourWeight(float3 blobColourSum)
{
	return max(max(blobColourSum.r, blobColourSum.g), blobColourSum.b);
}

float Phase0Density(float4 combined)
{
	return debugMode == 6 ? BlobColourWeight(combined.rgb) : combined.g;
}

float2 GetPhaseNormalXY(float4 normalPacked, float density0, float density1, bool usePhase1)
{
	float phaseDensity = usePhase1 ? density1 : density0;
	float2 encodedNormalXY = usePhase1
		? normalPacked.ba / max(density1, 0.0001)
		: normalPacked.rg / max(density0, 0.0001);
	return phaseDensity > 0.0001 ? encodedNormalXY * 2.0 - 1.0 : 0.0;
}

float3 GetCombinedPhaseNormal(float4 normalPacked, float density0, float density1, float phaseT)
{
	float2 normal0XY = ApplyNormalStrength(GetPhaseNormalXY(normalPacked, density0, density1, false), GetPhaseNormalStrength(false));
	float2 normal1XY = ApplyNormalStrength(GetPhaseNormalXY(normalPacked, density0, density1, true), GetPhaseNormalStrength(true));
	return NormalFromStrengthenedXY(lerp(normal0XY, normal1XY, phaseT));
}

float NormalizedData(float weightedData, float weight, float fallback)
{
	return weight > 0.0001 ? weightedData / weight : fallback;
}

#include "../Lighting/Shared/ParticleFluidGradientSampling.cginc"
#include "../Lighting/Shared/ParticleFluidPhaseAA.cginc"

float SamplePhase0DensityAtUv(float2 uv)
{
	float4 combined = tex2Dlod(CombinedTex, float4(saturate(uv), 0, 0));
	return Phase0Density(combined);
}

float SamplePhase1DensityAtUv(float2 uv)
{
	float4 combined = tex2Dlod(CombinedTex, float4(saturate(uv), 0, 0));
	return combined.a;
}

float SampleAntiAliasedPhaseT(float2 uv, float density0, float density1, float blendWidth)
{
	float2 texel = max(CombinedTex_TexelSize.xy, float2(0.0001, 0.0001));
	float phaseRatio = ParticleFluidPhaseRatio(density0, density1);
	float phaseBoundary = ParticleFluidPhaseBoundary(phase0RenderBias);
	float phaseRatioRight = ParticleFluidPhaseRatio(SamplePhase0DensityAtUv(uv + float2(texel.x, 0.0)), SamplePhase1DensityAtUv(uv + float2(texel.x, 0.0)));
	float phaseRatioLeft = ParticleFluidPhaseRatio(SamplePhase0DensityAtUv(uv - float2(texel.x, 0.0)), SamplePhase1DensityAtUv(uv - float2(texel.x, 0.0)));
	float phaseRatioUp = ParticleFluidPhaseRatio(SamplePhase0DensityAtUv(uv + float2(0.0, texel.y)), SamplePhase1DensityAtUv(uv + float2(0.0, texel.y)));
	float phaseRatioDown = ParticleFluidPhaseRatio(SamplePhase0DensityAtUv(uv - float2(0.0, texel.y)), SamplePhase1DensityAtUv(uv - float2(0.0, texel.y)));
	float phaseGradient = abs(phaseRatioRight - phaseRatioLeft) + abs(phaseRatioUp - phaseRatioDown);
	float phaseAA = max(0.25 * phaseGradient * max(blendWidth, 0.0001), 0.00001);
	return smoothstep(-phaseAA, phaseAA, phaseRatio - phaseBoundary);
}

bool ResolveMetaballMaterial(v2f i, out float alpha, out float phaseT, out float data0, out float data1, out float3 normal, out float3 albedo)
{
	float2 materialUv = i.uv;
	float4 combined = tex2D(CombinedTex, materialUv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	float density = max(density0, density1);
	float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
	if (useEllipticalBounds != 0)
	{
		alpha = min(particleAlpha, OuterAnalyticBoundaryAlphaFromDomainUv(materialUv));
	}
	else
	{
		alpha = particleAlpha;
	}

	if (alpha <= 0.0001)
	{
		phaseT = 0.0;
		data0 = 0.0;
		data1 = 0.0;
		normal = float3(0.0, 0.0, 1.0);
		albedo = 0.0;
		return false;
	}

	phaseT = SampleAntiAliasedPhaseT(materialUv, density0, density1, phaseBlendWidth);
	data0 = combined.r / max(density0, 0.0001);
	data1 = combined.b / max(density1, 0.0001);
	float4 normalPacked = tex2D(NormalTex, materialUv);
	float2 worldPos = ParticleFluidDomainWorldFromUv(materialUv);
	normal = ApplyAnalyticBoundaryNormal(GetCombinedPhaseNormal(normalPacked, density0, density1, phaseT), worldPos);
	float refractionMask = smoothstep(0.0, max(metaballRefractionEdgeFade, 0.0001), density - densityThreshold);
	float2 refractedSourceUv = saturate(materialUv - normal.xy * metaballRefractionStrength * refractionMask);
	float4 refractedCombined = tex2D(CombinedTex, refractedSourceUv);
	float refractedDensity0 = Phase0Density(refractedCombined);
	float refractedDensity1 = refractedCombined.a;
	float refractedDensity = max(refractedDensity0, refractedDensity1);
	float refractedPhaseRatio = ParticleFluidPhaseRatio(refractedDensity0, refractedDensity1);
	float refractedPhaseBoundary = ParticleFluidPhaseBoundary(phase0RenderBias);
	float refractedPhaseT = screenSpaceRefractionCanCrossPhases != 0 && refractedDensity >= densityThreshold ? step(refractedPhaseBoundary, refractedPhaseRatio) : phaseT;
	albedo = ParticleFluidSampleGradientColour(refractedCombined, refractedDensity0, refractedDensity1, data0, data1, refractedPhaseT);
	return true;
}

float4 fragMaterialAlbedo(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float data0;
	float data1;
	float3 normal;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, data0, data1, normal, albedo))
	{
		return 0.0;
	}

	return float4(albedo, alpha);
}

float4 fragMaterialNormal(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float data0;
	float data1;
	float3 normal;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, data0, data1, normal, albedo))
	{
		return 0.0;
	}

	return float4(saturate(normal * 0.5 + 0.5), phaseT);
}

float4 fragMaterialTransport(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float data0;
	float data1;
	float3 normal;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, data0, data1, normal, albedo))
	{
		return 0.0;
	}

	float2 materialUv = i.uv;
	float4 combined = tex2D(CombinedTex, materialUv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	phaseT = SampleAntiAliasedPhaseT(materialUv, density0, density1, transportPhaseBlendWidth);
	float scalarData = lerp(data0, data1, phaseT);
	return float4(scalarData, alpha, phaseT, 0.0);
}

float4 fragUnlitAlbedo(v2f i) : SV_Target
{
	float2 materialUv = i.uv;
	float4 materialAlbedo = tex2D(MaterialAlbedoTex, materialUv);
	if (materialAlbedo.a <= 0.0001)
	{
		discard;
	}

	return materialAlbedo;
}
		ENDHLSL

		Pass {
			Blend One Zero
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialAlbedo
			ENDHLSL
		}

		Pass {
			Blend One Zero
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialNormal
			ENDHLSL
		}

		Pass {
			Blend One Zero
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialTransport
			ENDHLSL
		}

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment fragUnlitAlbedo
			ENDHLSL
		}
	}
}
