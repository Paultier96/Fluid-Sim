Shader "Hidden/Particle2DMetaballMaterial" {
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
sampler2D MaterialAlbedoTex;
sampler2D VelocityTex0;
sampler2D VelocityTex1;
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
float phase0RenderBias;
float phaseBiasNormalStrength;
int useEllipticalBounds;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float obstacleY;
float2 metaballWorldCenter;
float2 metaballWorldSize;
float4 metaballSourceUvRect;
float analyticBoundaryExpansion;
float metaballGhostBoundaryNormalStrength;
float metaballRefractionStrength;
float metaballRefractionEdgeFade;
int screenSpaceRefractionCanCrossPhases;
int debugMode;
int debugShowClipping;
int metaballCompositeRegionEnabled;
float4 metaballCompositeUvRect;
int metaballClipRegionEnabled;
float4 metaballClipRect;
float debugGradientMax;
float motionDebugDeltaTime;
float particleNormalStrength;
v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	if (metaballClipRegionEnabled != 0)
	{
		float2 clipMin = metaballClipRect.xy * 2.0 - 1.0;
		float2 clipMax = (metaballClipRect.xy + metaballClipRect.zw) * 2.0 - 1.0;
		o.vertex.xy = lerp(clipMin, clipMax, v.uv) * o.vertex.w;
	}
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

float3 NormalFromXY(float2 normalXY, float normalStrengthMultiplier)
{
	normalXY = ApplyNormalStrength(normalXY, normalStrengthMultiplier);
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

float2 WorldPosFromUv(float2 uv)
{
	return metaballWorldCenter + (uv - 0.5) * max(metaballWorldSize, float2(0.0001, 0.0001));
}

float2 SourceUvFromMaterialUv(float2 uv)
{
	return metaballSourceUvRect.xy + uv * metaballSourceUvRect.zw;
}

bool TryGetCompositeMaterialUv(float2 screenUv, out float2 materialUv)
{
	if (metaballCompositeRegionEnabled == 0)
	{
		materialUv = screenUv;
		return true;
	}

	float2 localUv = (screenUv - metaballCompositeUvRect.xy) / max(metaballCompositeUvRect.zw, float2(0.000001, 0.000001));
	materialUv = localUv;
	return all(localUv >= 0.0) && all(localUv <= 1.0);
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

float AnalyticBoundaryDistance(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	return max(distances.x, distances.y);
}

float OuterAnalyticBoundaryDistance(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	float outsideDistance = length(max(distances, 0.0)) + min(max(distances.x, distances.y), 0.0);
	return outsideDistance - max(analyticBoundaryExpansion, 0.0);
}

float2 EllipseBoundaryNormal(float2 worldPos)
{
	float2 radii = max(abs(ellipseBoundsSize), 0.0001);
	float2 rel = worldPos - ellipseBoundsCenter;
	float2 q = rel / radii;
	return length(q) > 0.0001
		? normalize(float2(q.x / radii.x, q.y / radii.y))
		: float2(0.0, 1.0);
}

float2 AnalyticBoundaryNormal(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	float2 ellipseNormal = EllipseBoundaryNormal(worldPos);
	float2 cutNormal = float2(0.0, -1.0);
	return distances.x > distances.y ? ellipseNormal : cutNormal;
}

float2 OuterAnalyticBoundaryNormal(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	float2 ellipseNormal = EllipseBoundaryNormal(worldPos);
	float2 cutNormal = float2(0.0, -1.0);
	float2 outsideDistances = max(distances, 0.0);
	if (outsideDistances.x > 0.0 && outsideDistances.y > 0.0)
	{
		return normalize(ellipseNormal * outsideDistances.x + cutNormal * outsideDistances.y);
	}
	return AnalyticBoundaryNormal(worldPos);
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

float3 SampleGradientColour(float2 uv, float fallbackData0, float fallbackData1, float fallbackPhaseT, float noise)
{
	float4 combined = tex2D(CombinedTex, uv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	float sampleDensity = max(density0, density1);
	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float samplePhaseT = screenSpaceRefractionCanCrossPhases != 0 && sampleDensity >= densityThreshold ? step(phaseBoundary, phaseRatio) : fallbackPhaseT;
	float data0 = NormalizedData(combined.r, density0, fallbackData0);
	float data1 = NormalizedData(combined.b, density1, fallbackData1);
	float3 colour0 = tex2D(ColourMap,  float2(saturate(data0), 0.5)).rgb;
	float3 colour1 = tex2D(ColourMap2, float2(saturate(data1), 0.5)).rgb;
	return lerp(colour0, colour1, samplePhaseT);
}

float3 SamplePhaseGradientColour(float data, bool usePhase1, float noise)
{
	return usePhase1
		? tex2D(ColourMap2, float2(saturate(data), 0.5)).rgb
		: tex2D(ColourMap,  float2(saturate(data), 0.5)).rgb;
}
float ShiftedPhaseT(float density0, float density1)
{
	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float phaseDelta = phaseRatio - phaseBoundary;
	float phaseAA = max(0.5 * fwidth(phaseRatio) * max(phaseBlendWidth, 0.0001), 0.00001);
	return smoothstep(-phaseAA, phaseAA, phaseDelta);
}

bool ResolveMetaballMaterial(v2f i, out float alpha, out float phaseT, out float3 normal0, out float3 normal1, out float3 albedo)
{
	float2 materialUv = i.uv;
	float2 sourceUv = SourceUvFromMaterialUv(materialUv);
	float4 combined = tex2D(CombinedTex, sourceUv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	float density = max(density0, density1);
	float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
	if (useEllipticalBounds != 0)
	{
		float boundsDistance = OuterAnalyticBoundaryDistance(WorldPosFromUv(materialUv));
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
		normal0 = float3(0.0, 0.0, 1.0);
		normal1 = float3(0.0, 0.0, 1.0);
		albedo = 0.0;
		return false;
	}

	phaseT = ShiftedPhaseT(density0, density1);
	float data0 = combined.r / max(density0, 0.0001);
	float data1 = combined.b / max(density1, 0.0001);
	float noise = InterleavedGradientNoise(i.vertex.xy);
	float4 normalPacked = tex2D(NormalTex, sourceUv);
	float2 worldPos = WorldPosFromUv(materialUv);
	normal0 = GetPhaseNormal(normalPacked, density0, density1, false);
	normal1 = GetPhaseNormal(normalPacked, density0, density1, true);
	normal0 = ApplyAnalyticBoundaryNormal(normal0, worldPos);
	normal1 = ApplyAnalyticBoundaryNormal(normal1, worldPos);
	float3 blendedNormal = normalize(lerp(normal0, normal1, phaseT));
	float refractionMask = smoothstep(0.0, max(metaballRefractionEdgeFade, 0.0001), density - densityThreshold);
	float2 refractedSourceUv = saturate(sourceUv - blendedNormal.xy * metaballRefractionStrength * refractionMask);
	albedo = SampleGradientColour(refractedSourceUv, data0, data1, phaseT, noise);
	return true;
}

float4 fragMaterialAlbedo(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal0;
	float3 normal1;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, normal0, normal1, albedo))
	{
		return 0.0;
	}

	return float4(albedo, alpha);
}

float4 fragMaterialNormal(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal0;
	float3 normal1;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, normal0, normal1, albedo))
	{
		return 0.0;
	}

	return float4(saturate(normal0 * 0.5 + 0.5), phaseT);
}

float4 fragMaterialNormal1(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal0;
	float3 normal1;
	float3 albedo;
	if (!ResolveMetaballMaterial(i, alpha, phaseT, normal0, normal1, albedo))
	{
		return 0.0;
	}

	return float4(saturate(normal1 * 0.5 + 0.5), alpha);
}

float4 fragUnlitAlbedo(v2f i) : SV_Target
{
	float2 materialUv;
	if (!TryGetCompositeMaterialUv(i.uv, materialUv))
	{
		discard;
	}

	float4 materialAlbedo = tex2D(MaterialAlbedoTex, materialUv);
	if (materialAlbedo.a <= 0.0001)
	{
		discard;
	}

	return materialAlbedo;
}
		ENDCG

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialAlbedo
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialNormal
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialNormal1
			ENDCG
		}

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragUnlitAlbedo
			ENDCG
		}
	}
}
