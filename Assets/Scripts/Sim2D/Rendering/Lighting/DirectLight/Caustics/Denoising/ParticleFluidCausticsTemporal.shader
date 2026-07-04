Shader "Hidden/ParticleFluidCausticsTemporal" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType" = "Opaque" }
		Cull Off
		ZWrite Off
		ZTest Always

		CGINCLUDE
		#include "UnityCG.cginc"
		#include "../../../Shared/ParticleFluidCommon.hlsl"
		#include "../../../Shared/ParticleFluidPhaseAA.cginc"

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D _MainTex;
sampler2D MaterialTransportTex;
sampler2D VelocityTex0;
sampler2D VelocityTex1;
sampler2D CausticHistoryTex;
sampler2D CausticMotionTex;
sampler2D CausticProjectedShadowMapTex;
sampler2D CausticProjectedShadowHistoryTex;
float4 _MainTex_TexelSize;
float causticMotionDilationRadius;
int debugMode;
float motionDebugDeltaTime;
float causticTemporalHistoryWeight;
float causticTemporalHistoryClampStrength;
float causticTemporalClampRejection;
float causticTemporalRejectedSpatialFilter;
int causticTemporalMotionSource;
int causticProjectedShadowMapEnabled;
float2 causticCurrentWorldCenter;
float2 causticCurrentWorldSize;
float2 causticHistoryWorldCenter;
float2 causticHistoryWorldSize;		
float2 causticProjectedShadowDirection;
float2 causticProjectedShadowHistoryDirection;
float causticProjectedShadowOffset;
float causticProjectedShadowExpansion;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	return o;
}

void CurrentNeighbourhoodBounds(float2 uv, out float3 minColour, out float3 maxColour)
{
	float2 texel = _MainTex_TexelSize.xy;
	minColour =  float3(1000000.0, 1000000.0, 1000000.0);
	maxColour = -float3(1000000.0, 1000000.0, 1000000.0);

	[unroll]
	for (int y = -1; y <= 1; y++)
	{
		[unroll]
		for (int x = -1; x <= 1; x++)
		{
			float3 sampleColour = tex2D(_MainTex, uv + texel * float2(x, y)).rgb;
			minColour = min(minColour, sampleColour);
			maxColour = max(maxColour, sampleColour);
		}
	}
}

float3 CurrentSpatialFallback(float2 uv)
{
	float2 texel = _MainTex_TexelSize.xy;
	float3 sum = tex2D(_MainTex, uv).rgb * 4.0;
	float weight = 4.0;

	float3 axial0 = tex2D(_MainTex, uv + texel * float2(1, 0)).rgb;
	float3 axial1 = tex2D(_MainTex, uv + texel * float2(-1, 0)).rgb;
	float3 axial2 = tex2D(_MainTex, uv + texel * float2(0, 1)).rgb;
	float3 axial3 = tex2D(_MainTex, uv + texel * float2(0, -1)).rgb;
	sum += axial0 * 2.0 + axial1 * 2.0 + axial2 * 2.0 + axial3 * 2.0;
	weight += 8.0;

	sum += tex2D(_MainTex, uv + texel * float2(1, 1)).rgb;
	sum += tex2D(_MainTex, uv + texel * float2(-1, 1)).rgb;
	sum += tex2D(_MainTex, uv + texel * float2(1, -1)).rgb;
	sum += tex2D(_MainTex, uv + texel * float2(-1, -1)).rgb;
	weight += 4.0;

	return sum / max(weight, 0.0001);
}

float ProjectedShadowHalfPerp(float2 direction, float2 regionSize)
{
	float2 dirLengthSafe = length(direction) > 0.0001 ? normalize(direction) : float2(0.0, -1.0);
	float2 perp = float2(-dirLengthSafe.y, dirLengthSafe.x);
	return max(0.5 * (abs(perp.x) * regionSize.x + abs(perp.y) * regionSize.y), 0.0001);
}

float ProjectedShadowHalfForward(float2 direction, float2 regionSize)
{
	float2 forward = length(direction) > 0.0001 ? normalize(direction) : float2(0.0, -1.0);
	return max(0.5 * (abs(forward.x) * regionSize.x + abs(forward.y) * regionSize.y), 0.0001);
}

