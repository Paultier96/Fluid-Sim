using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidDirectLight : MonoBehaviour
	{
		public enum LightingMode
		{
			Off,
			Shadows,
			Caustics
		}
		
		[System.Serializable]
		public sealed class CausticsTraceSettings
		{
			[Header("Refraction")]
			public bool stochasticReflection = true;
			[Min(0f)] public float dispersionStrength = 0f;
			[Range(0f, 1f)] public float dispersionRotation = 1f;

			[Header("Rays")]
			[Range(8, 192)] public int extraRayTravelSteps = 64;
			[Min(0f)] public float rayBrightness = 1f;
			[Min(1)] public int rayStride = 1;
			[Range(1, 128)] public int raysPerPixel = 1;
			[Min(1)] public int colourSampleStride = 8;
			[Min(0f)] public float blur = 1.5f;
			[Range(0f, 1f)] public float temporalJitterPixels = 0f;
			[Min(0f)] public float surfaceNormalJitterPixels = 0f;
		}

		[System.Serializable]
		public sealed class CausticsTemporalSettings
		{
			public bool denoisingEnabled = true;
			[Range(0f, 0.99f)] public float temporalHistoryWeight = 0.95f;
			[Range(0f, 1f)] public float temporalHistoryClampStrength = 0.6f;
			[Min(0f)] public float temporalClampRejection = 0.5f;
			[Range(0f, 1f)] public float temporalRejectedSpatialFilter = 1f;
			public ParticleFluidCausticsTemporal.TemporalMotionSource temporalMotionSource = ParticleFluidCausticsTemporal.TemporalMotionSource.Static;
			[Min(0)] public float motionBlurRadius = 6;
			[Min(0f)] public float temporalMotionBlur = 1.5f;
			[Min(0f)] public float temporalMotionDilationRadius = 12f;
			[Range(1, 8)] public int temporalMotionDilationIterations = 3;
			public bool projectedShadowHistoryRejection = true;
		}

		[System.Serializable]
		public sealed class ProjectedShadowSettings
		{
			public float offset = 0f;
			[Min(0f)] public float expansion = 0f;
			[Min(1)] public int mapBins = 2048;
		}

		[Header("Shaders")]
		public Shader blurShader;
		public Shader temporalShader;
		public ComputeShader causticsCompute;
		public ComputeShader projectedShadowCompute;
		
		[Header("Direct Light")]
		public LightingMode lightingMode = LightingMode.Caustics;
		[Range(0.125f, 1f)] public float textureScale = 0.5f;

		[Header("Ray marched Lighting")]
		public CausticsTraceSettings traceSettings = new();

		[Header("Ray marched Lighting - Temporal Denoising")]
		public CausticsTemporalSettings temporalSettings = new();

		[Header("Projected Shadow")]
		public ProjectedShadowSettings projectedShadowSettings = new();

		internal ParticleFluidLighting2D particleFluidLighting2D;
		internal ParticleFluidProjectedShadow projectedShadow;

		void Awake()
		{
			projectedShadow = new ParticleFluidProjectedShadow();
			temporalCaustics = new ParticleFluidCausticsTemporal(this);
		    particleFluidLighting2D = GetComponent<ParticleFluidLighting2D>();
		}

		internal void EnsureMaterials()
		{
			temporalCaustics.EnsureMaterials();
		}

		internal void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityTexture)
		{
			temporalCaustics.ApplyTemporalSettings(context, velocityTexture);
		}

		internal Texture GetCurrentDirectLightTexture()
		{
			if (lightingMode != LightingMode.Caustics)
			{
				return Texture2D.blackTexture;
			}

			return temporalSettings.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}

		internal void ApplyInactiveProjectedShadow(Material material)
		{
			projectedShadow.ApplyToMaterial(material, false, Vector2.zero, projectedShadowSettings);
		}

		internal void EnsureResources(Vector2Int causticSize, Bounds domainRegion)
		{
			bool useProjectedShadowMap = projectedShadow.ShouldRender(this) || (temporalSettings.denoisingEnabled && (temporalSettings.projectedShadowHistoryRejection || temporalSettings.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.ProjectedShadow));

			void EnsureProjectedShadowResources()
			{
				projectedShadow.EnsureResources(useProjectedShadowMap, projectedShadowSettings.mapBins);
			}

			int causticWidth = causticSize.x;
			int causticHeight = causticSize.y;
			if (lightingMode == LightingMode.Caustics)
			{
				EnsureResources(causticWidth, causticHeight, true);
				if (temporalSettings.denoisingEnabled)
				{
					EnsureProjectedShadowResources();
					temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, true, useProjectedShadowMap);
				}
				else
				{
					temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, false, false);
					projectedShadow.Release();
				}
			}
			else
			{
				Release();
				EnsureProjectedShadowResources();
			}
		}

		internal void Release()
		{
			projectedShadow.Release();
			EnsureResources(0, 0, false);
			temporalCaustics.Release();
		}
		
		internal ParticleFluidCausticsTemporal temporalCaustics;
		ComputeBuffer causticAccumulationBuffer;
		ComputeBuffer causticMotionAccumulationBuffer;
		ComputeBuffer causticLightParamsBuffer;
		ComputeBuffer causticMaterialParamsBuffer;
		internal RenderTexture causticResolvedTexture;
		internal RenderTexture causticMotionTexture;
		internal int causticFrameIndex;
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

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.clearKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticMotionResult", causticMotionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle transportHandle, TextureHandle materialNormalHandle, TextureHandle velocityHandle, TextureHandle gradientHandle, TextureHandle gradient2Handle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "MaterialTransportTex", transportHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "MaterialNormalTex", materialNormalHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex", velocityHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap", gradientHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap2", gradient2Handle);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.traceKernel);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.resolveKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticMotionResult", causticMotionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex)
		{
			FluidSim2D fluidSim2D = context.display.sim;
			bool renderCausticMotion = lightingMode == LightingMode.Caustics && (temporalSettings.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.CausticMotion || particleFluidLighting2D.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
			int clearKernel = causticsCompute.FindKernel("Clear");
			int traceKernel = causticsCompute.FindKernel("Trace");
			int resolveKernel = causticsCompute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = particleFluidLighting2D.PhaseMaterials;
			int raysPerPixel = Mathf.Max(1, traceSettings.raysPerPixel);
			int totalRayBudget = particleFluidLighting2D.lightManager.UploadCausticsLightGpuData(causticLightParamsBuffer, context, new Vector2Int(width, height), raysPerPixel);

			Texture transportTexture = particleFluidLighting2D.materialTransportTexture != null ? particleFluidLighting2D.materialTransportTexture : Texture2D.blackTexture;
			Texture materialNormalTexture = particleFluidLighting2D.materialNormalTexture != null ? particleFluidLighting2D.materialNormalTexture : Texture2D.blackTexture;
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "transportResolution", new Vector2(transportTexture.width, transportTexture.height));
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "materialNormalResolution", new Vector2(materialNormalTexture.width, materialNormalTexture.height));
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticSize", new Vector2(width,height));
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRayCount", totalRayBudget);
			for (int i = 0; i < MaterialSlotCount; i++)
			{
				ParticleFluidLighting2D.PhaseMaterialSettings material = materials[i];
				int offset = i * 2;
				materialParams[offset] = new Vector4(material.indexOfRefraction, material.reflectance, material.metallic, material.absorption);
				Color diffuseTint = material.diffuseLightTint;
				materialParams[offset + 1] = new Vector4(diffuseTint.r, diffuseTint.g, diffuseTint.b, material.absorptionDiffuseTintBlend);
			}
			causticMaterialParamsBuffer.SetData(materialParams);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRaySteps", traceSettings.extraRayTravelSteps);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRayStride", traceSettings.rayStride);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsColourSampleStride", traceSettings.colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsStochasticReflection", traceSettings.stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionStrength", traceSettings.dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionRotation", traceSettings.dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoBrightnessInfluence", particleFluidLighting2D.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoSaturationInfluence", particleFluidLighting2D.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsRayBrightness", traceSettings.rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsTemporalJitterPixels", traceSettings.temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsSurfaceNormalJitterPixels", traceSettings.surfaceNormalJitterPixels);
			
			
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDeltaTime", fluidSim2D.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "useEllipticalBounds", fluidSim2D.analyticBoundary.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsCenter", fluidSim2D.analyticBoundary.ellipseBoundsCenter);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsSize", fluidSim2D.analyticBoundary.ellipseBoundsSize);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "obstacleY", fluidSim2D.analyticBoundary.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "analyticBoundaryExpansion", context.display.sim.analyticBoundary.analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldSize", context.renderRegion.size);

			return new CausticsComputePassState(causticsCompute, clearKernel, traceKernel, resolveKernel, width, height, totalRayBudget, raysPerPixel);
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
			if (lightingMode != LightingMode.Caustics)
			{
				return Texture2D.blackTexture;
			}

			return temporalSettings.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}
	}
}
