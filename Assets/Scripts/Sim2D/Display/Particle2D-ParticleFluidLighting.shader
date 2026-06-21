Shader "Hidden/Particle2DParticleFluidLighting" {
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

sampler2D MaterialAlbedoTex;
sampler2D MaterialNormalTex;
sampler2D MaterialNormalTex1;
sampler2D CausticTex;
sampler2D LightDirectionTex;
sampler2D SoftLightTex;
sampler2D SoftLightTexPhase1;
sampler2D ProjectedShadowTex;
float4 MaterialAlbedoTex_TexelSize;
float2 particleFluidWorldCenter;
float2 particleFluidWorldSize;
int particleFluidCompositeRegionEnabled;
float4 particleFluidCompositeUvRect;
float4 particleFluidCameraUvRect;
int particleFluidClipRegionEnabled;
float4 particleFluidClipRect;
int useEllipticalBounds;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float obstacleY;
float analyticBoundaryExpansion;
int particleFluidCausticsEnabled;
int particleFluidCausticRegionEnabled;
float4 particleFluidCausticUvRect;
int particleFluidDirectionalLightFieldEnabled;
int particleFluidPhaseDiffuseLightEnabled;
int particleFluidProjectedShadowEnabled;
float2 particleFluidProjectedShadowDirection;
float particleFluidProjectedShadowOffset;
float particleFluidProjectedShadowExpansion;
float particleFluidRadianceCascadeDirectCausticStrength;
float4 particleFluidPhase0DiffuseLightTint;
float4 particleFluidPhase1DiffuseLightTint;
float particleFluidRadianceCascadePhase0Visibility;
float particleFluidRadianceCascadePhase1Visibility;
float particleFluidIridescenceIntensity;
float particleFluidIridescenceScale;
int particleLightType;
float3 particleBaseLightDirection;
float3 particleLightDirection;
float4 particleLightPoint;
float particleLightPointFalloff;
int particleSecondaryLightEnabled;
int particleSecondaryLightType;
float3 particleSecondaryBaseLightDirection;
float3 particleSecondaryLightDirection;
float4 particleSecondaryLightPoint;
float particleSecondaryLightPointFalloff;
int particleTertiaryLightEnabled;
int particleTertiaryLightType;
float3 particleTertiaryBaseLightDirection;
float3 particleTertiaryLightDirection;
float4 particleTertiaryLightPoint;
float particleTertiaryLightPointFalloff;
float4 particleLightColor;
float4 particleSecondaryLightColor;
float4 particleTertiaryLightColor;
float particleAmbientLight;
float particleLightIntensity;
float particleSecondaryLightIntensity;
float particleTertiaryLightIntensity;
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
float particleSpecularCausticSampleOffset;
float2 particleSpecularCausticPhaseScale;
float particleTransmissionIntensity;
float particleTransmissionPower;
float particleAmbientOcclusion;
float particleAmbientOcclusionPower;
float2 particleAmbientOcclusionPhaseScale;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	if (particleFluidClipRegionEnabled != 0)
	{
		float2 clipMin = particleFluidClipRect.xy * 2.0 - 1.0;
		float2 clipMax = (particleFluidClipRect.xy + particleFluidClipRect.zw) * 2.0 - 1.0;
		o.vertex.xy = lerp(clipMin, clipMax, v.uv) * o.vertex.w;
	}
	return o;
}

float InterleavedGradientNoise(float2 pixel)
{
	float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	return frac(magic.z * frac(dot(pixel, magic.xy)));
}

float2 WorldPosFromUv(float2 uv)
{
	return particleFluidWorldCenter + (uv - 0.5) * max(particleFluidWorldSize, float2(0.0001, 0.0001));
}

bool TryGetMaterialUv(float2 screenUv, out float2 materialUv)
{
	if (particleFluidCompositeRegionEnabled == 0)
	{
		materialUv = screenUv;
		return true;
	}

	float2 localUv = (screenUv - particleFluidCompositeUvRect.xy) / max(particleFluidCompositeUvRect.zw, float2(0.000001, 0.000001));
	materialUv = localUv;
	return all(localUv >= 0.0) && all(localUv <= 1.0);
}

float2 CameraUvFromMaterialUv(float2 materialUv)
{
	return particleFluidCameraUvRect.xy + materialUv * particleFluidCameraUvRect.zw;
}