float4 SampleProjectedShadow(float2 worldPos, float2 regionCenter, float2 regionSize, float2 direction, sampler2D shadowMap)
{
	float dirLength = length(direction);
	if (dirLength <= 0.0001)
	{
		return 0.0;
	}

	float2 forward = direction / dirLength;
	float2 perp = float2(-forward.y, forward.x);
	worldPos -= forward * causticProjectedShadowOffset;
	float halfPerp = ProjectedShadowHalfPerp(direction, regionSize);
	float halfForward = ProjectedShadowHalfForward(direction, regionSize);
	float2 rel = worldPos - regionCenter;
	float binT = dot(rel, perp) / halfPerp * 0.5 + 0.5;
	float depthT = dot(rel, forward) / halfForward * 0.5 + 0.5;
	float perpExpansionT = max(causticProjectedShadowExpansion, 0.0) / halfPerp * 0.5;
	float forwardExpansionT = max(causticProjectedShadowExpansion, 0.0) / halfForward * 0.5;
	float4 bestSample = 0.0;
	float bestDepth = 2.0;
	float hasSample = 0.0;
	float centerOccupied = 0.0;
	int occupiedTapCount = 0;
	float binOffsets[5] = { -1.0, -0.5, 0.0, 0.5, 1.0 };

	[unroll]
	for (int tap = 0; tap < 5; tap++)
	{
		float tapBinT = binT + binOffsets[tap] * perpExpansionT;
		float tapInRange = step(0.0, tapBinT) * step(tapBinT, 1.0) * step(0.0, depthT) * step(depthT, 1.0);
		float4 tapSample = tex2D(shadowMap, float2(tapBinT, 0.5));
		float tapOccupied = tapInRange * tapSample.y * step(tapSample.x + 0.01 - forwardExpansionT, depthT);
		if (tap == 2)
		{
			centerOccupied = tapOccupied;
		}
		if (tapOccupied > 0.0)
		{
			occupiedTapCount++;
		}
		if (tapOccupied > 0.0 && tapSample.x < bestDepth)
		{
			bestDepth = tapSample.x;
			bestSample = float4(tapSample.x, tapSample.y, depthT, tapInRange);
			hasSample = 1.0;
		}
	}

	if (centerOccupied <= 0.0 && occupiedTapCount < 2)
	{
		return float4(0.0, 0.0, depthT, 0.0);
	}

	return hasSample > 0.0 ? bestSample : float4(0.0, 0.0, depthT, 0.0);
}

float ProjectedShadowOccupancy(float2 worldPos, float2 regionCenter, float2 regionSize, float2 direction, sampler2D shadowMap)
{
	float4 sample = SampleProjectedShadow(worldPos, regionCenter, regionSize, direction, shadowMap);
	return sample.w * sample.y * step(sample.x + 0.01, sample.z);
}

