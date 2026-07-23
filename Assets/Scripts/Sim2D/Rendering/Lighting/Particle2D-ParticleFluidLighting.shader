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
		#include "Shared/ParticleFluidCommon.hlsl"

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
sampler2D MaterialTransportTex;
sampler2D CausticTex;
sampler2D SoftLightTex;
sampler2D SoftLightTexPhase1;
#include "Shared/ParticleFluidGradientSampling.cginc"
float4 MaterialAlbedoTex_TexelSize;
#include "Shared/ParticleFluidAnalyticBoundary.hlsl"
int particleFluidCausticsEnabled;
int particleFluidGaussianSssEnabled;
int particleFluidRadianceCascadeEnabled;
int particleGaussianPhase0Only;
float particleFluidRadianceCascadeDirectCausticStrength;
float4 particleFluidPhaseDiffuseLightTint[2];
float4 particlePhaseSurface[2];
float4 particlePhaseSoftLight[2];
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
float particleSpecularAntiAliasingStrength;
float2 particlePhaseScale;
float particleTransmissionIntensity;
float particleTransmissionPower;
float particleAmbientOcclusion;
float particleAmbientOcclusionPower;
float densityThreshold;

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

	float3 baseIrradiance = tex2D(CausticTex, materialUv).rgb;
	float normalXYLength = length(normal.xy);
	if (particleSpecularCausticSampleOffset <= 0.0001 || normalXYLength <= 0.0001)
	{
		return baseIrradiance;
	}

	float2 outwardDir = normal.xy / normalXYLength;
	float virtualCapDistance = min(normal.z / max(normalXYLength, 0.02), 128.0);
	float2 outwardOffset = outwardDir * MaterialAlbedoTex_TexelSize.xy * particleSpecularCausticSampleOffset * max(phaseScale, 0.0001) * virtualCapDistance;
	float2 specularUv = materialUv + outwardOffset;
	float3 offsetIrradiance = 1.0;
	if (specularUv.x >= 0.0 && specularUv.x <= 1.0 && specularUv.y >= 0.0 && specularUv.y <= 1.0)
	{
		float2 specularWorldPos = ParticleFluidDomainWorldFromUv(specularUv);
		if (useEllipticalBounds == 0 || AnalyticBoundaryDistance(specularWorldPos) <= 0.0)
		{
			offsetIrradiance = tex2D(CausticTex, specularUv).rgb;
		}
	}
	float offsetBlend = smoothstep(0.05, 0.35, normalXYLength);
	return lerp(baseIrradiance, offsetIrradiance, offsetBlend);
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
	float3 reflectedIrradiance = particleFluidCausticsEnabled != 0 ? tex2D(CausticTex, saturate(reflectedUv)).rgb : 1.0;
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

float ApplySpecularAntiAliasing(float perceptualRoughness, float3 normal, float phaseT)
{
	if (particleSpecularAntiAliasingStrength <= 0.0001)
	{
		return saturate(perceptualRoughness);
	}

	float3 dndx = ddx(normal);
	float3 dndy = ddy(normal);
	float normalVariance = dot(dndx, dndx) + dot(dndy, dndy);
	float phaseBoundaryDistance = abs(phaseT - 0.5);
	float phaseBoundaryWidth = max(fwidth(phaseT) * 2.0, 0.0001);
	float interiorMask = smoothstep(phaseBoundaryWidth, phaseBoundaryWidth * 3.0, phaseBoundaryDistance);
	float roughness2 = perceptualRoughness * perceptualRoughness + normalVariance * particleSpecularAntiAliasingStrength * interiorMask;
	return saturate(sqrt(roughness2));
}