float2 CausticUvFromCameraUv(float2 cameraUv)
{
	if (particleFluidCausticRegionEnabled == 0)
	{
		return cameraUv;
	}

	return (cameraUv - particleFluidCausticUvRect.xy) / max(particleFluidCausticUvRect.zw, float2(0.000001, 0.000001));
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
	worldPos -= forward * particleFluidProjectedShadowOffset;
	float halfPerp = ProjectedShadowHalfPerp(direction, regionSize);
	float halfForward = ProjectedShadowHalfForward(direction, regionSize);
	float2 rel = worldPos - regionCenter;
	float binT = dot(rel, perp) / halfPerp * 0.5 + 0.5;
	float depthT = dot(rel, forward) / halfForward * 0.5 + 0.5;
	float perpExpansionT = max(particleFluidProjectedShadowExpansion, 0.0) / halfPerp * 0.5;
	float forwardExpansionT = max(particleFluidProjectedShadowExpansion, 0.0) / halfForward * 0.5;
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

float ProjectedShadowOccupancy(float2 worldPos)
{
	float4 sample = SampleProjectedShadow(worldPos, particleFluidWorldCenter, particleFluidWorldSize, particleFluidProjectedShadowDirection, ProjectedShadowTex);
	return sample.w * sample.y * step(sample.x + 0.01, sample.z);
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

float AnalyticBoundaryLightExclusion(float2 worldPos)
{
	if (useEllipticalBounds == 0 || analyticBoundaryExpansion <= 0.0001)
	{
		return 0.0;
	}

	float shellDistance = OuterAnalyticBoundaryDistance(worldPos) + analyticBoundaryExpansion;
	return step(0.0, shellDistance);
}

float3 ResolveParticlePointLightDirection(float4 pointLight, float2 worldPos)
{
	float3 toLight = float3(pointLight.xy - worldPos, max(pointLight.z, 0.0001));
	return normalize(toLight);
}

float ParticlePointLightAttenuation(float4 pointLight, float falloff, float2 worldPos)
{
	float range = max(pointLight.w, 0.0001);
	float planarDistance = length(pointLight.xy - worldPos);
	return pow(saturate(1.0 - planarDistance / range), max(falloff, 0.1));
}

float3 ResolveParticleLightDirection(float2 cameraUv, float2 worldPos)
{
	if (particleLightType == 1)
	{
		return ResolveParticlePointLightDirection(particleLightPoint, worldPos);
	}

	float baseLightDirLength = max(length(particleBaseLightDirection), 0.0001);
	float3 baseLightDir = particleBaseLightDirection / baseLightDirLength;
	float lightDirLength = max(length(particleLightDirection), 0.0001);
	float3 globalLightDir = particleLightDirection / lightDirLength;
	float boundaryExclusion = AnalyticBoundaryLightExclusion(worldPos);
	if (particleFluidDirectionalLightFieldEnabled == 0)
	{
		return normalize(lerp(globalLightDir, baseLightDir, boundaryExclusion));
	}

	float4 localDirection = tex2D(LightDirectionTex, CausticUvFromCameraUv(cameraUv));
	float localDirectionLength = length(localDirection.xy);
	if (localDirection.z <= 0.0 || localDirectionLength <= 0.0001)
	{
		return normalize(lerp(globalLightDir, baseLightDir, boundaryExclusion));
	}

	float planarLength = length(globalLightDir.xy);
	float2 localLightXY = -localDirection.xy / localDirectionLength * planarLength;
	float3 localLightDir = normalize(float3(localLightXY, globalLightDir.z));
	return normalize(lerp(localLightDir, baseLightDir, boundaryExclusion));
}

float3 ResolveParticleSecondaryLightDirection(float2 worldPos)
{
	if (particleSecondaryLightType == 1)
	{
		return ResolveParticlePointLightDirection(particleSecondaryLightPoint, worldPos);
	}

	float baseLightDirLength = max(length(particleSecondaryBaseLightDirection), 0.0001);
	float3 baseLightDir = particleSecondaryBaseLightDirection / baseLightDirLength;
	float lightDirLength = max(length(particleSecondaryLightDirection), 0.0001);
	float3 globalLightDir = particleSecondaryLightDirection / lightDirLength;
	float boundaryExclusion = AnalyticBoundaryLightExclusion(worldPos);
	return normalize(lerp(globalLightDir, baseLightDir, boundaryExclusion));
}

float3 ResolveParticleTertiaryLightDirection(float2 worldPos)
{
	if (particleTertiaryLightType == 1)
	{
		return ResolveParticlePointLightDirection(particleTertiaryLightPoint, worldPos);
	}

	float baseLightDirLength = max(length(particleTertiaryBaseLightDirection), 0.0001);
	float3 baseLightDir = particleTertiaryBaseLightDirection / baseLightDirLength;
	float lightDirLength = max(length(particleTertiaryLightDirection), 0.0001);
	float3 globalLightDir = particleTertiaryLightDirection / lightDirLength;
	float boundaryExclusion = AnalyticBoundaryLightExclusion(worldPos);
	return normalize(lerp(globalLightDir, baseLightDir, boundaryExclusion));
}

float3 SampleMetaballAlbedo(float2 uv, float noise)
{
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
	{
		return 0.0;
	}

	return tex2D(MaterialAlbedoTex, uv).rgb;
}

float3 SampleSpecularCausticIrradiance(float2 materialUv, float3 normal, float phaseScale)
{
	if (particleFluidCausticsEnabled == 0)
	{
		return 1.0;
	}

	float normalXYLength = length(normal.xy);
	if (particleSpecularCausticSampleOffset <= 0.0001 || normalXYLength <= 0.0001)
	{
		return tex2D(CausticTex, CausticUvFromCameraUv(CameraUvFromMaterialUv(materialUv))).rgb;
	}

	float2 outwardDir = normal.xy / normalXYLength;
	float virtualCapDistance = min(normal.z / max(normalXYLength, 0.02), 128.0);
	float2 outwardOffset = outwardDir * MaterialAlbedoTex_TexelSize.xy * particleSpecularCausticSampleOffset * max(phaseScale, 0.0001) * virtualCapDistance;
	float2 specularUv = saturate(materialUv + outwardOffset);
	return tex2D(CausticTex, CausticUvFromCameraUv(CameraUvFromMaterialUv(specularUv))).rgb;
}

float3 ApplyScreenSpaceReflection(float3 colour, float3 normal, float2 uv, float roughness, float metallic, float strength, float noise)
{
	if (strength <= 0.000001 || screenSpaceReflectionDistance <= 0.000001)
	{
		return colour;
	}

	float3 viewDir = float3(0.0, 0.0, 1.0);
	float3 reflectedView = reflect(-viewDir, normal);
	float2 reflectionOffset = reflectedView.xy * MaterialAlbedoTex_TexelSize.xy * screenSpaceReflectionDistance;
	float2 reflectedUv = uv + reflectionOffset;
	float3 reflectedColour = SampleMetaballAlbedo(reflectedUv, noise);
	float3 reflectedIrradiance = particleFluidCausticsEnabled != 0 ? tex2D(CausticTex, CausticUvFromCameraUv(CameraUvFromMaterialUv(saturate(reflectedUv)))).rgb : 1.0;
	reflectedColour *= particleFluidCausticsEnabled != 0
		? reflectedIrradiance
		: particleLightColor.rgb * reflectedIrradiance * particleLightIntensity;
	float maxColourChannel = max(max(colour.r, colour.g), colour.b);
	float3 metallicTint = maxColourChannel > 0.0001 ? colour / maxColourChannel : float3(1.0, 1.0, 1.0);
	reflectedColour *= lerp(float3(1.0, 1.0, 1.0), metallicTint, saturate(metallic));
	float edgeMask = pow(saturate(1.0 - normal.z), max(screenSpaceReflectionEdgePower, 0.1));
	float roughnessMask = saturate(1.0 - roughness * 0.5);
	return colour + reflectedColour * (strength * edgeMask * roughnessMask);
}

float3 IridescenceRamp(float phase)
{
	float3 offsets = float3(0.0, 0.33, 0.67);
	return 0.5 + 0.5 * cos(6.2831853 * (phase + offsets));
}

float3 ApplyIridescence(float3 colour, float3 normal)
{
	if (particleFluidIridescenceIntensity <= 0.000001)
	{
		return colour;
	}

	float grazing = saturate(1.0 - normal.z);
	float fresnelMask = pow(grazing, 0.75);
	float filmPhase = grazing * particleFluidIridescenceScale;
	float3 rainbow = IridescenceRamp(frac(filmPhase));
	float amount = saturate(particleFluidIridescenceIntensity * fresnelMask);
	return lerp(colour, colour * (0.65 + rainbow * 0.7), amount);
}

float PhaseAmbientOcclusion(float3 normal, float phaseRadiusScale)
{
	float scaledPower = max(particleAmbientOcclusionPower * max(phaseRadiusScale, 0.1), 0.1);
	return pow(saturate(1.0 - normal.z), scaledPower) * particleAmbientOcclusion;
}

float3 ParticleDirectLightTerm(float3 colour, float3 normal, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity)
{
	float nDotL = saturate(dot(normal, lightDir));
	float3 directLight = lightColor * directLightIrradiance * lightIntensity;
	float3 specularLight = lightColor * specularLightIrradiance * lightIntensity;
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float3 halfVector = lightDir + viewDir;
	float3 halfDir = halfVector / max(length(halfVector), 0.0001);
	float perceptualRoughness = saturate(roughness);
	float roughness2 = max(perceptualRoughness * perceptualRoughness, 0.0004);
	float specularPower = max(1.0, 2.0 / max(roughness2 * roughness2, 0.0001) - 2.0);
	float dielectricReflectance = 0.04 * (1.0 - saturate(metallic));
	float surfaceReflectance = saturate(max(reflectance, dielectricReflectance));
	float specular = pow(saturate(dot(normal, halfDir)), specularPower) * ((specularPower + 2.0) * 0.125) * surfaceReflectance;
	float transmission = pow(saturate(dot(-normal, float3(lightDir.xy,0))), max(particleTransmissionPower, 0.1)) * particleTransmissionIntensity;
	float maxColourChannel = max(max(colour.r, colour.g), colour.b);
	float3 metallicSpecularTint = maxColourChannel > 0.0001 ? colour / maxColourChannel : float3(1.0, 1.0, 1.0);
	float3 specularColour = lerp(float3(1.0, 1.0, 1.0), metallicSpecularTint, saturate(metallic));
	float diffuseWeight = (1.0 - saturate(metallic)) * (1.0 - surfaceReflectance);
	return
		colour * directLight * nDotL * diffuseWeight
		+ specularColour * specularLight * specular
		+ colour * directLight * transmission
	;
}

float3 ApplyParticleLightingWithSource(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity)
{
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	float3 ambient = colour * particleAmbientLight;
	return
		ambient
		+ ParticleDirectLightTerm(colour, normal, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, lightColor, lightIntensity)
		+ particleFresnelColor.rgb * fresnel
	;
}

float3 ApplyParticleLighting(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance)
{
	return ApplyParticleLightingWithSource(colour, normal, phaseRadiusScale, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleLightColor.rgb, particleLightIntensity);
}

float3 ApplyParticleSecondaryLighting(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance)
{
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	return ParticleDirectLightTerm(colour, normal, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleSecondaryLightColor.rgb, particleSecondaryLightIntensity);
}

float3 ApplyParticleTertiaryLighting(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance)
{
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	return ParticleDirectLightTerm(colour, normal, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleTertiaryLightColor.rgb, particleTertiaryLightIntensity);
}
float4 fragSplitLighting(v2f i) : SV_Target
{
	float2 materialUv;
	if (!TryGetMaterialUv(i.uv, materialUv))
	{
		discard;
	}

	float2 cameraUv = CameraUvFromMaterialUv(materialUv);
	float4 materialAlbedo = tex2D(MaterialAlbedoTex, materialUv);
	float alpha = materialAlbedo.a;
	if (alpha <= 0.0001)
	{
		discard;
	}

	float4 materialNormal = tex2D(MaterialNormalTex, materialUv);
	float4 materialNormal1 = tex2D(MaterialNormalTex1, materialUv);
	float3 normal0 = normalize(materialNormal.rgb * 2.0 - 1.0);
	float3 normal1 = normalize(materialNormal1.rgb * 2.0 - 1.0);
	float phaseT = saturate(materialNormal.a);
	float2 worldPos = WorldPosFromUv(materialUv);
	float3 directLightIrradiance0 = 1.0;
	float3 directLightIrradiance1 = 1.0;
	if (particleFluidCausticsEnabled != 0)
	{
		float3 lightField = tex2D(CausticTex, CausticUvFromCameraUv(cameraUv)).rgb;
		float softLightDirectCausticStrength = particleFluidPhaseDiffuseLightEnabled != 0 ? saturate(particleFluidRadianceCascadeDirectCausticStrength) : 1.0;
		float phase0DirectCausticStrength = softLightDirectCausticStrength;
		float phase1DirectCausticStrength = 1.0;
		directLightIrradiance0 = lightField * phase0DirectCausticStrength;
		directLightIrradiance1 = lightField * phase1DirectCausticStrength;
	}
	else if (particleFluidProjectedShadowEnabled != 0 && particleLightType == 0)
	{
		float shadowOccupancy = ProjectedShadowOccupancy(worldPos);
		float shadowLight = 1.0 - shadowOccupancy;
		directLightIrradiance0 = shadowLight;
		directLightIrradiance1 = shadowLight;
	}

	float primaryPointAttenuation = particleLightType == 1 ? ParticlePointLightAttenuation(particleLightPoint, particleLightPointFalloff, worldPos) : 1.0;
	float3 primaryPointIrradiance = float3(primaryPointAttenuation, primaryPointAttenuation, primaryPointAttenuation);
	float3 specularCausticIrradiance0 = SampleSpecularCausticIrradiance(materialUv, normal0, particleSpecularCausticPhaseScale.x);
	float3 specularCausticIrradiance1 = SampleSpecularCausticIrradiance(materialUv, normal1, particleSpecularCausticPhaseScale.y);
	float3 primaryDirectLightIrradiance0 = particleLightType == 1 ? primaryPointIrradiance : directLightIrradiance0;
	float3 primaryDirectLightIrradiance1 = particleLightType == 1 ? primaryPointIrradiance : directLightIrradiance1;
	float3 primarySpecularLightIrradiance0 = particleLightType == 1 ? primaryPointIrradiance : specularCausticIrradiance0;
	float3 primarySpecularLightIrradiance1 = particleLightType == 1 ? primaryPointIrradiance : specularCausticIrradiance1;
	bool primaryUsesCausticLight = particleFluidCausticsEnabled != 0 && particleLightType == 0;
	float3 directLightColor = primaryUsesCausticLight ? float3(1.0, 1.0, 1.0) : particleLightColor.rgb;
	float directLightIntensity = primaryUsesCausticLight ? 1.0 : particleLightIntensity;
	float3 lightDir = ResolveParticleLightDirection(cameraUv, worldPos);
	float3 lit0 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal0, particleAmbientOcclusionPhaseScale.x, lightDir, particlePhase0Reflectance, particlePhase0Roughness, particlePhase0Metallic, primaryDirectLightIrradiance0, primarySpecularLightIrradiance0, directLightColor, directLightIntensity);
	float3 lit1 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal1, particleAmbientOcclusionPhaseScale.y, lightDir, particlePhase1Reflectance, particlePhase1Roughness, particlePhase1Metallic, primaryDirectLightIrradiance1, primarySpecularLightIrradiance1, directLightColor, directLightIntensity);
	if (particleSecondaryLightEnabled != 0)
	{
		float3 secondaryLightDir = ResolveParticleSecondaryLightDirection(worldPos);
		float secondaryPointAttenuation = particleSecondaryLightType == 1 ? ParticlePointLightAttenuation(particleSecondaryLightPoint, particleSecondaryLightPointFalloff, worldPos) : 1.0;
		float3 secondaryPointIrradiance = float3(secondaryPointAttenuation, secondaryPointAttenuation, secondaryPointAttenuation);
		float3 secondaryDirectLightIrradiance0 = particleSecondaryLightType == 1 ? secondaryPointIrradiance : directLightIrradiance0;
		float3 secondaryDirectLightIrradiance1 = particleSecondaryLightType == 1 ? secondaryPointIrradiance : directLightIrradiance1;
		float3 secondarySpecularLightIrradiance0 = particleSecondaryLightType == 1 ? secondaryPointIrradiance : specularCausticIrradiance0;
		float3 secondarySpecularLightIrradiance1 = particleSecondaryLightType == 1 ? secondaryPointIrradiance : specularCausticIrradiance1;
		lit0 += ApplyParticleSecondaryLighting(materialAlbedo.rgb, normal0, particleAmbientOcclusionPhaseScale.x, secondaryLightDir, particlePhase0Reflectance, particlePhase0Roughness, particlePhase0Metallic, secondaryDirectLightIrradiance0, secondarySpecularLightIrradiance0);
		lit1 += ApplyParticleSecondaryLighting(materialAlbedo.rgb, normal1, particleAmbientOcclusionPhaseScale.y, secondaryLightDir, particlePhase1Reflectance, particlePhase1Roughness, particlePhase1Metallic, secondaryDirectLightIrradiance1, secondarySpecularLightIrradiance1);
	}
	if (particleTertiaryLightEnabled != 0)
	{
		float3 tertiaryLightDir = ResolveParticleTertiaryLightDirection(worldPos);
		float tertiaryPointAttenuation = particleTertiaryLightType == 1 ? ParticlePointLightAttenuation(particleTertiaryLightPoint, particleTertiaryLightPointFalloff, worldPos) : 1.0;
		float3 tertiaryPointIrradiance = float3(tertiaryPointAttenuation, tertiaryPointAttenuation, tertiaryPointAttenuation);
		float3 tertiaryDirectLightIrradiance0 = particleTertiaryLightType == 1 ? tertiaryPointIrradiance : directLightIrradiance0;
		float3 tertiaryDirectLightIrradiance1 = particleTertiaryLightType == 1 ? tertiaryPointIrradiance : directLightIrradiance1;
		float3 tertiarySpecularLightIrradiance0 = particleTertiaryLightType == 1 ? tertiaryPointIrradiance : specularCausticIrradiance0;
		float3 tertiarySpecularLightIrradiance1 = particleTertiaryLightType == 1 ? tertiaryPointIrradiance : specularCausticIrradiance1;
		lit0 += ApplyParticleTertiaryLighting(materialAlbedo.rgb, normal0, particleAmbientOcclusionPhaseScale.x, tertiaryLightDir, particlePhase0Reflectance, particlePhase0Roughness, particlePhase0Metallic, tertiaryDirectLightIrradiance0, tertiarySpecularLightIrradiance0);
		lit1 += ApplyParticleTertiaryLighting(materialAlbedo.rgb, normal1, particleAmbientOcclusionPhaseScale.y, tertiaryLightDir, particlePhase1Reflectance, particlePhase1Roughness, particlePhase1Metallic, tertiaryDirectLightIrradiance1, tertiarySpecularLightIrradiance1);
	}

	if (particleFluidPhaseDiffuseLightEnabled != 0)
	{
		float2 softLightUv = CausticUvFromCameraUv(cameraUv);
		float3 softLightPhase0 = tex2D(SoftLightTex, softLightUv).rgb;
		float3 softLightPhase1 = tex2D(SoftLightTexPhase1, softLightUv).rgb;
		lit0 += softLightPhase0 * (1.0 - phaseT) * particleFluidPhase0DiffuseLightTint.rgb;
		lit0 += softLightPhase1 * max(particleFluidRadianceCascadePhase0Visibility, 0.0) * particleFluidPhase1DiffuseLightTint.rgb;
		lit1 += softLightPhase1 * max(particleFluidRadianceCascadePhase1Visibility, 0.0) * particleFluidPhase1DiffuseLightTint.rgb;
	}

	lit0 = ApplyIridescence(lit0, normal0);
	lit1 = ApplyIridescence(lit1, normal1);
	float noise = InterleavedGradientNoise(i.vertex.xy);
	lit0 = ApplyScreenSpaceReflection(lit0, normal0, materialUv, particlePhase0Roughness, particlePhase0Metallic, screenSpaceReflectionStrength0, noise);
	lit1 = ApplyScreenSpaceReflection(lit1, normal1, materialUv, particlePhase1Roughness, particlePhase1Metallic, screenSpaceReflectionStrength1, noise);
	float3 colour = lerp(lit0, lit1, phaseT);
	return float4(colour, alpha);
}
		ENDCG

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragSplitLighting
			ENDCG
		}
	}
}
