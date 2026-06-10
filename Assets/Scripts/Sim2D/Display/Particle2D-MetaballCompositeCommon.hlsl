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
sampler2D CausticTex;
sampler2D CausticMotionTex;
sampler2D LightDirectionTex;
sampler2D SoftLightTex;
sampler2D CausticHistoryTex;
float4 CombinedTex_TexelSize;
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
float analyticBoundaryExpansion;
float metaballGhostBoundaryNormalStrength;
float metaballRefractionStrength;
float metaballRefractionEdgeFade;
int screenSpaceRefractionCanCrossPhases;
float metaballIridescenceIntensity;
float metaballIridescenceScale;
int debugMode;
int debugShowClipping;
float debugGradientMax;
float motionDebugDeltaTime;
int metaballCausticsEnabled;
int metaballDirectionalLightFieldEnabled;
int metaballPhaseDiffuseLightEnabled;
float4 metaballPhase0DiffuseLightTint;
float4 metaballPhase1DiffuseLightTint;
float metaballPhase0DiffuseAlbedoTintBlend;
float metaballPhase1DiffuseAlbedoTintBlend;
float causticTemporalHistoryWeight;
int causticTemporalMotionSource;
float2 causticCurrentWorldCenter;
float2 causticCurrentWorldSize;
float2 causticHistoryWorldCenter;
float2 causticHistoryWorldSize;
float3 particleBaseLightDirection;
float3 particleLightDirection;
float4 particleLightColor;
float particleAmbientLight;
float particleLightIntensity;
float particleNormalStrength;
float particlePhase0Reflectance;
float particlePhase1Reflectance;
float particlePhase0Roughness;
float particlePhase1Roughness;
float particlePhase0Metallic;
float particlePhase1Metallic;
float4 particleFresnelColor;
float particleFresnelIntensity;
float particleFresnelPower;
float screenSpaceReflectionStrength0;
float screenSpaceReflectionStrength1;
float screenSpaceReflectionDistance;
float screenSpaceReflectionEdgePower;
float2 particleGlowDirection;
float4 particleGlowColor;
float particleGlowIntensity;
float particleGlowPower;
float particleTransmissionIntensity;
float particleTransmissionPower;
float particleEdgeDarkening;
float particleEdgeDarkeningPower;
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

float3 SampleMetaballAlbedo(float2 uv, float noise)
{
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
	{
		return 0.0;
	}

	float4 combined = tex2D(CombinedTex, uv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	float density = max(density0, density1);
	if (density < densityThreshold)
	{
		return 0.0;
	}

	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float phaseT = step(phaseBoundary, phaseRatio);
	float data0 = combined.r / max(density0, 0.0001);
	float data1 = combined.b / max(density1, 0.0001);
	float3 colour0 = SamplePhaseGradientColour(data0, false, noise);
	float3 colour1 = SamplePhaseGradientColour(data1, true, noise);
	return lerp(colour0, colour1, phaseT);
}
