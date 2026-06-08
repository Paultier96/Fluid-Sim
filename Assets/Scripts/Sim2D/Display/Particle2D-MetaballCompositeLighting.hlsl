float3 ApplyScreenSpaceReflection(float3 colour, float3 normal, float2 uv, float roughness, float metallic, float strength, float noise)
{
	if (strength <= 0.000001 || screenSpaceReflectionDistance <= 0.000001)
	{
		return colour;
	}

	float3 viewDir = float3(0.0, 0.0, 1.0);
	float3 reflectedView = reflect(-viewDir, normal);
	float2 reflectionOffset = reflectedView.xy * CombinedTex_TexelSize.xy * screenSpaceReflectionDistance;
	float3 reflectedColour = SampleMetaballAlbedo(uv + reflectionOffset, noise);
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
	if (metaballIridescenceIntensity <= 0.000001)
	{
		return colour;
	}

	float grazing = saturate(1.0 - normal.z);
	float fresnelMask = pow(grazing, 0.75);
	float filmPhase = grazing * metaballIridescenceScale;
	float3 rainbow = IridescenceRamp(frac(filmPhase));
	float amount = saturate(metaballIridescenceIntensity * fresnelMask);
	return lerp(colour, colour * (0.65 + rainbow * 0.7), amount);
}

float ScatteringPhaseMask(float2 uv)
{
	float4 combined = tex2D(CombinedTex, uv);
	float density0 = Phase0Density(combined);
	float density1 = combined.a;
	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float phaseScatteringMask = phaseRatio < phaseBoundary ? metaballPhase0ScatteringEnabled : metaballPhase1ScatteringEnabled;
	return phaseScatteringMask * step(densityThreshold, max(density0, density1));
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

float3 ResolveParticleLightDirection(float2 uv, float2 worldPos)
{
	float baseLightDirLength = max(length(particleBaseLightDirection), 0.0001);
	float3 baseLightDir = particleBaseLightDirection / baseLightDirLength;
	float lightDirLength = max(length(particleLightDirection), 0.0001);
	float3 globalLightDir = particleLightDirection / lightDirLength;
	float boundaryExclusion = AnalyticBoundaryLightExclusion(worldPos);
	if (metaballDirectionalLightFieldEnabled == 0)
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

float3 ApplyParticleLighting(float3 colour, float3 normal, float3 lightDir, float density, float subsurfacePhaseMask, float reflectance, float roughness, float metallic, float3 directLightIrradiance)
{
	float nDotL = saturate(dot(normal, lightDir));
	float3 directLight = particleLightColor.rgb * directLightIrradiance * particleLightIntensity;
	float3 viewDir = float3(0.0, 0.0, 1.0);
	float3 halfVector = lightDir + viewDir;
	float3 halfDir = halfVector / max(length(halfVector), 0.0001);
	float perceptualRoughness = saturate(roughness);
	float roughness2 = max(perceptualRoughness * perceptualRoughness, 0.0004);
	float specularPower = max(1.0, 2.0 / max(roughness2 * roughness2, 0.0001) - 2.0);
	float dielectricReflectance = 0.04 * (1.0 - saturate(metallic));
	float surfaceReflectance = saturate(max(reflectance, dielectricReflectance));
	float specular = pow(saturate(dot(normal, halfDir)), specularPower) * ((specularPower + 2.0) * 0.125) * surfaceReflectance;
	float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
	float2 glowDir = particleGlowDirection / max(length(particleGlowDirection), 0.0001);
	float directionalGlow = pow(saturate(dot(normal.xy, glowDir)), max(particleGlowPower, 0.1)) * saturate(1.0 - normal.z) * particleGlowIntensity;
	float transmission = pow(saturate(dot(-normal, float3(lightDir.xy,0))), max(particleTransmissionPower, 0.1)) * particleTransmissionIntensity;
	float edgeT = pow(saturate(1.0 - normal.z), max(particleEdgeDarkeningPower, 0.1)) * particleEdgeDarkening;
	float thickness = saturate((density - densityThreshold) / max(particleSubsurfaceThickness, 0.0001));
	float thinRegion = 1.0 - thickness;
	float subsurfaceBacklight = pow(saturate(dot(-normal, float3(lightDir.xy, 0.0))), max(particleSubsurfacePower, 0.1));
	float subsurfaceThicknessMask = lerp(thickness, thinRegion, particleSubsurfaceEdgeBoost);
	float subsurface = subsurfacePhaseMask * subsurfaceBacklight * subsurfaceThicknessMask * particleSubsurfaceIntensity;
	float maxColourChannel = max(max(colour.r, colour.g), colour.b);
	float3 metallicSpecularTint = maxColourChannel > 0.0001 ? colour / maxColourChannel : float3(1.0, 1.0, 1.0);
	float3 specularColour = lerp(float3(1.0, 1.0, 1.0), metallicSpecularTint, saturate(metallic));
	float diffuseWeight = (1.0 - saturate(metallic)) * (1.0 - surfaceReflectance);
	colour *= 1.0 - edgeT;
	float3 ambient = colour * particleAmbientLight;
	return
		ambient
		+ colour * directLight * nDotL * diffuseWeight
		+ specularColour * directLight * specular
		+ particleFresnelColor.rgb * fresnel
		+ particleGlowColor.rgb * directLight * directionalGlow
		+ colour * directLight * transmission
		+ particleSubsurfaceColor.rgb * directLight * subsurface
	;
}
