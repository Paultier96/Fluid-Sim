using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleFluidLighting2D
	{
		public void ClearCausticHistory()
		{
			clearCausticHistory = true;
			hasPreviousCausticCamera = false;
			causticTemporalFrameCount = 0;
		}
		
		public void BindCausticAccumulationTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulationBuffer);
		}

		public void BindCausticMotionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticMotionAccum", causticMotionAccumulationBuffer);
		}

		public void BindLightDirectionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, bool renderDirectionalLightField)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "LightDirectionAccum", renderDirectionalLightField ? lightDirectionAccumulationBuffer : lightDirectionAccumulationFallbackBuffer);
		}
		
		float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface)
		{
			return Mathf.Max(surface.renderTextureScale * textureScale, 0.0001f);
		}

		static Vector4 GetCausticMultiplier(Color color, float intensity)
		{
			float clampedIntensity = Mathf.Max(intensity, 0f);
			return new Vector4(
				color.r * clampedIntensity,
				color.g * clampedIntensity,
				color.b * clampedIntensity,
				0f
			);
		}
		
		public void BuildCaustics(FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;

			targetCommandBuffer.BeginSample("Metaballs/Caustics");
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ComputeShader compute = computeShader;
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField();
			bool renderCausticMotion = ShouldRenderCaustics() && (temporalMotionSource == TemporalMotionSource.CausticMotion || debugMode == LightingDebugVisualization.CausticMotion);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			Vector3 lightDirection = primaryLight.Direction;
			Vector3 secondaryLightDirection = secondaryLight.Direction;
			Vector3 tertiaryLightDirection = tertiaryLight.Direction;
			bool primaryLightEnabled = SupportsCausticRaymarch(primaryLight);
			bool secondaryLightEnabled = SupportsCausticRaymarch(secondaryLight);
			bool tertiaryLightEnabled = SupportsCausticRaymarch(tertiaryLight);
			GetCausticRayRange(context, width, height, lightDirection, out float primaryRayStartOffset, out int primaryRangeRayCount);
			GetCausticRayRange(context, width, height, secondaryLightDirection, out float secondaryRayStartOffset, out int secondaryRangeRayCount);
			GetCausticRayRange(context, width, height, tertiaryLightDirection, out float tertiaryRayStartOffset, out int tertiaryRangeRayCount);
			GetCausticPointRaySpan(context, this.primaryLight, out float primaryPointAngleStart, out float primaryPointAngleRange);
			GetCausticPointRaySpan(context, secondaryLight, out float secondaryPointAngleStart, out float secondaryPointAngleRange);
			GetCausticPointRaySpan(context, tertiaryLight, out float tertiaryPointAngleStart, out float tertiaryPointAngleRange);
			if (primaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				primaryRangeRayCount = GetCausticPointRayCount(primaryLight, currentWorldSize, width, height);
				primaryRayStartOffset = 0f;
			}
			if (secondaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				secondaryRangeRayCount = GetCausticPointRayCount(secondaryLight, currentWorldSize, width, height);
				secondaryRayStartOffset = 0f;
			}
			if (tertiaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				tertiaryRangeRayCount = GetCausticPointRayCount(tertiaryLight, currentWorldSize, width, height);
				tertiaryRayStartOffset = 0f;
			}
			int raysPerPixel = Mathf.Max(1, this.raysPerPixel);
			int maxRayCount = Mathf.Max(1, MaxCausticTraceThreads / raysPerPixel);
			float primaryLightWeight = primaryLightEnabled ? LightSampleWeight(primaryLight) : 0f;
			float secondaryLightWeight = secondaryLightEnabled ? LightSampleWeight(secondaryLight) : 0f;
			float tertiaryLightWeight = tertiaryLightEnabled ? LightSampleWeight(tertiaryLight) : 0f;
			int enabledRangeRayCount = Mathf.Max(
				primaryLightWeight > 0f ? primaryRangeRayCount : 0,
				secondaryLightWeight > 0f ? secondaryRangeRayCount : 0,
				tertiaryLightWeight > 0f ? tertiaryRangeRayCount : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0
				? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount))
				: 1;
			GetLightRayShares(primaryLightWeight, secondaryLightWeight, tertiaryLightWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare);
			int secondarySubRaysPerPixel = secondaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * secondaryShare)
				: 0;
			secondarySubRaysPerPixel = Mathf.Clamp(secondarySubRaysPerPixel, 0, raysPerPixel);
			int tertiarySubRaysPerPixel = tertiaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * tertiaryShare)
				: 0;
			tertiarySubRaysPerPixel = Mathf.Clamp(tertiarySubRaysPerPixel, 0, raysPerPixel - secondarySubRaysPerPixel);
			if (AnyEnabledCausticPointLight())
			{
				secondarySubRaysPerPixel = 0;
				tertiarySubRaysPerPixel = 0;
			}
			int primarySubRaysPerPixel = primaryShare > 0f ? Mathf.Max(0, raysPerPixel - secondarySubRaysPerPixel - tertiarySubRaysPerPixel) : 0;
			bool splitBySubRay = secondarySubRaysPerPixel > 0 || tertiarySubRaysPerPixel > 0;
			int secondaryRayBudget = secondaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * secondaryShare) : 0;
			secondaryRayBudget = Mathf.Clamp(secondaryRayBudget, 0, Mathf.Min(totalRayBudget, secondaryRangeRayCount));
			int tertiaryRayBudget = tertiaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * tertiaryShare) : 0;
			tertiaryRayBudget = Mathf.Clamp(tertiaryRayBudget, 0, Mathf.Min(totalRayBudget - secondaryRayBudget, tertiaryRangeRayCount));
			int primaryRayBudget = primaryShare > 0f
				? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - secondaryRayBudget - tertiaryRayBudget)
				: 0;
			primaryRayBudget = Mathf.Clamp(primaryRayBudget, 0, primaryRangeRayCount);
			float primaryRaySpacing = primaryRangeRayCount > 1 && primaryRayBudget > 1
				? (primaryRangeRayCount - 1f) / (primaryRayBudget - 1f)
				: 1f;
			int secondarySpacingRayCount = splitBySubRay ? totalRayBudget : secondaryRayBudget;
			float secondaryRaySpacing = secondaryRangeRayCount > 1 && secondarySpacingRayCount > 1
				? (secondaryRangeRayCount - 1f) / (secondarySpacingRayCount - 1f)
				: 1f;
			int tertiarySpacingRayCount = splitBySubRay ? totalRayBudget : tertiaryRayBudget;
			float tertiaryRaySpacing = tertiaryRangeRayCount > 1 && tertiarySpacingRayCount > 1
				? (tertiaryRangeRayCount - 1f) / (tertiarySpacingRayCount - 1f)
				: 1f;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", totalRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimaryRayCount", primaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryRayCount", secondaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryRayCount", tertiaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimarySubRaysPerPixel", primarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondarySubRaysPerPixel", secondarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiarySubRaysPerPixel", tertiarySubRaysPerPixel);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayStartOffset", primaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRaySpacing", primaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRayStartOffset", secondaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRaySpacing", secondaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRayStartOffset", tertiaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRaySpacing", tertiaryRaySpacing);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", raySteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsStepPixels", stepPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassIndexOfRefraction", boundaryMaterial.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassReflectance", boundaryMaterial.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0IndexOfRefraction", phase0Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1IndexOfRefraction", phase1Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Reflectance", phase0Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Reflectance", phase1Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Metallic", phase0Material.metallic);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Metallic", phase1Material.metallic);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionRotation", dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightAngularRadius", lightAngularRadiusDegrees * Mathf.Deg2Rad);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Absorption", phase0Material.absorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Absorption", phase1Material.absorption);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase0AbsorptionTint", phase0Material.diffuseLightTint);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase1AbsorptionTint", phase1Material.diffuseLightTint);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0AbsorptionTintBlend", phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1AbsorptionTintBlend", phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoBrightnessInfluence", absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoSaturationInfluence", absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceNormalJitterPixels", surfaceNormalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", causticFrameIndex++);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsLightType", (int)primaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightTemperatureKelvin", primaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightDispersionScale", GetSaturationDispersionScale(primaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightPoint", GetPointLightVector(primaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointFalloff", primaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleStart", primaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleRange", primaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightMultiplier", primaryLightWeight > 0f ? GetCausticMultiplier(primaryLight.EffectiveColor, primaryLight.intensity) : Vector4.zero);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightEnabled", secondarySubRaysPerPixel > 0 || secondaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightType", (int)secondaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightTemperatureKelvin", secondaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightDispersionScale", GetSaturationDispersionScale(secondaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightPoint", GetPointLightVector(secondaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointFalloff", secondaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleStart", secondaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleRange", secondaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightDirection", new Vector4(secondaryLightDirection.x, secondaryLightDirection.y, secondaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightMultiplier", GetCausticMultiplier(secondaryLight.EffectiveColor, secondaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightEnabled", tertiarySubRaysPerPixel > 0 || tertiaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightType", (int)tertiaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightTemperatureKelvin", tertiaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightDispersionScale", GetSaturationDispersionScale(tertiaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightPoint", GetPointLightVector(tertiaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointFalloff", tertiaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleStart", tertiaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleRange", tertiaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightDirection", new Vector4(tertiaryLightDirection.x, tertiaryLightDirection.y, tertiaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightMultiplier", GetCausticMultiplier(tertiaryLight.EffectiveColor, tertiaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && useAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

			BindCausticAccumulationTextures(targetCommandBuffer, compute, clearKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, clearKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, clearKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, clearKernel, width, height);

			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex0", velocityPhase0AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex1", velocityPhase1AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap2", display.gradientTexture2);
			BindCausticAccumulationTextures(targetCommandBuffer, compute, traceKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, traceKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, traceKernel, renderDirectionalLightField);
			DispatchCausticTrace(targetCommandBuffer, compute, traceKernel, totalRayBudget, raysPerPixel);

			BindCausticAccumulationTextures(targetCommandBuffer, compute, resolveKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, resolveKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, resolveKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, resolveKernel, width, height);

			float rayTextureBlurScale = GetRayTextureBlurScale(surface);
			float causticBlurRadius = blur * rayTextureBlurScale;
			float directionalLightFieldBlurRadius = directionalLightFieldBlur * rayTextureBlurScale;
			float temporalMotionBlurRadius = temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				causticBlurMaterial.SetFloat("blurRadius", causticBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(causticResolvedTexture, causticBlurTexture, causticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(causticBlurTexture, causticResolvedTexture, causticBlurMaterial);
			}
			if (renderDirectionalLightField && directionalLightFieldBlurRadius > 0.001f)
			{
				lightDirectionBlurMaterial.SetFloat("blurRadius", directionalLightFieldBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(lightDirectionBlurTexture, lightDirectionTexture, lightDirectionBlurMaterial);
			}
			RenderTexture temporalMotionTexture = causticMotionTexture;
			bool useCausticMotion = temporalMaterial != null && temporalMotionSource == TemporalMotionSource.CausticMotion;
			if (useCausticMotion && temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, temporalMotionDilationIterations);
				float motionDilationRadius = temporalMotionDilationRadius * GetRayTextureBlurScale(surface) / dilationIterations;
				temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, temporalMaterial, 1);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == causticMotionDilatedTexture ? causticMotionDilationScratchTexture : causticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && causticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == causticMotionDilationScratchTexture
					? causticMotionDilatedTexture
					: causticMotionDilationScratchTexture;
				causticMotionBlurMaterial.SetFloat("blurRadius", temporalMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(temporalMotionTexture, motionBlurScratch, causticMotionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(motionBlurScratch, temporalMotionTexture, causticMotionBlurMaterial);
			}
			//debugMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : causticMotionTexture);

			if (denoisingEnabled && temporalMaterial != null)
			{
				if (clearCausticHistory || !hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionHistoryTexture);
					}
					clearCausticHistory = false;
					causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						temporalMaterial.SetTexture("CausticHistoryTex", lightDirectionHistoryTexture);
						temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture, temporalMaterial, 0);
						targetCommandBuffer.Blit(lightDirectionTemporalTexture, lightDirectionHistoryTexture);
					}
					causticTemporalFrameCount = nextFrameCount;
				}

				previousCausticWorldCenter = currentWorldCenter;
				previousCausticWorldSize = currentWorldSize;
				hasPreviousCausticCamera = true;
			}
			else
			{
				hasPreviousCausticCamera = false;
				causticTemporalFrameCount = 0;
			}

			if (ShouldRenderRadianceCascadeLight())
			{
				RenderRadianceCascadeLight(context, targetCommandBuffer, surface, denoisingEnabled ? causticTemporalTexture : causticResolvedTexture, combinedAccumulationTexture);
			}
			else if (ShouldRenderPhaseDiffuseLight())
			{
				RenderPhaseDiffuseLight(context, targetCommandBuffer, surface, denoisingEnabled ? causticTemporalTexture : causticResolvedTexture, combinedAccumulationTexture);
			}
			targetCommandBuffer.EndSample("Metaballs/Caustics");
		}
		
		void RenderPhaseDiffuseLight(FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			ComputeShader compute = phaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int gaussianHorizontalKernel = compute.FindKernel("MaskedGaussianHorizontal");
			int gaussianVerticalKernel = compute.FindKernel("MaskedGaussianVertical");
			int width = softLightTexture0.width;
			int height = softLightTexture0.height;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", softLightTexture0);
			DispatchCaustics(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = softLightTexture0;
			RenderTexture target = softLightTexture1;
			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianHorizontalKernel, width, height);

			RenderTexture previousSource = source;
			source = target;
			target = previousSource;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianVerticalKernel, width, height);

			previousSource = source;
			source = target;
			target = previousSource;

			//debugMaterial?.SetTexture("SoftLightTex", source);
			SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
		}
		
		void SetPhaseDiffuseCommonParams(FrameContext context, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int gaussianHorizontalKernel, int gaussianVerticalKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, RenderTexture combinedAccumulationTexture )
		{
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", sharpCaustics.width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", sharpCaustics.height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBlendWidth", surface.phaseBlendWidth);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", phaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", 1f);
			float gaussianRadiusScale = GetRayTextureBlurScale(surface) * Mathf.Max(phaseDiffuseLightTextureScale, 0.0001f);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius0", phase0Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius1", phase1Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", MetaballRenderer2D.GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}
		
		void DispatchCaustics(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchCausticTrace(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), MaxCausticTraceThreads);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)CausticTraceThreadGroupSize), 1, 1);
			}
		}
		
		void GetCausticPointRaySpan(FrameContext context, DirectionalLightSettings light, out float angleStart, out float angleRange)
		{
			ParticleDisplay2D display = context.display;
			angleStart = 0f;
			angleRange = TwoPi;
			if (display == null
			    || light == null
			    || light.type != DirectionalLightSettings.LightType.Point
			    || !useAnalyticBoundary
			    || !display.sim.useEllipticalBounds)
			{
				return;
			}

			Vector2 point = light.pointPosition;
			float expansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			float cutY = display.sim.obstacleY - expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f || IsInsideAnalyticBoundary(point, center, radii, cutY))
			{
				return;
			}

			int angleCount = 0;
			for (int i = 0; i < PointLightBoundaryEllipseSamples; i++)
			{
				float t = i / (float)PointLightBoundaryEllipseSamples * TwoPi;
				Vector2 boundaryPoint = center + new Vector2(Mathf.Cos(t) * radii.x, Mathf.Sin(t) * radii.y);
				if (boundaryPoint.y >= cutY)
				{
					AddPointLightBoundaryAngle(point, boundaryPoint, ref angleCount);
				}
			}

			float cutRelY = cutY - center.y;
			if (Mathf.Abs(cutRelY) <= radii.y)
			{
				float cutHalfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY / (radii.y * radii.y)));
				for (int i = 0; i < PointLightBoundaryCutSamples; i++)
				{
					float t = PointLightBoundaryCutSamples > 1 ? i / (float)(PointLightBoundaryCutSamples - 1) : 0.5f;
					AddPointLightBoundaryAngle(point, new Vector2(center.x + Mathf.Lerp(-cutHalfWidth, cutHalfWidth, t), cutY), ref angleCount);
				}
			}

			if (angleCount < 2)
			{
				return;
			}

			System.Array.Sort(pointLightBoundaryAngles, 0, angleCount);
			float largestGap = -1f;
			int largestGapIndex = 0;
			for (int i = 0; i < angleCount; i++)
			{
				float current = pointLightBoundaryAngles[i];
				float next = i == angleCount - 1 ? pointLightBoundaryAngles[0] + TwoPi : pointLightBoundaryAngles[i + 1];
				float gap = next - current;
				if (gap > largestGap)
				{
					largestGap = gap;
					largestGapIndex = i;
				}
			}

			float padding = 2f * Mathf.Deg2Rad;
			angleStart = Mathf.Repeat(pointLightBoundaryAngles[(largestGapIndex + 1) % angleCount] - padding, TwoPi);
			angleRange = Mathf.Clamp(TwoPi - largestGap + padding * 2f, 0.0001f, TwoPi);
		}

		void AddPointLightBoundaryAngle(Vector2 lightPoint, Vector2 boundaryPoint, ref int angleCount)
		{
			if (angleCount >= pointLightBoundaryAngles.Length)
			{
				return;
			}

			Vector2 delta = boundaryPoint - lightPoint;
			if (delta.sqrMagnitude <= 0.000001f)
			{
				return;
			}

			pointLightBoundaryAngles[angleCount++] = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x), TwoPi);
		}

		static bool IsInsideAnalyticBoundary(Vector2 point, Vector2 center, Vector2 radii, float cutY)
		{
			if (point.y < cutY)
			{
				return false;
			}

			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			return ellipseValue <= 1f;
		}
		
		void ApplyTemporalSettings(FrameContext context, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			if (temporalMaterial == null)
			{
				return;
			}
			ParticleDisplay2D display = context.display;
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			temporalMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture);
			temporalMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			temporalMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			temporalMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			temporalMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, this));
			temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : temporalHistoryWeight);
			temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", temporalHistoryClampStrength);
			temporalMaterial.SetFloat("causticTemporalClampRejection", temporalClampRejection);
			temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", temporalRejectedSpatialFilter);
			temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
		}

		public float GetCausticDebugExposure()
		{
			float exposure = SupportsCausticRaymarch(primaryLight) ? Luminance(primaryLight.EffectiveColor) * Mathf.Max(primaryLight.intensity, 0f) : 0f;
			if (SupportsCausticRaymarch(secondaryLight))
			{
				exposure += Luminance(secondaryLight.EffectiveColor) * Mathf.Max(secondaryLight.intensity, 0f);
			}
			if (SupportsCausticRaymarch(tertiaryLight))
			{
				exposure += Luminance(tertiaryLight.EffectiveColor) * Mathf.Max(tertiaryLight.intensity, 0f);
			}
			return Mathf.Max(exposure, 0.0001f);
		}

		static float Luminance(Color colour)
		{
			return colour.r * 0.2126f + colour.g * 0.7152f + colour.b * 0.0722f;
		}

		static float GetSaturationDispersionScale(Color color)
		{
			float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
			float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}

		static void GetLightRayShares(float primaryWeight, float secondaryWeight, float tertiaryWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare)
		{
			float totalWeight = primaryWeight + secondaryWeight + tertiaryWeight;
			if (totalWeight > 0.0001f)
			{
				primaryShare = primaryWeight / totalWeight;
				secondaryShare = secondaryWeight / totalWeight;
				tertiaryShare = tertiaryWeight / totalWeight;
				return;
			}

			primaryShare = 0f;
			secondaryShare = 0f;
			tertiaryShare = 0f;
		}

		static float LightSampleWeight(DirectionalLightSettings light)
		{
			if (!SupportsCausticRaymarch(light))
			{
				return 0f;
			}
			return Mathf.Max(0f, Luminance(light.EffectiveColor) * light.intensity * light.sampleBias);
		}

		static bool SupportsCausticRaymarch(DirectionalLightSettings light)
		{
			return light != null
			       && light.enabled
			       && light.intensity > 0f;
		}

		bool AnyEnabledCausticPointLight()
		{
			return IsWeightedPointLight(primaryLight)
			       || IsWeightedPointLight(secondaryLight)
			       || IsWeightedPointLight(tertiaryLight);
		}

		static bool IsWeightedPointLight(DirectionalLightSettings light)
		{
			return light != null
			       && light.type == DirectionalLightSettings.LightType.Point
			       && LightSampleWeight(light) > 0f;
		}

		void GetCausticRayRange(FrameContext context, int width, int height, Vector3 lightDirection, out float startOffset, out int rayCount)
		{
			ParticleDisplay2D display = context.display;
			Vector2 worldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 worldSize = context.causticRenderRegion.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			Vector2 lightXY = new Vector2(-lightDirection.x, -lightDirection.y);
			Vector2 rayDir = lightXY.sqrMagnitude > 0.0001f ? lightXY.normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(width * width + height * height);
			float screenMinOffset = -fullSpan * 0.5f;
			float screenMaxOffset = fullSpan * 0.5f;
			startOffset = screenMinOffset;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!display.sim.useEllipticalBounds || !useAnalyticBoundary)
			{
				return;
			}

			float minOffset = float.PositiveInfinity;
			float maxOffset = float.NegativeInfinity;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * analyticBoundaryExpansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return;
			}

			for (int i = 0; i < 128; i++)
			{
				float angle = i * Mathf.PI * 2f / 128f;
				Vector2 world = display.sim.ellipseBoundsCenter + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
				if (world.y >= display.sim.obstacleY - analyticBoundaryExpansion)
				{
					IncludeCausticLaunchPoint(world, worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				}
			}

			float expandedObstacleY = display.sim.obstacleY - analyticBoundaryExpansion;
			float cutRelY = (expandedObstacleY - display.sim.ellipseBoundsCenter.y) / radii.y;
			if (Mathf.Abs(cutRelY) <= 1f)
			{
				float cutX = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY));
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x - cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x + cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
			}

			if (float.IsNaN(minOffset) || float.IsInfinity(minOffset) || float.IsNaN(maxOffset) || float.IsInfinity(maxOffset))
			{
				return;
			}

			float angularPadding = Mathf.Sin(lightAngularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(stepPixels * 4f + angularPadding, 2f);
			float clippedMinOffset = Mathf.Max(minOffset - padding, screenMinOffset);
			float clippedMaxOffset = Mathf.Min(maxOffset + padding, screenMaxOffset);
			if (clippedMaxOffset <= clippedMinOffset)
			{
				rayCount = 0;
				return;
			}

			startOffset = Mathf.Floor(clippedMinOffset);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(clippedMaxOffset - startOffset));
		}

		void IncludeCausticLaunchPoint(Vector2 world, Vector2 worldCenter, Vector2 worldSize, int width, int height, Vector2 tangent, ref float minOffset, ref float maxOffset)
		{
			Vector2 uv = new Vector2(
				(world.x - worldCenter.x) / Mathf.Max(worldSize.x, 0.0001f) + 0.5f,
				(world.y - worldCenter.y) / Mathf.Max(worldSize.y, 0.0001f) + 0.5f
			);
			Vector2 pixel = new Vector2(uv.x * width, uv.y * height);
			Vector2 centredPixel = pixel - new Vector2(width, height) * 0.5f;
			float offset = Vector2.Dot(centredPixel, tangent);
			minOffset = Mathf.Min(minOffset, offset);
			maxOffset = Mathf.Max(maxOffset, offset);
		}
		
		static int GetCausticPointRayCount(DirectionalLightSettings light, Vector2 worldSize, int width, int height)
		{
			if (light == null)
			{
				return 0;
			}

			float pixelsPerWorldUnit = Mathf.Max(
				width / Mathf.Max(worldSize.x, 0.0001f),
				height / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(light.pointRange, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2 * radiusPixels));
		}
		
		void RenderRadianceCascadeLight(FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Radiance Cascades");
			int width = softLightTexture0.width;
			int height = softLightTexture0.height;
			int cascadeCount = Mathf.Clamp(radianceCascadeCount, 1, 6);
			RenderTexture source = softLightTexture0;
			RenderTexture target = softLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeCommonParams(context, targetCommandBuffer, surface, sharpCaustics, width, height, combinedAccumulationTexture);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, radianceCascadeMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			//debugMaterial?.SetTexture("SoftLightTex", source);
			SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Radiance Cascades");
		}
		
		void SetRadianceCascadeCommonParams(FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, int width, int height, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D display = context.display;
			Camera cam = context.cam;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics);
			targetCommandBuffer.SetGlobalTexture("CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("_Aspect", new Vector4(1f, width / Mathf.Max(height, 1f), 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(radianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(radianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", radianceCascadeIntensity);
			targetCommandBuffer.SetGlobalInt("radianceCascadeAbsorption", radianceCascadeAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetGlobalFloat("edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetGlobalFloat("phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthA", phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthB", phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("lightIntensity", 1f);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", phase0Material.absorption);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1Absorption", phase1Material.absorption);
			targetCommandBuffer.SetGlobalVector("causticsPhase0AbsorptionTint", phase0Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalVector("causticsPhase1AbsorptionTint", phase1Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0AbsorptionTintBlend", phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1AbsorptionTintBlend", phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetGlobalInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetGlobalFloat("analyticBoundaryExpansion", MetaballRenderer2D.GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}
		
		public void Release()
		{
			if (lightingMaterial != null)
			{
				DestroyImmediate(lightingMaterial);
				lightingMaterial = null;
			}

			if (colorBleedMaterial != null)
			{
				DestroyImmediate(colorBleedMaterial);
				colorBleedMaterial = null;
			}
			if (radianceCascadeMaterial != null)
			{
				DestroyImmediate(radianceCascadeMaterial);
				radianceCascadeMaterial = null;
			}
			if (temporalMaterial != null)
			{
				DestroyImmediate(temporalMaterial);
				temporalMaterial = null;
			}
			if (causticBlurMaterial != null)
			{
				DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}

			if (causticMotionBlurMaterial != null)
			{
				DestroyImmediate(causticMotionBlurMaterial);
				causticMotionBlurMaterial = null;
			}

			if (lightDirectionBlurMaterial != null)
			{
				DestroyImmediate(lightDirectionBlurMaterial);
				lightDirectionBlurMaterial = null;
			}
			
			ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer);
			causticAccumulationBuffer = null;
			causticMotionAccumulationBuffer = null;
			ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
			ReleaseOptionalCausticFallbackTextures();
			ReleaseLightDirectionTextures();
			ReleasePhaseDiffuseLightTextures();
		}
		
		void ReleasePhaseDiffuseLightTextures()
		{
			ComputeHelper.Release(softLightTexture0, softLightTexture1);
			softLightTexture0 = null;
			softLightTexture1 = null;
		}

		void ReleaseLightDirectionTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationBuffer);
			lightDirectionAccumulationBuffer = null;
			ComputeHelper.Release(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionHistoryTexture, lightDirectionTemporalTexture);
			lightDirectionTexture = null;
			lightDirectionBlurTexture = null;
			lightDirectionHistoryTexture = null;
			lightDirectionTemporalTexture = null;
		}

		void ReleaseOptionalCausticFallbackTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationFallbackBuffer);
			lightDirectionAccumulationFallbackBuffer = null;
			ComputeHelper.Release(lightDirectionResultFallbackTexture);
			lightDirectionResultFallbackTexture = null;
		}
	}
}
