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
		[System.Serializable]
		public sealed class CausticsTraceSettings
		{
			[Header("Refraction")]
			public bool stochasticReflection = true;
			[Min(0f)] public float dispersionStrength;
			[Range(0f, 1f)] public float dispersionRotation = 1f;

			[Header("Rays")]
			[Range(8, 192)] public int extraRayTravelSteps = 64;
			[Min(0f)] public float rayBrightness = 1f;
			[Min(1)] public int rayStride = 1;
			[Range(1, 128)] public int raysPerPixel = 1;
			[Min(1)] public int colourSampleStride = 8;
			[Min(0f)] public float blur = 1.5f;
			[Range(0f, 1f)] public float temporalJitterPixels;
			[Min(0f)] public float surfaceNormalJitterPixels;
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
		}

		[Header("Shaders")]
		public Shader blurShader;
		public Shader temporalShader;
		public ComputeShader causticsCompute;
		
		[Header("Caustics")]
		[Range(0.125f, 1f)] public float textureScale = 0.5f;

		[Header("Ray marched Lighting")]
		public CausticsTraceSettings traceSettings = new();

		[Header("Ray marched Lighting - Temporal Denoising")]
		public CausticsTemporalSettings temporalSettings = new();

		internal ParticleFluidLighting2D particleFluidLighting2D;
		internal bool IsCausticsEnabled => isActiveAndEnabled;

		void Awake()
		{
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
			if (!IsCausticsEnabled)
			{
				return Texture2D.blackTexture;
			}

			return temporalSettings.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}

		internal void EnsureResources(Vector2Int causticSize, Bounds domainRegion)
		{
			int causticWidth = causticSize.x;
			int causticHeight = causticSize.y;
			if (IsCausticsEnabled)
			{
				EnsureResources(causticWidth, causticHeight, true);
				if (temporalSettings.denoisingEnabled)
				{
					temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, true);
				}
				else
				{
					temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, false);
				}
			}
			else
			{
				Release();
			}
		}

		internal void Release()
		{
			EnsureResources(0, 0, false);
			temporalCaustics.Release();
		}
		
		internal ParticleFluidCausticsTemporal temporalCaustics;
		ComputeBuffer causticAccumulationBuffer;
		ComputeBuffer causticLightParamsBuffer;
		ComputeBuffer causticMaterialParamsBuffer;
		internal RenderTexture causticResolvedTexture;
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
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticLightParamsBuffer, 18);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticMaterialParamsBuffer, MaterialSlotCount * 2);
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				return;
			}

			ComputeHelper.Release(causticAccumulationBuffer, causticLightParamsBuffer, causticMaterialParamsBuffer);
			ComputeHelper.Release(causticResolvedTexture);
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle causticResolvedHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.clearKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle transportHandle, TextureHandle materialNormalHandle, TextureHandle gradientHandle, TextureHandle gradient2Handle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "MaterialTransportTex", transportHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "MaterialNormalTex", materialNormalHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap", gradientHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap2", gradient2Handle);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.traceKernel);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, TextureHandle causticResolvedHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.resolveKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex)
		{
			FluidSim2D fluidSim2D = context.display.sim;
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
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsStochasticReflection", traceSettings.stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionStrength", traceSettings.dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionRotation", traceSettings.dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoBrightnessInfluence", particleFluidLighting2D.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoSaturationInfluence", particleFluidLighting2D.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsRayBrightness", traceSettings.rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsTemporalJitterPixels", traceSettings.temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsSurfaceNormalJitterPixels", traceSettings.surfaceNormalJitterPixels);
			
			
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "useEllipticalBounds", fluidSim2D.analyticBoundary.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsCenter", fluidSim2D.analyticBoundary.BoundsCenter);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsSize", fluidSim2D.analyticBoundary.boundsSize);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "obstacleY", fluidSim2D.analyticBoundary.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "analyticBoundaryExpansion", context.display.sim.analyticBoundary.analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldSize", context.renderRegion.size);

			return new CausticsComputePassState(causticsCompute, clearKernel, traceKernel, resolveKernel, width, height, totalRayBudget, raysPerPixel);
		}

		void BindComputeBuffers(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulationBuffer);
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
			if (!IsCausticsEnabled)
			{
				return Texture2D.blackTexture;
			}

			return temporalSettings.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}
	}
}
