Shader "Hidden/Particle2DMetaballDebug" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always

		CGINCLUDE
		#include "UnityCG.cginc"
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
sampler2D NormalTex;
sampler2D VelocityTex0;
sampler2D VelocityTex1;
sampler2D ColourMap;
sampler2D ColourMap2;
sampler2D DebugHeatMap;
sampler2D DebugSignedHeatMap;
sampler2D _MainTex;
sampler2D DebugTex0;
sampler2D DebugTex1;
float4 _MainTex_TexelSize;
int particleCausticTemporalDebugEnabled;
float causticMotionDilationRadius;
float densityThreshold;
float edgeSoftness;
float phaseBlendWidth;
float phase0RenderBias;
float phaseBiasNormalStrength;
int useEllipticalBounds;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float obstacleY;
float2 domainWorldCenter;
float2 domainWorldSize;
float analyticBoundaryExpansion;
#include "../Lighting/Shared/ParticleFluidAnalyticBoundary.hlsl"
float metaballGhostBoundaryNormalStrength;
int screenSpaceRefractionCanCrossPhases;
int debugMode;
int debugShowClipping;
float debugGradientMax;
float motionDebugDeltaTime;
int metaballPhaseDiffuseLightEnabled;
float4 metaballPhase0DiffuseLightTint;
float4 metaballPhase1DiffuseLightTint;
float metaballPhase0DiffuseAdditiveBlend;
float metaballPhase1DiffuseAdditiveBlend;
float causticTemporalHistoryWeight;
int causticTemporalMotionSource;
float particleCausticDebugExposure;
float particleNormalStrength;
float particleNormalProfileCurve;
v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
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
		return GammaToLinearSpace(clipColour);
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


