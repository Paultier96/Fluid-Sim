using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTrace
	{
		readonly ParticleFluidCausticsView caustics;

		readonly struct CausticsComputePassState
		{
			public readonly ComputeShader compute;
			public readonly int clearKernel;
			public readonly int traceKernel;
			public readonly int resolveKernel;
			public readonly int width;
			public readonly int height;
			public readonly int totalRayBudget;
			public readonly int raysPerPixel;
			public readonly bool renderDirectionalLightField;

			public CausticsComputePassState(ComputeShader compute, int clearKernel, int traceKernel, int resolveKernel, int width, int height, int totalRayBudget, int raysPerPixel, bool renderDirectionalLightField)
			{
				this.compute = compute;
				this.clearKernel = clearKernel;
				this.traceKernel = traceKernel;
				this.resolveKernel = resolveKernel;
				this.width = width;
				this.height = height;
				this.totalRayBudget = totalRayBudget;
				this.raysPerPixel = raysPerPixel;
				this.renderDirectionalLightField = renderDirectionalLightField;
			}
		}

		interface ICausticsComputeParamWriter
		{
			void SetInt(ComputeShader compute, string name, int value);
			void SetFloat(ComputeShader compute, string name, float value);
			void SetVector(ComputeShader compute, string name, Vector4 value);
		}

		readonly struct ComputeCommandBufferParamWriter : ICausticsComputeParamWriter
		{
			readonly IComputeCommandBuffer commandBuffer;

			public ComputeCommandBufferParamWriter(IComputeCommandBuffer commandBuffer)
			{
				this.commandBuffer = commandBuffer;
			}

			public void SetInt(ComputeShader compute, string name, int value) => commandBuffer.SetComputeIntParam(compute, name, value);
			public void SetFloat(ComputeShader compute, string name, float value) => commandBuffer.SetComputeFloatParam(compute, name, value);
			public void SetVector(ComputeShader compute, string name, Vector4 value) => commandBuffer.SetComputeVectorParam(compute, name, value);
		}

		readonly struct ClassicCommandBufferParamWriter : ICausticsComputeParamWriter
		{
			readonly CommandBuffer commandBuffer;

			public ClassicCommandBufferParamWriter(CommandBuffer commandBuffer)
			{
				this.commandBuffer = commandBuffer;
			}

			public void SetInt(ComputeShader compute, string name, int value) => commandBuffer.SetComputeIntParam(compute, name, value);
			public void SetFloat(ComputeShader compute, string name, float value) => commandBuffer.SetComputeFloatParam(compute, name, value);
			public void SetVector(ComputeShader compute, string name, Vector4 value) => commandBuffer.SetComputeVectorParam(compute, name, value);
		}

		public ParticleFluidCausticsTrace(ParticleFluidCausticsView caustics)
		{
			this.caustics = caustics;
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle, TextureHandle lightDirectionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.clearKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.clearKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.clearKernel, state.renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticMotionResult", causticMotionHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "LightDirectionResult", lightDirectionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle combinedHandle, TextureHandle velocityPhase0Handle, TextureHandle velocityPhase1Handle, TextureHandle gradientHandle, TextureHandle gradient2Handle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "CombinedTex", combinedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex0", velocityPhase0Handle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex1", velocityPhase1Handle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap", gradientHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap2", gradient2Handle);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.traceKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.traceKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.traceKernel, state.renderDirectionalLightField);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle, TextureHandle lightDirectionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.resolveKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.resolveKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.resolveKernel, state.renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticMotionResult", causticMotionHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "LightDirectionResult", lightDirectionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, RenderTexture causticResolvedTarget, RenderTexture causticMotionTarget, RenderTexture lightDirectionTarget)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.clearKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.clearKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.clearKernel, state.renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticMotionResult", causticMotionTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "LightDirectionResult", lightDirectionTarget);
			DispatchCompute(targetCommandBuffer, state.compute, state.clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, RenderTexture combinedTarget, RenderTexture velocityPhase0Target, RenderTexture velocityPhase1Target, Texture gradientTarget, Texture gradient2Target)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "CombinedTex", combinedTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex0", velocityPhase0Target);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex1", velocityPhase1Target);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap", gradientTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap2", gradient2Target);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.traceKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.traceKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.traceKernel, state.renderDirectionalLightField);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, RenderTexture causticResolvedTarget, RenderTexture causticMotionTarget, RenderTexture lightDirectionTarget)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			caustics.BindCausticAccumulationTextures(targetCommandBuffer, state.compute, state.resolveKernel);
			caustics.BindCausticMotionTextures(targetCommandBuffer, state.compute, state.resolveKernel);
			caustics.BindLightDirectionTextures(targetCommandBuffer, state.compute, state.resolveKernel, state.renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticMotionResult", causticMotionTarget);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "LightDirectionResult", lightDirectionTarget);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex)
		{
			ComputeCommandBufferParamWriter writer = new ComputeCommandBufferParamWriter(targetCommandBuffer);
			return ApplyComputeCommonParams(context, ref writer, combinedAccumulationTexture, frameIndex);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex)
		{
			ClassicCommandBufferParamWriter writer = new ClassicCommandBufferParamWriter(targetCommandBuffer);
			return ApplyComputeCommonParams(context, ref writer, combinedAccumulationTexture, frameIndex);
		}

		CausticsComputePassState ApplyComputeCommonParams<TWriter>(ParticleFluidLighting2D.FrameContext context, ref TWriter targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex)
			where TWriter : struct, ICausticsComputeParamWriter
		{
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ComputeShader compute = caustics.ComputeShader;
			bool renderDirectionalLightField = caustics.ShouldRenderDirectionalLightField();
			bool renderCausticMotion = caustics.ShouldRenderCaustics() && (caustics.TemporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion || caustics.DebugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = caustics.CausticResolvedTexture.width;
			int height = caustics.CausticResolvedTexture.height;
			Vector3 lightDirection = caustics.PrimaryLight.Direction;
			Vector3 secondaryLightDirection = caustics.SecondaryLight.Direction;
			Vector3 tertiaryLightDirection = caustics.TertiaryLight.Direction;
			bool primaryLightEnabled = ParticleFluidLighting2D.SupportsCausticRaymarch(caustics.PrimaryLight);
			bool secondaryLightEnabled = ParticleFluidLighting2D.SupportsCausticRaymarch(caustics.SecondaryLight);
			bool tertiaryLightEnabled = ParticleFluidLighting2D.SupportsCausticRaymarch(caustics.TertiaryLight);
			caustics.GetCausticRayRange(context, width, height, lightDirection, out float primaryRayStartOffset, out int primaryRangeRayCount);
			caustics.GetCausticRayRange(context, width, height, secondaryLightDirection, out float secondaryRayStartOffset, out int secondaryRangeRayCount);
			caustics.GetCausticRayRange(context, width, height, tertiaryLightDirection, out float tertiaryRayStartOffset, out int tertiaryRangeRayCount);
			caustics.GetCausticPointRaySpan(context, caustics.PrimaryLight, out float primaryPointAngleStart, out float primaryPointAngleRange);
			caustics.GetCausticPointRaySpan(context, caustics.SecondaryLight, out float secondaryPointAngleStart, out float secondaryPointAngleRange);
			caustics.GetCausticPointRaySpan(context, caustics.TertiaryLight, out float tertiaryPointAngleStart, out float tertiaryPointAngleRange);
			if (caustics.PrimaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				primaryRangeRayCount = ParticleFluidLighting2D.GetCausticPointRayCount(caustics.PrimaryLight, currentWorldSize, width, height);
				primaryRayStartOffset = 0f;
			}
			if (caustics.SecondaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				secondaryRangeRayCount = ParticleFluidLighting2D.GetCausticPointRayCount(caustics.SecondaryLight, currentWorldSize, width, height);
				secondaryRayStartOffset = 0f;
			}
			if (caustics.TertiaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				tertiaryRangeRayCount = ParticleFluidLighting2D.GetCausticPointRayCount(caustics.TertiaryLight, currentWorldSize, width, height);
				tertiaryRayStartOffset = 0f;
			}
			int raysPerPixel = Mathf.Max(1, caustics.RaysPerPixel);
			int maxRayCount = Mathf.Max(1, caustics.MaxCausticTraceThreadCount / raysPerPixel);
			float primaryLightWeight = primaryLightEnabled ? ParticleFluidLighting2D.LightSampleWeight(caustics.PrimaryLight) : 0f;
			float secondaryLightWeight = secondaryLightEnabled ? ParticleFluidLighting2D.LightSampleWeight(caustics.SecondaryLight) : 0f;
			float tertiaryLightWeight = tertiaryLightEnabled ? ParticleFluidLighting2D.LightSampleWeight(caustics.TertiaryLight) : 0f;
			int enabledRangeRayCount = Mathf.Max(
				primaryLightWeight > 0f ? primaryRangeRayCount : 0,
				secondaryLightWeight > 0f ? secondaryRangeRayCount : 0,
				tertiaryLightWeight > 0f ? tertiaryRangeRayCount : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0 ? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount)) : 1;
			ParticleFluidLighting2D.GetLightRayShares(primaryLightWeight, secondaryLightWeight, tertiaryLightWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare);
			int secondarySubRaysPerPixel = secondaryShare > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * secondaryShare) : 0;
			secondarySubRaysPerPixel = Mathf.Clamp(secondarySubRaysPerPixel, 0, raysPerPixel);
			int tertiarySubRaysPerPixel = tertiaryShare > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * tertiaryShare) : 0;
			tertiarySubRaysPerPixel = Mathf.Clamp(tertiarySubRaysPerPixel, 0, raysPerPixel - secondarySubRaysPerPixel);
			if (caustics.AnyEnabledCausticPointLight())
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
			int primaryRayBudget = primaryShare > 0f ? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - secondaryRayBudget - tertiaryRayBudget) : 0;
			primaryRayBudget = Mathf.Clamp(primaryRayBudget, 0, primaryRangeRayCount);
			float primaryRaySpacing = primaryRangeRayCount > 1 && primaryRayBudget > 1 ? (primaryRangeRayCount - 1f) / (primaryRayBudget - 1f) : 1f;
			int secondarySpacingRayCount = splitBySubRay ? totalRayBudget : secondaryRayBudget;
			float secondaryRaySpacing = secondaryRangeRayCount > 1 && secondarySpacingRayCount > 1 ? (secondaryRangeRayCount - 1f) / (secondarySpacingRayCount - 1f) : 1f;
			int tertiarySpacingRayCount = splitBySubRay ? totalRayBudget : tertiaryRayBudget;
			float tertiaryRaySpacing = tertiaryRangeRayCount > 1 && tertiarySpacingRayCount > 1 ? (tertiaryRangeRayCount - 1f) / (tertiarySpacingRayCount - 1f) : 1f;

			targetCommandBuffer.SetInt(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetInt(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetInt(compute, "causticWidth", width);
			targetCommandBuffer.SetInt(compute, "causticHeight", height);
			targetCommandBuffer.SetInt(compute, "causticsRayCount", totalRayBudget);
			targetCommandBuffer.SetInt(compute, "causticsPrimaryRayCount", primaryRayBudget);
			targetCommandBuffer.SetInt(compute, "causticsSecondaryRayCount", secondaryRayBudget);
			targetCommandBuffer.SetInt(compute, "causticsTertiaryRayCount", tertiaryRayBudget);
			targetCommandBuffer.SetInt(compute, "causticsPrimarySubRaysPerPixel", primarySubRaysPerPixel);
			targetCommandBuffer.SetInt(compute, "causticsSecondarySubRaysPerPixel", secondarySubRaysPerPixel);
			targetCommandBuffer.SetInt(compute, "causticsTertiarySubRaysPerPixel", tertiarySubRaysPerPixel);
			targetCommandBuffer.SetFloat(compute, "causticsRayStartOffset", primaryRayStartOffset);
			targetCommandBuffer.SetFloat(compute, "causticsRaySpacing", primaryRaySpacing);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryRayStartOffset", secondaryRayStartOffset);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryRaySpacing", secondaryRaySpacing);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryRayStartOffset", tertiaryRayStartOffset);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryRaySpacing", tertiaryRaySpacing);
			targetCommandBuffer.SetInt(compute, "causticsRaySteps", caustics.RaySteps);
			targetCommandBuffer.SetInt(compute, "causticsRayStride", caustics.RayStride);
			targetCommandBuffer.SetInt(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetInt(compute, "causticsColourSampleStride", caustics.ColourSampleStride);
			targetCommandBuffer.SetInt(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetFloat(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetFloat(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetFloat(compute, "causticsStepPixels", caustics.StepPixels);
			targetCommandBuffer.SetFloat(compute, "causticsGlassIndexOfRefraction", caustics.BoundaryMaterial.indexOfRefraction);
			targetCommandBuffer.SetFloat(compute, "causticsGlassReflectance", caustics.BoundaryMaterial.reflectance);
			targetCommandBuffer.SetFloat(compute, "causticsPhase0IndexOfRefraction", caustics.Phase0Material.indexOfRefraction);
			targetCommandBuffer.SetFloat(compute, "causticsPhase1IndexOfRefraction", caustics.Phase1Material.indexOfRefraction);
			targetCommandBuffer.SetFloat(compute, "causticsPhase0Reflectance", caustics.Phase0Material.reflectance);
			targetCommandBuffer.SetFloat(compute, "causticsPhase1Reflectance", caustics.Phase1Material.reflectance);
			targetCommandBuffer.SetFloat(compute, "causticsPhase0Metallic", caustics.Phase0Material.metallic);
			targetCommandBuffer.SetFloat(compute, "causticsPhase1Metallic", caustics.Phase1Material.metallic);
			targetCommandBuffer.SetInt(compute, "causticsStochasticReflection", caustics.StochasticReflection ? 1 : 0);
			targetCommandBuffer.SetFloat(compute, "causticsDispersionStrength", caustics.DispersionStrength);
			targetCommandBuffer.SetFloat(compute, "causticsDispersionRotation", caustics.DispersionRotation);
			targetCommandBuffer.SetFloat(compute, "causticsLightAngularRadius", caustics.LightAngularRadiusDegrees * Mathf.Deg2Rad);
			targetCommandBuffer.SetFloat(compute, "causticsPhase0Absorption", caustics.Phase0Material.absorption);
			targetCommandBuffer.SetFloat(compute, "causticsPhase1Absorption", caustics.Phase1Material.absorption);
			targetCommandBuffer.SetVector(compute, "causticsPhase0AbsorptionTint", caustics.Phase0Material.diffuseLightTint);
			targetCommandBuffer.SetVector(compute, "causticsPhase1AbsorptionTint", caustics.Phase1Material.diffuseLightTint);
			targetCommandBuffer.SetFloat(compute, "causticsPhase0AbsorptionTintBlend", caustics.Phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetFloat(compute, "causticsPhase1AbsorptionTintBlend", caustics.Phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetFloat(compute, "causticsAbsorptionAlbedoBrightnessInfluence", caustics.AbsorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetFloat(compute, "causticsAbsorptionAlbedoSaturationInfluence", caustics.AbsorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetInt(compute, "causticsDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			targetCommandBuffer.SetFloat(compute, "causticsRayBrightness", caustics.RayBrightness);
			targetCommandBuffer.SetFloat(compute, "causticsTemporalJitterPixels", caustics.TemporalJitterPixels);
			targetCommandBuffer.SetFloat(compute, "causticsSurfaceNormalJitterPixels", caustics.SurfaceNormalJitterPixels);
			targetCommandBuffer.SetFloat(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetInt(compute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetInt(compute, "causticsLightType", (int)caustics.PrimaryLight.type);
			targetCommandBuffer.SetFloat(compute, "causticsLightTemperatureKelvin", caustics.PrimaryLight.temperatureKelvin);
			targetCommandBuffer.SetFloat(compute, "causticsLightDispersionScale", ParticleFluidLighting2D.GetSaturationDispersionScale(caustics.PrimaryLight.color));
			targetCommandBuffer.SetVector(compute, "causticsLightPoint", ParticleFluidLighting2D.GetPointLightVector(caustics.PrimaryLight));
			targetCommandBuffer.SetFloat(compute, "causticsLightPointFalloff", caustics.PrimaryLight.pointFalloff);
			targetCommandBuffer.SetFloat(compute, "causticsLightPointAngleStart", primaryPointAngleStart);
			targetCommandBuffer.SetFloat(compute, "causticsLightPointAngleRange", primaryPointAngleRange);
			targetCommandBuffer.SetVector(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetVector(compute, "causticsLightMultiplier", primaryLightWeight > 0f ? ParticleFluidLighting2D.GetCausticMultiplier(caustics.PrimaryLight.EffectiveColor, caustics.PrimaryLight.intensity) : Vector4.zero);
			targetCommandBuffer.SetInt(compute, "causticsSecondaryLightEnabled", secondarySubRaysPerPixel > 0 || secondaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetInt(compute, "causticsSecondaryLightType", (int)caustics.SecondaryLight.type);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryLightTemperatureKelvin", caustics.SecondaryLight.temperatureKelvin);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryLightDispersionScale", ParticleFluidLighting2D.GetSaturationDispersionScale(caustics.SecondaryLight.color));
			targetCommandBuffer.SetVector(compute, "causticsSecondaryLightPoint", ParticleFluidLighting2D.GetPointLightVector(caustics.SecondaryLight));
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryLightPointFalloff", caustics.SecondaryLight.pointFalloff);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryLightPointAngleStart", secondaryPointAngleStart);
			targetCommandBuffer.SetFloat(compute, "causticsSecondaryLightPointAngleRange", secondaryPointAngleRange);
			targetCommandBuffer.SetVector(compute, "causticsSecondaryLightDirection", new Vector4(secondaryLightDirection.x, secondaryLightDirection.y, secondaryLightDirection.z, 0f));
			targetCommandBuffer.SetVector(compute, "causticsSecondaryLightMultiplier", ParticleFluidLighting2D.GetCausticMultiplier(caustics.SecondaryLight.EffectiveColor, caustics.SecondaryLight.intensity));
			targetCommandBuffer.SetInt(compute, "causticsTertiaryLightEnabled", tertiarySubRaysPerPixel > 0 || tertiaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetInt(compute, "causticsTertiaryLightType", (int)caustics.TertiaryLight.type);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryLightTemperatureKelvin", caustics.TertiaryLight.temperatureKelvin);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryLightDispersionScale", ParticleFluidLighting2D.GetSaturationDispersionScale(caustics.TertiaryLight.color));
			targetCommandBuffer.SetVector(compute, "causticsTertiaryLightPoint", ParticleFluidLighting2D.GetPointLightVector(caustics.TertiaryLight));
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryLightPointFalloff", caustics.TertiaryLight.pointFalloff);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryLightPointAngleStart", tertiaryPointAngleStart);
			targetCommandBuffer.SetFloat(compute, "causticsTertiaryLightPointAngleRange", tertiaryPointAngleRange);
			targetCommandBuffer.SetVector(compute, "causticsTertiaryLightDirection", new Vector4(tertiaryLightDirection.x, tertiaryLightDirection.y, tertiaryLightDirection.z, 0f));
			targetCommandBuffer.SetVector(compute, "causticsTertiaryLightMultiplier", ParticleFluidLighting2D.GetCausticMultiplier(caustics.TertiaryLight.EffectiveColor, caustics.TertiaryLight.intensity));
			targetCommandBuffer.SetInt(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && caustics.UseAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetVector(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetVector(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetFloat(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetFloat(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetVector(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetVector(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetVector(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

			return new CausticsComputePassState(compute, clearKernel, traceKernel, resolveKernel, width, height, totalRayBudget, raysPerPixel, renderDirectionalLightField);
		}

		static void DispatchCompute(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		static void DispatchCompute(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchTrace(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), caustics.MaxCausticTraceThreadCount);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)caustics.CausticTraceThreadGroupWidth), 1, 1);
			}
		}

		void DispatchTrace(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), caustics.MaxCausticTraceThreadCount);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)caustics.CausticTraceThreadGroupWidth), 1, 1);
			}
		}
	}
}
