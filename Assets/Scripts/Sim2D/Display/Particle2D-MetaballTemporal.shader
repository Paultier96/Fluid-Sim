Shader "Hidden/Particle2DMetaballTemporal" {
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

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D _MainTex;
sampler2D CombinedTex;
sampler2D VelocityTex0;
sampler2D VelocityTex1;
sampler2D CausticHistoryTex;
sampler2D CausticMotionTex;
float4 _MainTex_TexelSize;
float causticMotionDilationRadius;
float densityThreshold;
float phaseBlendWidth;
float phase0RenderBias;
int debugMode;
float motionDebugDeltaTime;
float causticTemporalHistoryWeight;
float causticTemporalHistoryClampStrength;
float causticTemporalClampRejection;
float causticTemporalRejectedSpatialFilter;
int causticTemporalMotionSource;
float2 causticCurrentWorldCenter;
float2 causticCurrentWorldSize;
float2 causticHistoryWorldCenter;
float2 causticHistoryWorldSize;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	return o;
}

float BlobColourWeight(float3 blobColourSum)
{
	return max(max(blobColourSum.r, blobColourSum.g), blobColourSum.b);
}

float Phase0Density(float4 combined)
{
	return debugMode == 6 ? BlobColourWeight(combined.rgb) : combined.g;
}

float ShiftedPhaseT(float density0, float density1)
{
	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float phaseDelta = phaseRatio - phaseBoundary;
	float phaseAA = max(0.5 * fwidth(phaseRatio) * max(phaseBlendWidth, 0.0001), 0.00001);
	return smoothstep(-phaseAA, phaseAA, phaseDelta);
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

float4 fragCausticTemporal(v2f i) : SV_Target
{
	float3 current = tex2D(_MainTex, i.uv).rgb;
	float2 worldPos = causticCurrentWorldCenter + (i.uv - 0.5) * max(causticCurrentWorldSize, float2(0.0001, 0.0001));
	float2 stationaryHistoryUv = (worldPos - causticHistoryWorldCenter) / max(causticHistoryWorldSize, float2(0.0001, 0.0001)) + 0.5;
	float2 historyUv = stationaryHistoryUv;
	if (causticTemporalMotionSource == 1)
	{
		float4 combined = tex2D(CombinedTex, i.uv);
		float density0 = Phase0Density(combined);
		float density1 = combined.a;
		float phaseT = ShiftedPhaseT(density0, density1);
		float4 packedVelocity0 = tex2D(VelocityTex0, i.uv);
		float4 packedVelocity1 = tex2D(VelocityTex1, i.uv);
		float2 weightedVelocity = lerp(packedVelocity0.rg, packedVelocity1.rg, phaseT);
		float weight = lerp(packedVelocity0.b, packedVelocity1.b, phaseT);
		float2 motionWorld = weight > 0.0001 ? weightedVelocity / weight * max(motionDebugDeltaTime, 0.0) : 0.0;
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
	float historyInFrame = step(0.0, historyUv.x) * step(historyUv.x, 1.0) * step(0.0, historyUv.y) * step(historyUv.y, 1.0);
	float3 history = tex2D(CausticHistoryTex, historyUv).rgb;

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

	float historyWeight = saturate(causticTemporalHistoryWeight) * historyInFrame * clampValidity;
	float baseHistoryWeight = saturate(causticTemporalHistoryWeight) * historyInFrame;
	float rejectedT = baseHistoryWeight > 0.0001 ? saturate(1.0 - historyWeight / baseHistoryWeight) : 1.0;
	float3 spatialCurrent = CurrentSpatialFallback(i.uv);
	float3 filteredCurrent = lerp(current, spatialCurrent, rejectedT * saturate(causticTemporalRejectedSpatialFilter));
	float temporalDebug = debugMode == 13 ? clampAmount : historyWeight;
	return float4(lerp(filteredCurrent, validatedHistory, historyWeight), temporalDebug);
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

void ConsiderDilatedMotion(float2 uv, float2 offset, inout float4 bestMotion, inout float bestScore)
{
	float4 candidate = tex2D(_MainTex, uv + offset);
	float candidateScore = MotionDilationScore(candidate);
	if (candidateScore <= bestScore)
	{
		return;
	}

	bestMotion = float4(candidate.xy, saturate(candidate.z), candidate.w);
	bestScore = candidateScore;
}

float4 fragCausticMotionDilate(v2f i) : SV_Target
{
	float4 bestMotion = tex2D(_MainTex, i.uv);
	float bestScore = MotionDilationScore(bestMotion);
	if (bestScore <= 0.0)
	{
		bestMotion = 0.0;
	}

	float radius = max(causticMotionDilationRadius, 0.0);
	if (radius <= 0.001)
	{
		return bestMotion;
	}

	float2 texelOffset = _MainTex_TexelSize.xy * radius;
	ConsiderDilatedMotion(i.uv, texelOffset * float2(1, 0), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-1, 0), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0, 1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0, -1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(1, 1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-1, 1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(1, -1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-1, -1), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0.5, 0), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-0.5, 0), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0, 0.5), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0, -0.5), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0.5, 0.5), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-0.5, 0.5), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(0.5, -0.5), bestMotion, bestScore);
	ConsiderDilatedMotion(i.uv, texelOffset * float2(-0.5, -0.5), bestMotion, bestScore);

	return bestMotion;
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
	}
}
