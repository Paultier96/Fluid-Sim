float ShiftedPhaseT(float density0, float density1)
{
	float phaseRatio = density1 / max(density0 + density1, 0.0001);
	float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
	float phaseDelta = phaseRatio - phaseBoundary;
	float phaseAA = max(0.5 * fwidth(phaseRatio) * max(phaseBlendWidth, 0.0001), 0.00001);
	return smoothstep(-phaseAA, phaseAA, phaseDelta);
}

bool ResolveMetaball(v2f i, out float alpha, out float phaseT, out float density0, out float density1, out float3 litColour, out float3 albedoColour)
{
	float4 combined = tex2D(CombinedTex, i.uv);
	density0 = Phase0Density(combined);
	density1 = combined.a;
	float density = max(density0, density1);
	float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
	if (useEllipticalBounds != 0)
	{
		float boundsDistance = OuterAnalyticBoundaryDistance(WorldPosFromUv(i.uv));
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
		litColour = 0.0;
		albedoColour = 0.0;
		return false;
	}

	phaseT = ShiftedPhaseT(density0, density1);

	float data0 = combined.r / max(density0, 0.0001);
	float data1 = combined.b / max(density1, 0.0001);
	float noise = InterleavedGradientNoise(i.vertex.xy);

	if (debugMode != 0)
	{
		if (debugMode == 6)
		{
			float blobWeight = max(density0, 0.0001);
			float3 blobCol = combined.rgb / blobWeight;
			litColour = saturate(lerp(blobCol, float3(0, 0, 0), phaseT));
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 1)
		{
			float4 normalPacked = tex2D(NormalTex, i.uv);
			float2 worldPos = WorldPosFromUv(i.uv);
			float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
			normal = ApplyAnalyticBoundaryNormal(normal, worldPos);
			float3 encodedNormal = saturate(0.5 + normal / 2.0);
			float particleClipped = lerp(
				PhaseNormalClipAmount(normalPacked, density0, density1, false),
				PhaseNormalClipAmount(normalPacked, density0, density1, true),
				step(0.5, phaseT));
			float clipped = max(particleClipped, AnalyticBoundaryNormalClipAmount(worldPos));
			float3 debugColour = lerp(encodedNormal, float3(0.0, 1.0, 0.0), clipped);
			#if defined(UNITY_COLORSPACE_GAMMA)
				litColour = debugColour;
			#else
				litColour = GammaToLinearSpace(debugColour);
			#endif
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 2)
		{
			float curvature = data0;
			float heatT = 0.5 + curvature * 0.5;
			litColour = SampleDebugHeatMap(DebugSignedHeatMap, heatT, heatT);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 3)
		{
			float viscosityRaw = lerp(data0, data1, phaseT);
			float viscosity = Dither01(viscosityRaw, noise);
			litColour = SampleDebugHeatMap(DebugHeatMap, viscosityRaw, viscosity);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 4)
		{
			float densityRaw = lerp(data0, data1, phaseT);
			float densityVal = Dither01(densityRaw, noise);
			litColour = SampleDebugHeatMap(DebugHeatMap, densityRaw, densityVal);
			albedoColour = litColour;
			return true;
		}

		if (debugMode == 5)
		{
			float tempRaw = lerp(data0, data1, phaseT);
			float tempVal = Dither01(tempRaw, noise);
			litColour = SampleDebugHeatMap(DebugHeatMap, tempRaw, tempVal);
			albedoColour = litColour;
			return true;
		}

		float2 force = float2(data0, data1);
		float2 mapped = saturate(0.5 + force * 0.5);
		float mag = saturate(length(force));
		litColour = float3(mapped, mag);
		albedoColour = litColour;
		return true;
	}

	float4 normalPacked = tex2D(NormalTex, i.uv);
	float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
	float2 worldPos = WorldPosFromUv(i.uv);
	normal = ApplyAnalyticBoundaryNormal(normal, worldPos);
	float refractionMask = smoothstep(0.0, max(metaballRefractionEdgeFade, 0.0001), density - densityThreshold);
	float2 refractedUv = saturate(i.uv - normal.xy * metaballRefractionStrength * refractionMask);
	float3 refractedColour = SampleGradientColour(refractedUv, data0, data1, phaseT, noise);
	float3 normal0 = GetPhaseNormal(normalPacked, density0, density1, false);
	float3 normal1 = GetPhaseNormal(normalPacked, density0, density1, true);
	normal0 = ApplyAnalyticBoundaryNormal(normal0, worldPos);
	normal1 = ApplyAnalyticBoundaryNormal(normal1, worldPos);
	float3 colour0 = refractedColour;
	float3 colour1 = refractedColour;
	albedoColour = refractedColour;
	float maxDensity = max(density0, density1);
	float3 directLightIrradiance0 = 1.0;
	float3 directLightIrradiance1 = 1.0;
	if (metaballCausticsEnabled != 0)
	{
		float3 lightField = tex2D(CausticTex, i.uv).rgb;
		float3 scatteredLight = 0.0;
		if (metaballScatteredLightEnabled != 0)
		{
			scatteredLight = tex2D(ScatteredLightTex, i.uv).rgb * metaballScatteredLightIntensity;
		}
		directLightIrradiance0 = lightField + scatteredLight * metaballPhase0ScatteringEnabled;
		directLightIrradiance1 = lightField + scatteredLight * metaballPhase1ScatteringEnabled;
	}
	float3 lightDir = ResolveParticleLightDirection(i.uv, worldPos);
	float3 lit0 = ApplyParticleLighting(colour0, normal0, lightDir, maxDensity, 1.0, particlePhase0Reflectance, particlePhase0Roughness, particlePhase0Metallic, directLightIrradiance0);
	float3 lit1 = ApplyParticleLighting(colour1, normal1, lightDir, maxDensity, 0.0, particlePhase1Reflectance, particlePhase1Roughness, particlePhase1Metallic, directLightIrradiance1);
	lit0 = ApplyIridescence(lit0, normal0);
	lit1 = ApplyIridescence(lit1, normal1);
	lit0 = ApplyScreenSpaceReflection(lit0, normal0, i.uv, particlePhase0Roughness, particlePhase0Metallic, screenSpaceReflectionStrength0, noise);
	lit1 = ApplyScreenSpaceReflection(lit1, normal1, i.uv, particlePhase1Roughness, particlePhase1Metallic, screenSpaceReflectionStrength1, noise);
	litColour = lerp(lit0, lit1, phaseT);
	return true;
}