float4 fragCausticTemporal(v2f i) : SV_Target
{
	float3 current = tex2D(_MainTex, i.uv).rgb;
	float2 worldPos = causticCurrentWorldCenter + (i.uv - 0.5) * max(causticCurrentWorldSize, float2(0.0001, 0.0001));
	float2 stationaryHistoryUv = (worldPos - causticHistoryWorldCenter) / max(causticHistoryWorldSize, float2(0.0001, 0.0001)) + 0.5;
	float2 historyUv = stationaryHistoryUv;
	if (causticTemporalMotionSource == 1)
	{
		float phaseT = tex2D(MaterialTransportTex, i.uv).a;
		float4 packedVelocity0 = tex2D(VelocityTex0, i.uv);
		float4 packedVelocity1 = tex2D(VelocityTex1, i.uv);
		float2 weightedVelocity = lerp(packedVelocity0.rg, packedVelocity1.rg, phaseT);
		float weight = lerp(packedVelocity0.b, packedVelocity1.b, phaseT);
		float2 velocityWorld = weight > 0.0001 ? weightedVelocity / weight : 0.0;
		float2 motionWorld = velocityWorld * max(motionDebugDeltaTime, 0.0);
		float2 motionHistoryUv = stationaryHistoryUv - motionWorld / max(causticHistoryWorldSize, float2(0.0001, 0.0001));
		historyUv = lerp(stationaryHistoryUv, motionHistoryUv, step(0.0001, weight));
	}
	else if (causticTemporalMotionSource == 2)
	{
		float4 motion = tex2D(CausticMotionTex, i.uv);
		float2 motionWorld = motion.xy * causticCurrentWorldSize;
		float2 motionHistoryUv = stationaryHistoryUv - motionWorld / max(causticHistoryWorldSize, float2(0.0001, 0.0001));
		float causticMotionConfidence = smoothstep(0.05, 0.35, saturate(motion.z));
		historyUv = lerp(stationaryHistoryUv, motionHistoryUv, causticMotionConfidence);
	}
	else if (causticTemporalMotionSource == 3 && causticProjectedShadowMapEnabled != 0)
	{
		float4 currentShadowSample = SampleProjectedShadow(worldPos, causticCurrentWorldCenter, causticCurrentWorldSize, causticProjectedShadowDirection, CausticProjectedShadowMapTex);
		float4 previousShadowSample = SampleProjectedShadow(worldPos, causticHistoryWorldCenter, causticHistoryWorldSize, causticProjectedShadowHistoryDirection, CausticProjectedShadowHistoryTex);
		float currentShadowOccupied = currentShadowSample.w * currentShadowSample.y * step(currentShadowSample.x + 0.01, currentShadowSample.z);
		float previousShadowOccupied = previousShadowSample.w * previousShadowSample.y * step(previousShadowSample.x + 0.01, previousShadowSample.z);
		float currentShadowValid = currentShadowOccupied;
		float previousShadowValid = previousShadowOccupied;
		float shadowMotionConfidence = currentShadowValid * previousShadowValid;
		if (shadowMotionConfidence > 0.0)
		{
			float currentDepthWorld = (currentShadowSample.x * 2.0 - 1.0) * ProjectedShadowHalfForward(causticProjectedShadowDirection, causticCurrentWorldSize);
			float previousDepthWorld = (previousShadowSample.x * 2.0 - 1.0) * ProjectedShadowHalfForward(causticProjectedShadowHistoryDirection, causticHistoryWorldSize);
			float2 currentForward = normalize(causticProjectedShadowDirection);
			float2 shadowMotionWorld = currentForward * (currentDepthWorld - previousDepthWorld);
			float2 motionHistoryUv = stationaryHistoryUv - shadowMotionWorld / max(causticHistoryWorldSize, float2(0.0001, 0.0001));
			historyUv = lerp(stationaryHistoryUv, motionHistoryUv, shadowMotionConfidence);
		}
	}
	float historyInFrame = step(0.0, historyUv.x) * step(historyUv.x, 1.0) * step(0.0, historyUv.y) * step(historyUv.y, 1.0);
	float4 historySample = tex2D(CausticHistoryTex, historyUv);
	float historyValidity = step(0.5, historySample.a);
	historyInFrame *= historyValidity;
	float3 history = historySample.rgb;

	float3 minColour;
	float3 maxColour;
	CurrentNeighbourhoodBounds(i.uv, minColour, maxColour);
	float3 neighbourhoodRange = max(maxColour - minColour, 0.0);
	float3 clampMargin = neighbourhoodRange * 0.75 + max(maxColour, current) * 0.08 + 0.0005;
	minColour = max(minColour - clampMargin, 0.0);
	maxColour = maxColour + clampMargin;
	float3 clampedHistory = clamp(history, minColour, maxColour);
	float clampStrength = saturate(causticTemporalHistoryClampStrength);
	float3 validatedHistory = lerp(history, clampedHistory, clampStrength);

	float clampDelta = length(history - clampedHistory) / max(length(history), 0.01);
	float clampAmount = saturate(clampDelta * clampStrength);
	float clampValidity = rcp(1.0 + clampDelta * max(causticTemporalClampRejection, 0.0) * clampStrength);
	float currentShadow = 0.0;
	float previousShadow = 0.0;

	float historyWeight = saturate(causticTemporalHistoryWeight) * historyInFrame * clampValidity;
	float baseHistoryWeight = saturate(causticTemporalHistoryWeight) * historyInFrame;
	float rejectedT = baseHistoryWeight > 0.0001 ? saturate(1.0 - historyWeight / baseHistoryWeight) : 1.0;
	float reactiveMask = max(1.0 - historyInFrame, saturate((rejectedT - 0.2) / 0.6) * saturate((clampAmount - 0.15) / 0.5));
	if (causticProjectedShadowMapEnabled != 0)
	{
		currentShadow = ProjectedShadowOccupancy(worldPos, causticCurrentWorldCenter, causticCurrentWorldSize, causticProjectedShadowDirection, CausticProjectedShadowMapTex);
		previousShadow = ProjectedShadowOccupancy(worldPos, causticHistoryWorldCenter, causticHistoryWorldSize, causticProjectedShadowHistoryDirection, CausticProjectedShadowHistoryTex);
		reactiveMask = max(reactiveMask, saturate(previousShadow - currentShadow));
	}
	float effectiveHistoryWeight = historyWeight * (1.0 - reactiveMask);
	float temporalDebug = debugMode == 13 ? clampAmount : debugMode == 14 ? currentShadow : reactiveMask;
	return float4(lerp(current, validatedHistory, effectiveHistoryWeight), temporalDebug);
}