float3 ApplyAnalyticBoundaryNormal(float3 particleNormal, float2 worldPos)
{
	if (useEllipticalBounds == 0 || abs(metaballGhostBoundaryNormalStrength) <= 0.0001)
	{
		return particleNormal;
	}

	float width = max(analyticBoundaryExpansion, 0.0001);
	float shellDistance = analyticBoundaryExpansion > 0.0001
		? OuterAnalyticBoundaryDistance(worldPos) + analyticBoundaryExpansion
		: AnalyticBoundaryDistance(worldPos);
	float filletT = smoothstep(0.0, 1.0, saturate(shellDistance / width));
	float strength = abs(metaballGhostBoundaryNormalStrength);
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
	if (useEllipticalBounds == 0 || debugShowClipping == 0 || abs(metaballGhostBoundaryNormalStrength) <= 0.0001)
	{
		return 0.0;
	}

	float width = max(analyticBoundaryExpansion, 0.0001);
	float shellDistance = analyticBoundaryExpansion > 0.0001
		? OuterAnalyticBoundaryDistance(worldPos) + analyticBoundaryExpansion
		: AnalyticBoundaryDistance(worldPos);
	float filletT = smoothstep(0.0, 1.0, saturate(shellDistance / width));
	float analyticMagnitude = sin(filletT * 1.57079633) * abs(metaballGhostBoundaryNormalStrength);
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

float3 GetPhaseNormal(float4 normalPacked, float density0, float density1, bool usePhase1)
{
	return NormalFromXY(GetPhaseNormalXY(normalPacked, density0, density1, usePhase1), GetPhaseNormalStrength(usePhase1));
}

float PhaseNormalClipAmount(float4 normalPacked, float density0, float density1, bool usePhase1)
{
	float2 normalXY = ApplyNormalStrength(GetPhaseNormalXY(normalPacked, density0, density1, usePhase1), GetPhaseNormalStrength(usePhase1));
	return debugShowClipping != 0 ? step(1.0, dot(normalXY, normalXY)) : 0.0;
}

float3 GetBlendedPhaseNormal(float4 normalPacked, float density0, float density1, float phaseT)
{
	float3 normal0 = GetPhaseNormal(normalPacked, density0, density1, false);
	float3 normal1 = GetPhaseNormal(normalPacked, density0, density1, true);
	return normalize(lerp(normal0, normal1, phaseT));
}

float NormalizedData(float weightedData, float weight, float fallback)
{
	return weight > 0.0001 ? weightedData / weight : fallback;
}

#include "../Lighting/Shared/ParticleFluidGradientSampling.cginc"
#include "../Lighting/Shared/ParticleFluidPhaseAA.cginc"

bool ResolveMetaball(v2f i, out float alpha, out float phaseT, out float density0, out float density1, out float3 litColour, out float3 albedoColour)
{
	float2 materialUv = i.uv;
	float4 combined = tex2D(CombinedTex, materialUv);
	density0 = Phase0Density(combined);
	density1 = combined.a;
	float density = max(density0, density1);
	float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
	if (useEllipticalBounds != 0)
	{
		float boundsDistance = OuterAnalyticBoundaryDistance(ParticleFluidWorldFromUv(materialUv, domainWorldCenter, domainWorldSize));
		float boundsAA = max(fwidth(boundsDistance), 0.0001);
		float boundsAlpha = smoothstep(boundsAA, -boundsAA, boundsDistance);
		alpha = min(particleAlpha, boundsAlpha);
	}
	else
	{
		alpha = particleAlpha;
	}
	if (alpha <= 0.0001)
	{
		phaseT = 0.0;
		litColour = 0.0;
		albedoColour = 0.0;
		return false;
	}

	phaseT = ParticleFluidShiftedPhaseT(density0, density1, phase0RenderBias, phaseBlendWidth);

	float data0 = combined.r / max(density0, 0.0001);
	float data1 = combined.b / max(density1, 0.0001);
	float noise = InterleavedGradientNoise(i.vertex.xy);

	if (debugMode != 0)
	{
		if (debugMode == 6)
		{
			float blobWeight = max(density0, 0.0001);
			float3 blobCol = combined.rgb / blobWeight;
			litColour = saturate(lerp(blobCol, float3(0, 0, 0), phaseT));
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 1)
		{
			float4 normalPacked = tex2D(NormalTex, materialUv);
			float2 worldPos = ParticleFluidWorldFromUv(materialUv, domainWorldCenter, domainWorldSize);
			float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
			normal = ApplyAnalyticBoundaryNormal(normal, worldPos);
			float3 encodedNormal = saturate(0.5 + normal / 2.0);
			float particleClipped = lerp(
				PhaseNormalClipAmount(normalPacked, density0, density1, false),
				PhaseNormalClipAmount(normalPacked, density0, density1, true),
				step(0.5, phaseT));
			float clipped = max(particleClipped, AnalyticBoundaryNormalClipAmount(worldPos));
			float3 debugColour = lerp(encodedNormal, float3(0.0, 1.0, 0.0), clipped);
			#if defined(UNITY_COLORSPACE_GAMMA)
				litColour = debugColour;
			#else
				litColour = GammaToLinearSpace(debugColour);
			#endif
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 2)
		{
			float curvature = data0;
			float heatT = 0.5 + curvature * 0.5;
			litColour = SampleDebugHeatMap(DebugSignedHeatMap, heatT, heatT);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 3)
		{
			float viscosityRaw = lerp(data0, data1, phaseT);
			litColour = SampleDebugHeatMap(DebugHeatMap, viscosityRaw, viscosityRaw);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 4)
		{
			float densityRaw = lerp(data0, data1, phaseT);
			litColour = SampleDebugHeatMap(DebugHeatMap, densityRaw, densityRaw);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 5)
		{
			float tempRaw = lerp(data0, data1, phaseT);
			litColour = SampleDebugHeatMap(DebugHeatMap, tempRaw, tempRaw);
			albedoColour = litColour;
			return true;
		}

		float2 force = float2(data0, data1);
		float2 mapped = saturate(0.5 + force * 0.5);
		float mag = saturate(length(force));
		litColour = float3(mapped, mag);
		albedoColour = litColour;
		return true;
	}

	litColour = ParticleFluidSampleGradientColour(combined, density0, density1, data0, data1, phaseT, screenSpaceRefractionCanCrossPhases, densityThreshold, phase0RenderBias);
	albedoColour = litColour;
	return true;
}

float3 ApplyMotionClipMarker(float rawMotionMagnitude, float3 colour)
{
	if (debugShowClipping != 0 && rawMotionMagnitude > 1.0)
	{
		return HeatMapClipColour(rawMotionMagnitude);
	}

	return colour;
}

float4 frag(v2f i) : SV_Target
{
	float2 causticUv = i.uv;
	if (debugMode == 7)
	{
		return float4(tex2D(DebugTex0, causticUv).rgb / max(particleCausticDebugExposure, 0.0001), 1.0);
	}
	if (debugMode == 8)
	{
		return float4(tex2D(DebugTex0, causticUv).rgb, 1.0);
	}
	if (debugMode == 18)
	{
		return float4(tex2D(DebugTex0, causticUv).rgb, 1.0);
	}
	if (debugMode == 17)
	{
		return float4(tex2D(DebugTex0, causticUv).rgb, 1.0);
	}
	if (debugMode == 10)
	{
		float4 motionDebug = tex2D(DebugTex0, causticUv);
		float2 debugMotion = motionDebug.xy * max(domainWorldSize, float2(0.0001, 0.0001));
		float motionConfidence = saturate(motionDebug.z);
		float motionScale = max(debugGradientMax, 0.0001);
		float rawMotionMagnitude = length(debugMotion) * motionScale;
		float motionMagnitude = saturate(rawMotionMagnitude) * motionConfidence;
		float2 motionDirection = length(debugMotion) > 0.0000001 ? normalize(debugMotion) : 0.0;
		float2 motionColour = saturate(0.5 + motionDirection * 0.5);
		float3 movingColour = float3(motionColour * motionMagnitude, motionMagnitude);
		float3 debugColour = movingColour;
		debugColour = motionConfidence > 0.0 ? ApplyMotionClipMarker(rawMotionMagnitude, debugColour) : debugColour;
		return float4(debugColour, 1.0);
	}
	if (debugMode == 12)
	{
		if (particleCausticTemporalDebugEnabled == 0)
		{
			return float4(0.0, 0.1, 0.35, 1.0);
		}

		float historyWeight = saturate(tex2D(DebugTex0, causticUv).a);
		float3 rejected = float3(1.0, 0.05, 0.0);
		float3 accepted = float3(0.0, 1.0, 0.15);
		return float4(lerp(rejected, accepted, historyWeight), 1.0);
	}
	if (debugMode == 13)
	{
		if (particleCausticTemporalDebugEnabled == 0)
		{
			return float4(0.0, 0.1, 0.35, 1.0);
		}

		float clampAmount = saturate(tex2D(DebugTex0, causticUv).a);
		float3 unclamped = float3(0.0, 0.0, 0.0);
		float3 clamped = float3(0.0, 0.35, 1.0);
		return float4(lerp(unclamped, clamped, clampAmount), 1.0);
	}
	if (debugMode == 14)
	{
		if (particleCausticTemporalDebugEnabled == 0)
		{
			return float4(0.0, 0.1, 0.35, 1.0);
		}

		float projectedShadow = saturate(tex2D(DebugTex0, causticUv).a);
		float3 unshadowed = float3(0.0, 0.0, 0.0);
		float3 shadowed = float3(1.0, 0.6, 0.0);
		return float4(lerp(unshadowed, shadowed, projectedShadow), 1.0);
	}
	if (debugMode == 11)
	{
		float2 materialUv = i.uv;
		float4 combined = tex2D(CombinedTex, materialUv);
		float density0 = Phase0Density(combined);
		float density1 = combined.a;
		float density = max(density0, density1);
		float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
		float alpha = particleAlpha;
		if (useEllipticalBounds != 0)
		{
			float boundsDistance = OuterAnalyticBoundaryDistance(ParticleFluidWorldFromUv(materialUv, domainWorldCenter, domainWorldSize));
			float boundsAA = max(fwidth(boundsDistance), 0.0001);
			float boundsAlpha = smoothstep(boundsAA, -boundsAA, boundsDistance);
			alpha = min(particleAlpha, boundsAlpha);
		}
		if (alpha <= 0.0001)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		float4 packedVelocity0 = tex2D(VelocityTex0, materialUv);
		float4 packedVelocity1 = tex2D(VelocityTex1, materialUv);
		float phaseT = ParticleFluidShiftedPhaseT(density0, density1, phase0RenderBias, phaseBlendWidth);
		float2 weightedVelocity = lerp(packedVelocity0.rg, packedVelocity1.rg, phaseT);
		float weight = lerp(packedVelocity0.b, packedVelocity1.b, phaseT);
		float2 velocity = weight > 0.0001 ? weightedVelocity / weight : 0.0;
		float2 debugMotion = velocity * max(motionDebugDeltaTime, 0.0);
		float motionScale = max(debugGradientMax, 0.0001);
		float rawMotionMagnitude = length(debugMotion) * motionScale;
		float motionMagnitude = saturate(rawMotionMagnitude);
		float2 motionDirection = length(debugMotion) > 0.0000001 ? normalize(debugMotion) : 0.0;
		float2 motionColour = saturate(0.5 + motionDirection * 0.5);
		float3 debugColour = float3(motionColour * motionMagnitude, motionMagnitude);
		debugColour = weight > 0.0001 ? ApplyMotionClipMarker(rawMotionMagnitude, debugColour) : debugColour;
		return float4(debugColour, 1.0);
	}

	float alpha;
	float phaseT;
	float density0;
	float density1;
	float3 colour;
	float3 albedo;
	if (!ResolveMetaball(i, alpha, phaseT, density0, density1, colour, albedo))
	{
		discard;
	}

	return float4(colour, alpha);
}
		ENDCG

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
	}
}