float3 ParticleDirectLightTerm(float3 colour, float3 normal, float phaseT, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity, float causticAdditiveBlend)
{
	float nDotL = saturate(dot(normal, lightDir));
	float3 directLight = lightColor * directLightIrradiance * lightIntensity;
	float3 specularLight = lightColor * specularLightIrradiance * lightIntensity;
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float3 halfVector = lightDir + viewDir;
	float3 halfDir = halfVector / max(length(halfVector), 0.0001);
	float perceptualRoughness = ApplySpecularAntiAliasing(saturate(roughness), normal, phaseT);
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

float3 ApplyParticleLightingWithSource(float3 colour, float3 normal, float phaseT, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, float3 lightColor, float lightIntensity, float causticAdditiveBlend)
{
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	float3 ambient = colour * particleAmbientLight;
	return
		ambient
		+ ParticleDirectLightTerm(colour, normal, phaseT, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, lightColor, lightIntensity, causticAdditiveBlend)
		+ particleFresnelColor.rgb * fresnel
	;
}

float3 ApplyParticleLighting(float3 colour, float3 normal, float phaseT, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance)
{
	return ApplyParticleLightingWithSource(colour, normal, phaseT, phaseRadiusScale, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleLightColors[0].rgb, ParticleLightIntensity(0), 0.0);
}

float3 ApplyParticleAdditionalLighting(float3 colour, float3 normal, float phaseT, float phaseRadiusScale, float3 lightDir, float reflectance, float roughness, float metallic, float3 directLightIrradiance, float3 specularLightIrradiance, int lightIndex, float causticAdditiveBlend)
{
	float ambientOcclusion = PhaseAmbientOcclusion(normal, phaseRadiusScale);
	colour *= 1.0 - ambientOcclusion;
	return ParticleDirectLightTerm(colour, normal, phaseT, lightDir, reflectance, roughness, metallic, directLightIrradiance, specularLightIrradiance, particleLightColors[lightIndex].rgb, ParticleLightIntensity(lightIndex), causticAdditiveBlend);
}
float4 fragSplitLighting(v2f i) : SV_Target
{
	float2 materialUv = i.uv;
	float4 materialAlbedo = tex2D(MaterialAlbedoTex, materialUv);
	float alpha = materialAlbedo.a;
	if (alpha <= 0.0001)
	{
		discard;
	}

	float4 materialNormal = tex2D(MaterialNormalTex, materialUv);
	float3 normal = normalize(materialNormal.rgb * 2.0 - 1.0);
	float4 materialTransport = tex2D(MaterialTransportTex, materialUv);
	float phaseT = saturate(materialNormal.a);
	float phaseData = materialTransport.r;
	float2 worldPos = ParticleFluidDomainWorldFromUv(materialUv);
	float3 directLightIrradiance0 = 1.0;
	float3 directLightIrradiance1 = 1.0;
	if (particleFluidCausticsEnabled != 0)
	{
		float3 lightField = tex2D(CausticTex, materialUv).rgb;
		float softLightDirectCausticStrength = particleFluidRadianceCascadeEnabled != 0 ? saturate(particleFluidRadianceCascadeDirectCausticStrength) : 1.0;
		float phase0DirectCausticStrength = softLightDirectCausticStrength;
		float phase1DirectCausticStrength = 1.0;
		directLightIrradiance0 = lightField * phase0DirectCausticStrength;
		directLightIrradiance1 = lightField * phase1DirectCausticStrength;
	}
	float primaryPointAttenuation = ParticleLightType(0) == 1 ? ParticlePointLightAttenuation(particleLightPoints[0], ParticleLightPointFalloff(0), worldPos) : 1.0;
	float3 primaryPointIrradiance = float3(primaryPointAttenuation, primaryPointAttenuation, primaryPointAttenuation);
	float3 specularCausticIrradiance0 = SampleSpecularCausticIrradiance(materialUv, normal, particlePhaseScale.x);
	float3 specularCausticIrradiance1 = SampleSpecularCausticIrradiance(materialUv, normal, particlePhaseScale.y);
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
	float3 lit0 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal, phaseT, particlePhaseScale.x, lightDir, ParticlePhaseReflectance(0), ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), primaryDirectLightIrradiance0, primarySpecularLightIrradiance0, directLightColor, directLightIntensity, phase0PrimaryCausticAdditiveBlend);
	float3 lit1 = ApplyParticleLightingWithSource(materialAlbedo.rgb, normal, phaseT, particlePhaseScale.y, lightDir, ParticlePhaseReflectance(1), ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), primaryDirectLightIrradiance1, primarySpecularLightIrradiance1, directLightColor, directLightIntensity, phase1PrimaryCausticAdditiveBlend);
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
			lit0 += ApplyParticleAdditionalLighting(materialAlbedo.rgb, normal, phaseT, particlePhaseScale.x, additionalLightDir, ParticlePhaseReflectance(0), ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), additionalDirectLightIrradiance0, additionalSpecularLightIrradiance0, lightIndex, additionalUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(0) : 0.0);
			lit1 += ApplyParticleAdditionalLighting(materialAlbedo.rgb, normal, phaseT, particlePhaseScale.y, additionalLightDir, ParticlePhaseReflectance(1), ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), additionalDirectLightIrradiance1, additionalSpecularLightIrradiance1, lightIndex, additionalUsesCausticLight ? ParticlePhaseCausticAdditiveBlend(1) : 0.0);
		}
	}

	float2 softLightUv = materialUv;
	float additiveBlend0 = saturate(ParticlePhaseDiffuseAdditiveBlend(0));
	float additiveBlend1 = saturate(ParticlePhaseDiffuseAdditiveBlend(1));
	float lightNormal = saturate(dot(normal, lightDir));
	float softNormal0 = lerp(1.0, lightNormal, saturate(ParticlePhaseDiffuseNormalInfluence(0)));
	float softNormal1 = lerp(1.0, lightNormal, saturate(ParticlePhaseDiffuseNormalInfluence(1)));
	if (particleFluidGaussianSssEnabled != 0)
	{
		float4 gaussianSoftLight = tex2D(SoftLightTex, softLightUv);
		float3 softLightPhase0 = gaussianSoftLight.rgb;
		float3 diffuseAlbedo0 = ParticleFluidSamplePhaseGradientColour(phaseData, false);
		float3 diffuseAlbedo1 = ParticleFluidSamplePhaseGradientColour(phaseData, true);
		float3 gaussianDiffuse0 = lerp(diffuseAlbedo0, 1.0, additiveBlend0) * ParticlePhaseDiffuseLightTint(0);
		float3 gaussianDiffuse1 = lerp(diffuseAlbedo1, 1.0, additiveBlend1) * ParticlePhaseDiffuseLightTint(1);
		lit0 += softLightPhase0 * (1.0 - phaseT) * gaussianDiffuse0 * softNormal0;
		if (particleGaussianPhase0Only == 0)
		{
			lit1 += softLightPhase0 * phaseT * gaussianDiffuse1 * softNormal1;
		}
	}

	if (particleFluidRadianceCascadeEnabled != 0)
	{
		float3 softLightPhase1 = tex2D(SoftLightTexPhase1, softLightUv).rgb;
		float3 radianceDiffuse1 = lerp(materialAlbedo.rgb, 1.0, additiveBlend1);
		lit1 += softLightPhase1 * radianceDiffuse1 * softNormal1;
	}

	lit0 = ApplyIridescence(lit0, normal);
	lit1 = ApplyIridescence(lit1, normal);
	float noise = InterleavedGradientNoise(i.vertex.xy);
	lit0 = ApplyScreenSpaceReflection(lit0, normal, materialUv, ParticlePhaseRoughness(0), ParticlePhaseMetallic(0), ParticlePhaseScreenSpaceReflectionStrength(0), noise);
	lit1 = ApplyScreenSpaceReflection(lit1, normal, materialUv, ParticlePhaseRoughness(1), ParticlePhaseMetallic(1), ParticlePhaseScreenSpaceReflectionStrength(1), noise);
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