float3 ApplyMotionClipMarker(float rawMotionMagnitude, float3 colour)
{
	if (debugShowClipping != 0 && rawMotionMagnitude > 1.0)
	{
		return HeatMapClipColour(rawMotionMagnitude);
	}

	return colour;
}

float4 frag(v2f i) : SV_Target
{
	if (debugMode == 7)
	{
		float3 causticDebug = tex2D(CausticTex, i.uv).rgb;
		if (metaballScatteredLightEnabled != 0)
		{
			causticDebug += tex2D(ScatteredLightTex, i.uv).rgb * metaballScatteredLightIntensity * ScatteringPhaseMask(i.uv);
		}
		return float4(causticDebug, 1.0);
	}
	if (debugMode == 8)
	{
		float3 scatteredDebug = 0.0;
		if (metaballScatteredLightEnabled != 0)
		{
			scatteredDebug = tex2D(ScatteredLightTex, i.uv).rgb * metaballScatteredLightIntensity * ScatteringPhaseMask(i.uv);
		}
		return float4(scatteredDebug, 1.0);
	}
	if (debugMode == 9)
	{
		if (metaballDirectionalLightFieldEnabled == 0)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		float4 directionDebug = tex2D(LightDirectionTex, i.uv);
		float validDirection = saturate(directionDebug.z);
		float3 encodedDirection = float3(directionDebug.xy * 0.5 + 0.5, validDirection);
		return float4(encodedDirection * validDirection, 1.0);
	}
	if (debugMode == 10)
	{
		float4 motionDebug = tex2D(CausticMotionTex, i.uv);
		float2 debugMotion = motionDebug.xy * metaballWorldSize;
		float motionConfidence = saturate(motionDebug.z);
		float motionScale = max(debugGradientMax, 0.0001);
		float rawMotionMagnitude = length(debugMotion) * motionScale;
		float motionMagnitude = saturate(rawMotionMagnitude) * motionConfidence;
		float2 motionDirection = length(debugMotion) > 0.0000001 ? normalize(debugMotion) : 0.0;
		float2 motionColour = saturate(0.5 + motionDirection * 0.5);
		float3 movingColour = float3(motionColour * motionMagnitude, motionMagnitude);
		float3 debugColour = movingColour;
		debugColour = motionConfidence > 0.0 ? ApplyMotionClipMarker(rawMotionMagnitude, debugColour) : debugColour;
		return float4(debugColour, 1.0);
	}
	if (debugMode == 11)
	{
		float4 combined = tex2D(CombinedTex, i.uv);
		float density0 = Phase0Density(combined);
		float density1 = combined.a;
		float density = max(density0, density1);
		float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
		float alpha = particleAlpha;
		if (useEllipticalBounds != 0)
		{
			float boundsDistance = OuterAnalyticBoundaryDistance(WorldPosFromUv(i.uv));
			float boundsAA = max(fwidth(boundsDistance), 0.0001);
			float boundsAlpha = smoothstep(boundsAA, -boundsAA, boundsDistance);
			alpha = min(particleAlpha, boundsAlpha);
		}
		if (alpha <= 0.0001)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		float4 packedVelocity0 = tex2D(VelocityTex0, i.uv);
		float4 packedVelocity1 = tex2D(VelocityTex1, i.uv);
		float phaseT = ShiftedPhaseT(density0, density1);
		float2 weightedVelocity = lerp(packedVelocity0.rg, packedVelocity1.rg, phaseT);
		float weight = lerp(packedVelocity0.b, packedVelocity1.b, phaseT);
		float2 velocity = weight > 0.0001 ? weightedVelocity / weight : 0.0;
		float2 debugMotion = velocity * max(motionDebugDeltaTime, 0.0);
		float motionScale = max(debugGradientMax, 0.0001);
		float rawMotionMagnitude = length(debugMotion) * motionScale;
		float motionMagnitude = saturate(rawMotionMagnitude);
		float2 motionDirection = length(debugMotion) > 0.0000001 ? normalize(debugMotion) : 0.0;
		float2 motionColour = saturate(0.5 + motionDirection * 0.5);
		float3 debugColour = float3(motionColour * motionMagnitude, motionMagnitude);
		debugColour = weight > 0.0001 ? ApplyMotionClipMarker(rawMotionMagnitude, debugColour) : debugColour;
		return float4(debugColour, 1.0);
	}

	float alpha;
	float phaseT;
	float density0;
	float density1;
	float3 colour;
	float3 albedo;
	if (!ResolveMetaball(i, alpha, phaseT, density0, density1, colour, albedo))
	{
		discard;
	}

	if (debugMode == 0)
	{
		colour = DitherColour(colour, i.vertex.xy);
	}

	return float4(colour, alpha);
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
