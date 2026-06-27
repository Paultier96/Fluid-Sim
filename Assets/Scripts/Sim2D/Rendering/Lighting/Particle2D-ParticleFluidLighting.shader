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
sampler2D CausticTex;
sampler2D SoftLightTex;
sampler2D SoftLightTexPhase1;
sampler2D ProjectedShadowTex;
sampler2D CombinedTex;
sampler2D ColourMap;
sampler2D ColourMap2;
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
int particleFluidPhaseDiffuseLightEnabled;
int particleFluidProjectedShadowEnabled;
float2 particleFluidProjectedShadowDirection;
float particleFluidProjectedShadowOffset;
float particleFluidProjectedShadowExpansion;
float particleFluidRadianceCascadeDirectCausticStrength;
float4 particleFluidPhaseDiffuseLightTint[2];
float4 particlePhaseSurface[2];
float4 particlePhaseSoftLight[2];
float particleFluidRadianceCascadePhase0Visibility;
float particleFluidRadianceCascadePhase1Visibility;
float particleFluidIridescenceIntensity;
float particleFluidIridescenceScale;
float4 particleLightBaseDirections[3];
float4 particleLightDirections[3];
float4 particleLightPoints[3];
float4 particleLightColors[3];
float4 particleLightData[3];
float particleAmbientLight;
float4 particleFresnelColor;
float particleFresnelIntensity;
float particleFresnelPower;
float screenSpaceReflectionDistance;
float screenSpaceReflectionEdgePower;
float particleSpecularCausticSampleOffset;
float2 particleSpecularCausticPhaseScale;
float particleTransmissionIntensity;
float particleTransmissionPower;
float particleAmbientOcclusion;
float particleAmbientOcclusionPower;
float2 particleAmbientOcclusionPhaseScale;
float densityThreshold;

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

int ParticleLightType(int lightIndex)
{
	return (int)(particleLightData[lightIndex].y + 0.5);
}

float ParticleLightEnabled(int lightIndex)
{
	return particleLightData[lightIndex].x;
}

float ParticleLightPointFalloff(int lightIndex)
{
	return particleLightData[lightIndex].z;
}

float ParticleLightIntensity(int lightIndex)
{
	return particleLightData[lightIndex].w;
}

float3 ParticlePhaseDiffuseLightTint(int phaseIndex)
{
	return particleFluidPhaseDiffuseLightTint[phaseIndex].rgb;
}

float ParticlePhaseReflectance(int phaseIndex)
{
	return particlePhaseSurface[phaseIndex].x;
}

float ParticlePhaseRoughness(int phaseIndex)
{
	return particlePhaseSurface[phaseIndex].y;
}

float ParticlePhaseMetallic(int phaseIndex)
{
	return particlePhaseSurface[phaseIndex].z;
}

float ParticlePhaseScreenSpaceReflectionStrength(int phaseIndex)
{
	return particlePhaseSurface[phaseIndex].w;
}

float ParticlePhaseCausticAdditiveBlend(int phaseIndex)
{
	return particlePhaseSoftLight[phaseIndex].x;
}

float ParticlePhaseDiffuseAdditiveBlend(int phaseIndex)
{
	return particlePhaseSoftLight[phaseIndex].y;
}

float ParticlePhaseDiffuseNormalInfluence(int phaseIndex)
{
	return particlePhaseSoftLight[phaseIndex].z;
}

float3 ResolveParticleLightDirection(int lightIndex, float2 worldPos)
{
	if (ParticleLightType(lightIndex) == 1)
	{
		return ResolveParticlePointLightDirection(particleLightPoints[lightIndex], worldPos);
	}

	float3 baseLightDirection = particleLightBaseDirections[lightIndex].xyz;
	float3 refractedLightDirection = particleLightDirections[lightIndex].xyz;
	float baseLightDirLength = max(length(baseLightDirection), 0.0001);
	float3 baseLightDir = baseLightDirection / baseLightDirLength;
	float lightDirLength = max(length(refractedLightDirection), 0.0001);
	float3 globalLightDir = refractedLightDirection / lightDirLength;
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

	float2 causticUv = CausticUvFromCameraUv(CameraUvFromMaterialUv(materialUv));
	float3 baseIrradiance = tex2D(CausticTex, causticUv).rgb;
	float normalXYLength = length(normal.xy);
	if (particleSpecularCausticSampleOffset <= 0.0001 || normalXYLength <= 0.0001)
	{
		return baseIrradiance;
	}

	float2 outwardDir = normal.xy / normalXYLength;
	float virtualCapDistance = min(normal.z / max(normalXYLength, 0.02), 128.0);
	float2 outwardOffset = outwardDir * MaterialAlbedoTex_TexelSize.xy * particleSpecularCausticSampleOffset * max(phaseScale, 0.0001) * virtualCapDistance;
	float2 specularUv = saturate(materialUv + outwardOffset);
	float3 offsetIrradiance = tex2D(CausticTex, CausticUvFromCameraUv(CameraUvFromMaterialUv(specularUv))).rgb;
	float offsetBlend = smoothstep(0.05, 0.35, normalXYLength);
	return lerp(baseIrradiance, offsetIrradiance, offsetBlend);
}

