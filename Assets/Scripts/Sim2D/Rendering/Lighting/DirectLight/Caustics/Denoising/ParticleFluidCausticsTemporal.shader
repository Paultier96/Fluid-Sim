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

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D _MainTex;
sampler2D VelocityTex;
sampler2D CausticHistoryTex;
float4 _MainTex_TexelSize;
float motionDebugDeltaTime;
float causticTemporalHistoryWeight;
float causticTemporalHistoryClampStrength;
float causticTemporalClampRejection;
float causticTemporalRejectedSpatialFilter;
int causticTemporalMotionSource;
float2 causticHistoryWorldCenter;
float2 causticHistoryWorldSize;

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

float4 fragCausticTemporal(v2f i) : SV_Target
{
	float3 current = tex2D(_MainTex, i.uv).rgb;
	float3 filteredCurrent = lerp(current, CurrentSpatialFallback(i.uv), saturate(causticTemporalRejectedSpatialFilter));
	float2 worldPos = ParticleFluidDomainWorldFromUv(i.uv);
	float2 stationaryHistoryUv = (worldPos - causticHistoryWorldCenter) / max(causticHistoryWorldSize, float2(0.0001, 0.0001)) + 0.5;
	float2 historyUv = stationaryHistoryUv;
	if (causticTemporalMotionSource == 1)
	{
		float4 packedVelocity = tex2D(VelocityTex, i.uv);
		float2 weightedVelocity = packedVelocity.rg;
		float weight = packedVelocity.b;
		float2 velocityWorld = weight > 0.0001 ? weightedVelocity / weight : 0.0;
		float2 motionWorld = velocityWorld * max(motionDebugDeltaTime, 0.0);
		float2 motionHistoryUv = stationaryHistoryUv - motionWorld / max(causticHistoryWorldSize, float2(0.0001, 0.0001));
		historyUv = lerp(stationaryHistoryUv, motionHistoryUv, step(0.0001, weight));
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
	float baseHistoryWeight = saturate(causticTemporalHistoryWeight) * historyInFrame;
	float clampRejection = saturate(clampAmount * max(causticTemporalClampRejection, 0.0));
	float historyWeight = baseHistoryWeight * (1.0 - clampRejection);
	float currentLuma = max(dot(current, float3(0.2126, 0.7152, 0.0722)), 0.0);
	float historyLuma = max(dot(history, float3(0.2126, 0.7152, 0.0722)), 0.0);
	float brighteningT = saturate((currentLuma - historyLuma) / max(currentLuma, 0.01));
	float reactiveMask = max(1.0 - historyInFrame, clampRejection);
	reactiveMask = max(reactiveMask, brighteningT * saturate(causticTemporalClampRejection) * clampStrength);
	float effectiveHistoryWeight = historyWeight * (1.0 - reactiveMask);
	float3 currentFallback = lerp(current, filteredCurrent, reactiveMask);
	return float4(lerp(currentFallback, validatedHistory, effectiveHistoryWeight), reactiveMask);
}

float4 fragReprojectHistory(v2f i) : SV_Target
{
	float2 worldPos = ParticleFluidDomainWorldFromUv(i.uv);
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
