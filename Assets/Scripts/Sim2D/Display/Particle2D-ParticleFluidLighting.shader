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
float4 MaterialAlbedoTex_TexelSize;
float2 particleFluidWorldCenter;
float2 particleFluidWorldSize;
int useEllipticalBounds;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float obstacleY;
float analyticBoundaryExpansion;
int particleFluidCausticsEnabled;
int particleFluidDirectionalLightFieldEnabled;
int particleFluidPhaseDiffuseLightEnabled;
int particleFluidSoftLightPhase0Only;
float particleFluidRadianceCascadeDirectCausticStrength;
float4 particleFluidPhase0DiffuseLightTint;
float4 particleFluidPhase1DiffuseLightTint;
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

float3 ResolveParticleLightDirection(float2 uv, float2 worldPos)
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

	float4 localDirection = tex2D(LightDirectionTex, uv);
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

float3 SampleSpecularCausticIrradiance(float2 uv, float3 normal, float phaseScale)
{
	if (particleFluidCausticsEnabled == 0)
	{
		return 1.0;
	}

	float normalXYLength = length(normal.xy);
	if (particleSpecularCausticSampleOffset <= 0.0001 || normalXYLength <= 0.0001)
	{
		return tex2D(CausticTex, uv).rgb;
	}

	float2 outwardDir = normal.xy / normalXYLength;
	float virtualCapDistance = min(normal.z / max(normalXYLength, 0.02), 128.0);
	float2 outwardOffset = outwardDir * MaterialAlbedoTex_TexelSize.xy * particleSpecularCausticSampleOffset * max(phaseScale, 0.0001) * virtualCapDistance;
	float2 specularUv = saturate(uv + outwardOffset);
	return tex2D(CausticTex, specularUv).rgb;
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
	float3 reflectedIrradiance = particleFluidCausticsEnabled != 0 ? tex2D(CausticTex, reflectedUv).rgb : 1.0;
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
	float4 materialAlbedo = tex2D(MaterialAlbedoTex, i.uv);
	float alpha = materialAlbedo.a;
	if (alpha <= 0.0001)
	{
		discard;
	}

	float4 materialNormal = tex2D(MaterialNormalTex, i.uv);
	float4 materialNormal1 = tex2D(MaterialNormalTex1, i.uv);
	float3 normal0 = normalize(materialNormal.rgb * 2.0 - 1.0);
	float3 normal1 = normalize(materialNormal1.rgb * 2.0 - 1.0);
	float phaseT = saturate(materialNormal.a);
	float2 worldPos = WorldPosFromUv(i.uv);
	float3 directLightIrradiance0 = 1.0;
	float3 directLightIrradiance1 = 1.0;
	if (particleFluidCausticsEnabled != 0)
	{
		float3 lightField = tex2D(CausticTex, i.uv).rgb;
		float softLightDirectCausticStrength = particleFluidPhaseDiffuseLightEnabled != 0 ? saturate(particleFluidRadianceCascadeDirectCausticStrength) : 1.0;
		float phase0DirectCausticStrength = softLightDirectCausticStrength;
		float phase1DirectCausticStrength = particleFluidSoftLightPhase0Only == 0 ? softLightDirectCausticStrength : 1.0;
		directLightIrradiance0 = lightField * phase0DirectCausticStrength;
		directLightIrradiance1 = lightField * phase1DirectCausticStrength;
	}

	float primaryPointAttenuation = particleLightType == 1 ? ParticlePointLightAttenuation(particleLightPoint, particleLightPointFalloff, worldPos) : 1.0;
	float3 primaryPointIrradiance = float3(primaryPointAttenuation, primaryPointAttenuation, primaryPointAttenuation);
	float3 specularCausticIrradiance0 = SampleSpecularCausticIrradiance(i.uv, normal0, particleSpecularCausticPhaseScale.x);
	float3 specularCausticIrradiance1 = SampleSpecularCausticIrradiance(i.uv, normal1, particleSpecularCausticPhaseScale.y);
	float3 primaryDirectLightIrradiance0 = particleLightType == 1 ? primaryPointIrradiance : directLightIrradiance0;
	float3 primaryDirectLightIrradiance1 = particleLightType == 1 ? primaryPointIrradiance : directLightIrradiance1;
	float3 primarySpecularLightIrradiance0 = particleLightType == 1 ? primaryPointIrradiance : specularCausticIrradiance0;
	float3 primarySpecularLightIrradiance1 = particleLightType == 1 ? primaryPointIrradiance : specularCausticIrradiance1;
	bool primaryUsesCausticLight = particleFluidCausticsEnabled != 0 && particleLightType == 0;
	float3 directLightColor = primaryUsesCausticLight ? float3(1.0, 1.0, 1.0) : particleLightColor.rgb;
	float directLightIntensity = primaryUsesCausticLight ? 1.0 : particleLightIntensity;
	float3 lightDir = ResolveParticleLightDirection(i.uv, worldPos);
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
		float4 softLight = tex2D(SoftLightTex, i.uv);
		lit0 += softLight.rgb * (1.0 - phaseT) * particleFluidPhase0DiffuseLightTint.rgb;
		if (particleFluidSoftLightPhase0Only == 0)
		{
			lit1 += softLight.rgb * phaseT * particleFluidPhase1DiffuseLightTint.rgb;
		}
	}

	lit0 = ApplyIridescence(lit0, normal0);
	lit1 = ApplyIridescence(lit1, normal1);
	float noise = InterleavedGradientNoise(i.vertex.xy);
	lit0 = ApplyScreenSpaceReflection(lit0, normal0, i.uv, particlePhase0Roughness, particlePhase0Metallic, screenSpaceReflectionStrength0, noise);
	lit1 = ApplyScreenSpaceReflection(lit1, normal1, i.uv, particlePhase1Roughness, particlePhase1Metallic, screenSpaceReflectionStrength1, noise);
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
