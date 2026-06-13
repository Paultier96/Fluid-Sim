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
		historyUv = lerp(stationaryHistoryUv, motionHistoryUv, smoothstep(0.05, 0.35, saturate(motion.z)));
	}
	float historyInFrame = step(0.0, historyUv.x) * step(historyUv.x, 1.0) * step(0.0, historyUv.y) * step(historyUv.y, 1.0);
	float3 history = tex2D(CausticHistoryTex, historyUv).rgb;
	return float4(lerp(current, history, saturate(causticTemporalHistoryWeight) * historyInFrame), 1.0);
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