float MotionDilationScore(float4 motion)
{
	float motionMagnitude = length(motion.xy * causticCurrentWorldSize);
	if (motionMagnitude <= 0.0)
	{
		return 0.0;
	}

	return saturate(motion.z);
}

void AccumulateDilatedMotion(float2 uv, float2 offset, float distanceWeight, inout float2 motionSum, inout float confidenceSum, inout float weightSum, inout float bestNeighborScore)
{
	float4 candidate = tex2D(_MainTex, uv + offset);
	float candidateScore = MotionDilationScore(candidate);
	if (candidateScore <= 0.05)
	{
		return;
	}

	float weight = candidateScore * distanceWeight;
	motionSum += candidate.xy * weight;
	confidenceSum += saturate(candidate.z) * distanceWeight;
	weightSum += weight;
	bestNeighborScore = max(bestNeighborScore, candidateScore);
}

float4 fragCausticMotionDilate(v2f i) : SV_Target
{
	float4 currentMotion = tex2D(_MainTex, i.uv);
	float currentScore = MotionDilationScore(currentMotion);
	if (currentScore >= 0.35)
	{
		return currentMotion;
	}

	float radius = max(causticMotionDilationRadius, 0.0);
	if (radius <= 0.001)
	{
		return currentMotion;
	}

	float2 motionSum = currentMotion.xy * currentScore;
	float confidenceSum = saturate(currentMotion.z);
	float weightSum = currentScore;
	float bestNeighborScore = 0.0;
	float2 texelOffset = _MainTex_TexelSize.xy * radius;
	AccumulateDilatedMotion(i.uv, texelOffset * float2(1, 0), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-1, 0), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0, 1), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0, -1), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(1, 1), 0.7, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-1, 1), 0.7, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(1, -1), 0.7, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-1, -1), 0.7, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0.5, 0), 1.3, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-0.5, 0), 1.3, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0, 0.5), 1.3, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0, -0.5), 1.3, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0.5, 0.5), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-0.5, 0.5), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(0.5, -0.5), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);
	AccumulateDilatedMotion(i.uv, texelOffset * float2(-0.5, -0.5), 1.0, motionSum, confidenceSum, weightSum, bestNeighborScore);

	if (bestNeighborScore <= currentScore || weightSum <= 0.0001)
	{
		return currentMotion;
	}

	float2 filledMotion = motionSum / weightSum;
	float filledConfidence = saturate(confidenceSum / max(weightSum, 0.0001));
	float holeFill = saturate((0.35 - currentScore) / 0.35);
	float4 filled = float4(filledMotion, max(currentMotion.z, filledConfidence), 1.0);
	return lerp(currentMotion, filled, holeFill);
}

float4 fragReprojectHistory(v2f i) : SV_Target
{
	float2 worldPos = causticCurrentWorldCenter + (i.uv - 0.5) * max(causticCurrentWorldSize, float2(0.0001, 0.0001));
	float2 historyUv = (worldPos - causticHistoryWorldCenter) / max(causticHistoryWorldSize, float2(0.0001, 0.0001)) + 0.5;
	float inBounds = step(0.0, historyUv.x) * step(historyUv.x, 1.0) * step(0.0, historyUv.y) * step(historyUv.y, 1.0);
	return inBounds > 0.0 ? tex2D(_MainTex, historyUv) : 0.0;
}

float4 fragCopyWithValidAlpha(v2f i) : SV_Target
{
	float4 sample = tex2D(_MainTex, i.uv);
	return float4(sample.rgb, 1.0);
}
		ENDCG

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragCausticTemporal
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragCausticMotionDilate
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragReprojectHistory
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragCopyWithValidAlpha
			ENDCG
		}
	}
}
