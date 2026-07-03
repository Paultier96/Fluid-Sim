using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTrace
	{
		internal readonly ParticleFluidDirectLight owner;
		internal readonly ParticleFluidCausticsTemporal temporalCaustics;
		ComputeBuffer causticAccumulationBuffer;
		ComputeBuffer causticMotionAccumulationBuffer;
		ComputeBuffer causticLightParamsBuffer;
		ComputeBuffer causticMaterialParamsBuffer;
		internal RenderTexture causticResolvedTexture;
		internal RenderTexture causticMotionTexture;
		internal int causticFrameIndex;
		const int LightSlotCount = 3;
		const int MaterialSlotCount = 3;
		readonly Vector4[] materialParams = new Vector4[MaterialSlotCount * 2];

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

			public CausticsComputePassState(ComputeShader compute, int clearKernel, int traceKernel, int resolveKernel, int width, int height, int totalRayBudget, int raysPerPixel)
			{
				this.compute = compute;
				this.clearKernel = clearKernel;
				this.traceKernel = traceKernel;
				this.resolveKernel = resolveKernel;
				this.width = width;
				this.height = height;
				this.totalRayBudget = totalRayBudget;
				this.raysPerPixel = raysPerPixel;
			}
		}

		public ParticleFluidCausticsTrace(ParticleFluidDirectLight owner)
		{
			this.owner = owner;
			temporalCaustics = new ParticleFluidCausticsTemporal(this);
		}

		internal void EnsureResources(int width, int height, bool renderCaustics)
		{
			if (renderCaustics)
			{
				int causticAccumulationCount = width * height * 4;
				ComputeHelper.CreateStructuredBuffer<uint>(ref causticAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<int>(ref causticMotionAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticLightParamsBuffer, 18);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticMaterialParamsBuffer, MaterialSlotCount * 2);
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticMotionTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion");
				return;
			}

			ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer, causticLightParamsBuffer, causticMaterialParamsBuffer);
			ComputeHelper.Release(causticResolvedTexture, causticMotionTexture);
		}

		internal void Release()
		{
			EnsureResources(0, 0, false);
			temporalCaustics.Release();
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.clearKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticMotionResult", causticMotionHandle);
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
			BindComputeBuffers(targetCommandBuffer, state.compute, state.traceKernel);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.resolveKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticMotionResult", causticMotionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex)
		{
			ParticleDisplay2D display = context.display;
			ParticleFluidLighting2D lightingOwner = owner.Owner;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ComputeShader compute = owner.computeShader;
			bool renderCausticMotion = owner.lightingMode == ParticleFluidLighting2D.LightingMode.Caustics && (owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion || lightingOwner.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = lightingOwner.PhaseMaterials;
			int raysPerPixel = Mathf.Max(1, owner.raysPerPixel);
			int totalRayBudget = lightingOwner.lightManager.UploadCausticsLightGpuData(
				causticLightParamsBuffer,
				context,
				width,
				height,
				currentWorldSize,
				raysPerPixel);

			targetCommandBuffer.SetComputeVectorParam(compute, "combinedResolution", new Vector2(combinedAccumulationTexture.width,combinedAccumulationTexture.height));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticSize", new Vector2(width,height));
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", totalRayBudget);
			for (int i = 0; i < MaterialSlotCount; i++)
			{
				ParticleFluidLighting2D.PhaseMaterialSettings material = materials[i];
				int offset = i * 2;
				materialParams[offset] = new Vector4(material.indexOfRefraction, material.reflectance, material.metallic, material.absorption);
				Color diffuseTint = material.diffuseLightTint;
				materialParams[offset + 1] = new Vector4(diffuseTint.r, diffuseTint.g, diffuseTint.b, material.absorptionDiffuseTintBlend);
			}
			causticMaterialParamsBuffer.SetData(materialParams);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", owner.extraRayTravelSteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", owner.rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", owner.colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", owner.stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", owner.dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionRotation", owner.dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoBrightnessInfluence", lightingOwner.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoSaturationInfluence", lightingOwner.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", owner.rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", owner.temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceNormalJitterPixels", owner.surfaceNormalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.analyticBoundary.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.analyticBoundary.ellipseBoundsCenter.x, display.sim.analyticBoundary.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.analyticBoundary.ellipseBoundsSize.x, display.sim.analyticBoundary.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.analyticBoundary.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

			return new CausticsComputePassState(compute, clearKernel, traceKernel, resolveKernel, width, height, totalRayBudget, raysPerPixel);
		}

		void BindComputeBuffers(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulationBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticMotionAccum", causticMotionAccumulationBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsLights", causticLightParamsBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsMaterials", causticMaterialParamsBuffer);
		}

		static void DispatchCompute(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchTrace(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), ParticleFluidLighting2D.MaxCausticTraceThreads);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)ParticleFluidLighting2D.CausticTraceThreadGroupSize), 1, 1);
			}
		}
		
		public Texture GetSharpCausticsTexture()
		{
			if (owner.lightingMode != ParticleFluidLighting2D.LightingMode.Caustics)
			{
				return Texture2D.blackTexture;
			}

			return owner.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}
	}
}