float Phase0Density(float4 combined)
{
	return combined.g;
}

float3 SamplePhaseGradientColour(float data, bool usePhase1)
{
	return usePhase1
		? tex2D(ColourMap2, float2(saturate(data), 0.5)).rgb
		: tex2D(ColourMap, float2(saturate(data), 0.5)).rgb;
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
		: particleLightColors[0].rgb * reflectedIrradiance * ParticleLightIntensity(0);
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

float3 ParticleDirectLightTerm(float3 colour, float3 normal, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity, float causticAdditiveBlend)
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
	float3 diffuseCausticColour = lerp(colour, 1.0, saturate(causticAdditiveBlend));
	float diffuseWeight = (1.0 - saturate(metallic)) * (1.0 - surfaceReflectance);
	return
		diffuseCausticColour * directLight * nDotL * diffuseWeight
		+ specularColour * specularLight * specular
		+ diffuseCausticColour * directLight * transmission
	;
}

float3 ApplyParticleLightingWithSource(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity, float causticAdditiveBlend)
{
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	float3 ambient = colour * particleAmbientLight;
	return
		ambient
		+ ParticleDirectLightTerm(colour, normal, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, lightColor, lightIntensity, causticAdditiveBlend)
		+ particleFresnelColor.rgb * fresnel
	;
}

float3 ApplyParticleLighting(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance)
{
	return ApplyParticleLightingWithSource(colour, normal, phaseRadiusScale, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleLightColors[0].rgb, ParticleLightIntensity(0), 0.0);
}

float3 ApplyParticleAdditionalLighting(float3 colour, float3 normal, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, int lightIndex, float causticAdditiveBlend)
{
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	return ParticleDirectLightTerm(colour, normal, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleLightColors[lightIndex].rgb, ParticleLightIntensity(lightIndex), causticAdditiveBlend);
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
	float3 materialSurfaceNormal = normalize(materialNormal.rgb * 2.0 - 1.0);
	float3 normal0 = materialSurfaceNormal;
	float3 normal1 = materialSurfaceNormal;
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
	else if (particleFluidProjectedShadowEnabled != 0 && ParticleLightType(0) == 0)
	{
		float shadowOccupancy = ProjectedShadowOccupancy(worldPos);
		float shadowLight = 1.0 - shadowOccupancy;
		directLightIrradiance0 = shadowLight;
		directLightIrradiance1 = shadowLight;
	}

	float primaryPointAttenuation = ParticleLightType(0) == 1 ? ParticlePointLightAttenuation(particleLightPoints[0], ParticleLightPointFalloff(0), worldPos) : 1.0;
	float3 primaryPointIrradiance = float3(primaryPointAttenuation, primaryPointAttenuation, primaryPointAttenuation);
	float3 specularCausticIrradiance0 = SampleSpecularCausticIrradiance(materialUv, normal0, particleSpecularCausticPhaseScale.x);
	float3 specularCausticIrradiance1 = SampleSpecularCausticIrradiance(materialUv, normal1, particleSpecularCausticPhaseScale.y);
	float3 primaryDirectLightIrradiance0 = ParticleLightType(0) == 1 ? primaryPointIrradiance : directLightIrradiance0;
	float3 primaryDirectLightIrradiance1 = ParticleLightType(0) == 1 ? primaryPointIrradiance : directLightIrradiance1;
	float3 primarySpecularLightIrradiance0 = ParticleLightType(0) == 1 ? primaryPointIrradiance : specularCausticIrradiance0;
	float3 primarySpecularLightIrradiance1 = ParticleLightType(0) == 1 ? primaryPointIrradiance : specularCausticIrradiance1;
	bool primaryUsesCausticLight = particleFluidCausticsEnabled != 0 && ParticleLightType(0) == 0;
	float3 directLightColor = primaryUsesCausticLight ? float3(1.0, 1.0, 1.0) : particleLightColors[0].rgb;
	float directLightIntensity = primaryUsesCausticLight ? 1.0 : ParticleLightIntensity(0);
	float phase0PrimaryCausticAdditiveBlend = primaryUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(0) : 0.0;
	float phase1PrimaryCausticAdditiveBlend = primaryUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(1) : 0.0;
	float3 lightDir = ResolveParticleLightDirection(0, worldPos);
	float3 lit0 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal0, particleAmbientOcclusionPhaseScale.x, lightDir, ParticlePhaseReflectance(0), ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), primaryDirectLightIrradiance0, primarySpecularLightIrradiance0, directLightColor, directLightIntensity, phase0PrimaryCausticAdditiveBlend);
	float3 lit1 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal1, particleAmbientOcclusionPhaseScale.y, lightDir, ParticlePhaseReflectance(1), ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), primaryDirectLightIrradiance1, primarySpecularLightIrradiance1, directLightColor, directLightIntensity, phase1PrimaryCausticAdditiveBlend);
	[unroll]
	for (int lightIndex = 1; lightIndex < 3; lightIndex++)
	{
		if (ParticleLightEnabled(lightIndex) > 0.5)
		{
			float3 additionalLightDir = ResolveParticleLightDirection(lightIndex, worldPos);
			float pointAttenuation = ParticleLightType(lightIndex) == 1 ? ParticlePointLightAttenuation(particleLightPoints[lightIndex], ParticleLightPointFalloff(lightIndex), worldPos) : 1.0;
			float3 pointIrradiance = float3(pointAttenuation, pointAttenuation, pointAttenuation);
			float3 additionalDirectLightIrradiance0 = ParticleLightType(lightIndex) == 1 ? pointIrradiance : directLightIrradiance0;
			float3 additionalDirectLightIrradiance1 = ParticleLightType(lightIndex) == 1 ? pointIrradiance : directLightIrradiance1;
			float3 additionalSpecularLightIrradiance0 = ParticleLightType(lightIndex) == 1 ? pointIrradiance : specularCausticIrradiance0;
			float3 additionalSpecularLightIrradiance1 = ParticleLightType(lightIndex) == 1 ? pointIrradiance : specularCausticIrradiance1;
			bool additionalUsesCausticLight = particleFluidCausticsEnabled != 0 && ParticleLightType(lightIndex) == 0;
			lit0 += ApplyParticleAdditionalLighting(materialAlbedo.rgb, normal0, particleAmbientOcclusionPhaseScale.x, additionalLightDir, ParticlePhaseReflectance(0), ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), additionalDirectLightIrradiance0, additionalSpecularLightIrradiance0, lightIndex, additionalUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(0) : 0.0);
			lit1 += ApplyParticleAdditionalLighting(materialAlbedo.rgb, normal1, particleAmbientOcclusionPhaseScale.y, additionalLightDir, ParticlePhaseReflectance(1), ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), additionalDirectLightIrradiance1, additionalSpecularLightIrradiance1, lightIndex, additionalUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(1) : 0.0);
		}
	}

	if (particleFluidPhaseDiffuseLightEnabled != 0)
	{
		float2 softLightUv = CausticUvFromCameraUv(cameraUv);
		float4 gaussianSoftLight = tex2D(SoftLightTex, softLightUv);
		float3 softLightPhase0 = gaussianSoftLight.rgb;
		float3 softLightPhase1 = tex2D(SoftLightTexPhase1, softLightUv).rgb;
		float gaussianPhaseT = saturate(gaussianSoftLight.a);
		float4 combined = tex2D(CombinedTex, materialUv);
		float density0 = Phase0Density(combined);
		float density1 = combined.a;
		float data0 = density0 >= densityThreshold ? combined.r / max(density0, 0.0001) : 0.0;
		float data1 = density1 >= densityThreshold ? combined.b / max(density1, 0.0001) : 0.0;
		float3 diffuseAlbedo0 = SamplePhaseGradientColour(data0, false);
		float3 diffuseAlbedo1 = SamplePhaseGradientColour(data1, true);
		float additiveBlend0 = saturate(ParticlePhaseDiffuseAdditiveBlend(0));
		float additiveBlend1 = saturate(ParticlePhaseDiffuseAdditiveBlend(1));
		float3 gaussianDiffuse0 = lerp(diffuseAlbedo0, 1.0, additiveBlend0) * ParticlePhaseDiffuseLightTint(0);
		float3 gaussianDiffuse1 = lerp(diffuseAlbedo1, 1.0, additiveBlend1) * ParticlePhaseDiffuseLightTint(1);
		float3 radianceDiffuse0 = lerp(materialAlbedo.rgb, 1.0, additiveBlend0);
		float3 radianceDiffuse1 = lerp(materialAlbedo.rgb, 1.0, additiveBlend1);
		float softNormal0 = lerp(1.0, saturate(dot(normal0, lightDir)), saturate(ParticlePhaseDiffuseNormalInfluence(0)));
		float softNormal1 = lerp(1.0, saturate(dot(normal1, lightDir)), saturate(ParticlePhaseDiffuseNormalInfluence(1)));
		lit0 += softLightPhase0 * (1.0 - gaussianPhaseT) * gaussianDiffuse0 * softNormal0;
		lit1 += softLightPhase0 * gaussianPhaseT * gaussianDiffuse1 * softNormal1;
		lit0 += softLightPhase1 * radianceDiffuse0 * max(particleFluidRadianceCascadePhase0Visibility, 0.0) * softNormal0;
		lit1 += softLightPhase1 * radianceDiffuse1 * max(particleFluidRadianceCascadePhase1Visibility, 0.0) * softNormal1;
	}

	lit0 = ApplyIridescence(lit0, normal0);
	lit1 = ApplyIridescence(lit1, normal1);
	float noise = InterleavedGradientNoise(i.vertex.xy);
	lit0 = ApplyScreenSpaceReflection(lit0, normal0, materialUv, ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), ParticlePhaseScreenSpaceReflectionStrength(0), noise);
	lit1 = ApplyScreenSpaceReflection(lit1, normal1, materialUv, ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), ParticlePhaseScreenSpaceReflectionStrength(1), noise);
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
