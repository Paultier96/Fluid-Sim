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
		[Header("Shaders")]
		public Shader blurShader;
		public ComputeShader causticsCompute;
		
		[Header("Caustics")]
		[Range(0.125f, 1f)] public float textureScale = 0.5f;

		[Header("Refraction")]
		public bool stochasticReflection = true;
		[Min(0f)] public float dispersionStrength = 0.33f;
		[Range(0f, 1f)] public float dispersionRotation = 0.1f;

		[Header("Rays")]
		[Range(8, 192)] public int extraRayTravelSteps = 192;
		[Min(0f)] public float rayBrightness = 1.38f;
		[Min(1)] public int rayStride = 1;
		[Range(1, 128)] public int raysPerPixel = 10;
		[Min(1)] public int colourSampleStride = 5;
		[Min(0f)] public float blur = 2f;
		[Range(0f, 1f)] public float temporalJitterPixels = 0.75f;
		[Min(0f)] public float surfaceNormalJitterPixels = 2;

		[Header("Temporal Denoising")]
		public ParticleFluidCausticsTemporal temporalCaustics = new();

		private ParticleFluidLighting2D _particleFluidLighting2D;

		private void Awake()
		{
		    _particleFluidLighting2D = GetComponent<ParticleFluidLighting2D>();
		}

		private void OnEnable()
		{
			_particleFluidLighting2D ??= GetComponent<ParticleFluidLighting2D>();
			ResetKernelCache();
			EnsureMaterials();
		}

		private void OnDisable()
		{
			Release();
		}

		private void OnValidate()
		{
			ResetKernelCache();
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _causticBlurMaterial, blurShader);
			temporalCaustics.EnsureMaterials();
		}

		internal Texture GetCurrentDirectLightTexture()
		{
			if (!isActiveAndEnabled)
			{
				return Texture2D.blackTexture;
			}

			return temporalCaustics.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}

		internal void EnsureResources(Vector2Int causticSize, Bounds domainRegion)
		{
			if (isActiveAndEnabled)
			{
				EnsureResources(causticSize.x, causticSize.y, true);
				temporalCaustics.EnsureTemporalResources(causticSize.x, causticSize.y, domainRegion);
			}
			else
			{
				ReleaseResources();
			}
		}

		internal void Release()
		{
			ReleaseResources();
			temporalCaustics.Release();
			ParticleFluidRenderUtils.DestroyMaterial(ref _causticBlurMaterial);
		}
		
		private ComputeBuffer _causticLightParamsBuffer;
		private ComputeBuffer _causticMaterialParamsBuffer;
		internal RenderTexture causticResolvedTexture;
		private Material _causticBlurMaterial;
		internal int causticFrameIndex;
		private int _clearKernel = -1;
		private int _traceKernel = -1;
		private int _resolveKernel = -1;
		private const int MaterialSlotCount = 3;
		private readonly Vector4[] _materialParams = new Vector4[MaterialSlotCount * 2];

		private readonly struct CausticsTextureState
		{
			public readonly ComputeShader compute;
			public readonly int width;
			public readonly int height;

			public CausticsTextureState(ComputeShader compute, int width, int height)
			{
				this.compute = compute;
				this.width = width;
				this.height = height;
			}
		}

		internal void EnsureResources(int width, int height, bool renderCaustics)
		{
			if (renderCaustics)
			{
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref _causticLightParamsBuffer, 18);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref _causticMaterialParamsBuffer, MaterialSlotCount * 2);
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				return;
			}

			ComputeHelper.Release(_causticLightParamsBuffer, _causticMaterialParamsBuffer);
			ComputeHelper.Release(causticResolvedTexture);
			_causticLightParamsBuffer = null;
			_causticMaterialParamsBuffer = null;
			causticResolvedTexture = null;
		}

		private void ReleaseResources()
		{
			EnsureResources(0, 0, false);
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, BufferHandle causticAccumulationHandle, TextureHandle causticResolvedHandle)
		{
			CausticsTextureState state = ApplyCausticTextureParams(targetCommandBuffer);
			BindAccumulationBuffer(targetCommandBuffer, state.compute, _clearKernel, causticAccumulationHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, _clearKernel, "CausticResult", causticResolvedHandle);
			DispatchCompute(targetCommandBuffer, state.compute, _clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, BufferHandle causticAccumulationHandle, TextureHandle transportHandle, TextureHandle materialNormalHandle, TextureHandle gradientAtlasHandle)
		{
			CausticsTextureState state = ApplyCausticTextureParams(targetCommandBuffer);
			int totalRayBudget = ApplyTraceParams(context, targetCommandBuffer, frameIndex, state.width, state.height);
			targetCommandBuffer.SetComputeTextureParam(state.compute, _traceKernel, "MaterialTransportTex", transportHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, _traceKernel, "MaterialNormalTex", materialNormalHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, _traceKernel, "GradientAtlas", gradientAtlasHandle);
			BindTraceBuffers(targetCommandBuffer, state.compute, _traceKernel, causticAccumulationHandle);
			int traceThreadCount = Mathf.Min(Mathf.Max(totalRayBudget, 0) * Mathf.Max(raysPerPixel, 1), ParticleFluidLighting2D.MaxCausticTraceThreads);
			if (traceThreadCount > 0)
			{
				targetCommandBuffer.DispatchCompute(state.compute, _traceKernel, Mathf.CeilToInt(traceThreadCount / (float)ParticleFluidLighting2D.CausticTraceThreadGroupSize), 1, 1);
			}
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, BufferHandle causticAccumulationHandle, TextureHandle causticResolvedHandle)
		{
			CausticsTextureState state = ApplyCausticTextureParams(targetCommandBuffer);
			BindAccumulationBuffer(targetCommandBuffer, state.compute, _resolveKernel, causticAccumulationHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, _resolveKernel, "CausticResult", causticResolvedHandle);
			DispatchCompute(targetCommandBuffer, state.compute, _resolveKernel, state.width, state.height);
		}

		internal void RecordRenderGraph(RenderGraph renderGraph, ParticleFluidLighting2D.FrameContext context, LightingInputHandles inputs, LightingResourceHandles resources)
		{
			int frameIndex = causticFrameIndex++;
			BufferHandle causticAccumulation = renderGraph.CreateBuffer(new BufferDesc
			{
				name = "Caustics Accumulation",
				count = causticResolvedTexture.width * causticResolvedTexture.height * 4,
				stride = sizeof(uint),
				target = GraphicsBuffer.Target.Structured
			});
			
			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Caustics Clear", out CausticsComputePassData passData))
			{
				passData.directLight = this;
				passData.context = context;
				passData.frameIndex = frameIndex;
				passData.causticAccumulation = causticAccumulation;
				passData.causticResolved = resources.causticResolved;
				builder.UseBuffer(passData.causticAccumulation, AccessFlags.Write);
				UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.directLight.RecordComputeClear(data.context, context.cmd, data.frameIndex, data.causticAccumulation, data.causticResolved);
				});
			}

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Caustics Trace", out CausticsComputePassData passData))
			{
				passData.directLight = this;
				passData.context = context;
				passData.frameIndex = frameIndex;
				passData.causticAccumulation = causticAccumulation;
				passData.transport = inputs.transport;
				passData.materialNormal = inputs.materialNormal;
				passData.gradientAtlas = inputs.gradientAtlas;
				builder.UseBuffer(passData.causticAccumulation, AccessFlags.ReadWrite);
				UseIfValid(builder, passData.transport, AccessFlags.Read);
				UseIfValid(builder, passData.materialNormal, AccessFlags.Read);
				UseIfValid(builder, passData.gradientAtlas, AccessFlags.Read);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.directLight.RecordComputeTrace(data.context, context.cmd, data.frameIndex, data.causticAccumulation, data.transport, data.materialNormal, data.gradientAtlas);
				});
			}

			using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Caustics Resolve", out CausticsComputePassData passData))
			{
				passData.directLight = this;
				passData.context = context;
				passData.frameIndex = frameIndex;
				passData.causticAccumulation = causticAccumulation;
				passData.causticResolved = resources.causticResolved;
				builder.UseBuffer(passData.causticAccumulation, AccessFlags.Read);
				UseIfValid(builder, passData.causticResolved, AccessFlags.Write);
				builder.SetRenderFunc(static (CausticsComputePassData data, ComputeGraphContext context) =>
				{
					data.directLight.RecordComputeResolve(data.context, context.cmd, data.frameIndex, data.causticAccumulation, data.causticResolved);
				});
			}

			float causticBlurRadius = blur * (context.display.metaballs.renderTextureScale * textureScale);
			Vector2Int causticTextureSize = new(causticResolvedTexture.width, causticResolvedTexture.height);
			ParticleFluidRenderUtils.RecordGaussianBlur(renderGraph, "Caustics Blur", causticBlurRadius, _causticBlurMaterial, resources.causticResolved, resources.causticBlur, causticTextureSize);

			using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Caustics Temporal", out CausticsPassData passData))
			{
				passData.directLight = this;
				passData.context = context;
				UseIfValid(builder, resources.causticResolved, AccessFlags.Read);
				UseIfValid(builder, resources.causticTemporal, AccessFlags.ReadWrite);
				UseIfValid(builder, resources.causticHistory, AccessFlags.ReadWrite);
				UseIfValid(builder, inputs.velocity, AccessFlags.Read);
				builder.SetRenderFunc(static (CausticsPassData data, UnsafeGraphContext context) =>
				{
					CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
					data.directLight.temporalCaustics.RecordTemporal(data.context, nativeCommandBuffer, data.directLight.causticResolvedTexture);
				});
			}
		}

		private CausticsTextureState ApplyCausticTextureParams(IComputeCommandBuffer targetCommandBuffer)
		{
			EnsureCausticsKernels();
			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticSize", new Vector2(width, height));
			return new CausticsTextureState(causticsCompute, width, height);
		}

		private int ApplyTraceParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, int frameIndex, int width, int height)
		{
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = _particleFluidLighting2D.PhaseMaterials;
			int totalRayBudget = _particleFluidLighting2D.lightManager.UploadCausticsLightGpuData(_causticLightParamsBuffer, context, new Vector2Int(width, height), raysPerPixel);

			Texture transportTexture = _particleFluidLighting2D.materialTransportTexture != null ? _particleFluidLighting2D.materialTransportTexture : Texture2D.blackTexture;
			Texture materialNormalTexture = _particleFluidLighting2D.materialNormalTexture != null ? _particleFluidLighting2D.materialNormalTexture : Texture2D.blackTexture;
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "transportResolution", new Vector2(transportTexture.width, transportTexture.height));
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "materialNormalResolution", new Vector2(materialNormalTexture.width, materialNormalTexture.height));
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRayCount", totalRayBudget);
			for (int i = 0; i < MaterialSlotCount; i++)
			{
				ParticleFluidLighting2D.PhaseMaterialSettings material = materials[i];
				int offset = i * 2;
				_materialParams[offset] = new Vector4(material.indexOfRefraction, material.reflectance, material.metallic, material.absorption);
				Color diffuseTint = material.diffuseLightTint;
				_materialParams[offset + 1] = new Vector4(diffuseTint.r, diffuseTint.g, diffuseTint.b, material.absorptionDiffuseTintBlend);
			}
			_causticMaterialParamsBuffer.SetData(_materialParams);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRaySteps", extraRayTravelSteps);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRayStride", rayStride);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsColourSampleStride", colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsStochasticReflection", stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionStrength", dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsDispersionRotation", dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoBrightnessInfluence", _particleFluidLighting2D.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsAbsorptionAlbedoSaturationInfluence", _particleFluidLighting2D.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsRayBrightness", rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsTemporalJitterPixels", temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "causticsSurfaceNormalJitterPixels", surfaceNormalJitterPixels);
			
			ParticleFluidAnalyticBoundary2D analyticBoundary = context.display.sim.analyticBoundary;

			targetCommandBuffer.SetComputeIntParam(causticsCompute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetComputeIntParam(causticsCompute, "useEllipticalBounds", analyticBoundary.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsCenter", analyticBoundary.BoundsCenter);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "ellipseBoundsSize", analyticBoundary.boundsSize);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "obstacleY", analyticBoundary.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(causticsCompute, "analyticBoundaryExpansion", analyticBoundary.analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(causticsCompute, "causticsWorldSize", context.renderRegion.size);

			return totalRayBudget;
		}

		private void EnsureCausticsKernels()
		{
			if (_clearKernel >= 0 && _traceKernel >= 0 && _resolveKernel >= 0)
			{
				return;
			}

			_clearKernel = causticsCompute.FindKernel("Clear");
			_traceKernel = causticsCompute.FindKernel("Trace");
			_resolveKernel = causticsCompute.FindKernel("Resolve");
		}

		private void ResetKernelCache()
		{
			_clearKernel = -1;
			_traceKernel = -1;
			_resolveKernel = -1;
		}

		private static void BindAccumulationBuffer(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, BufferHandle causticAccumulation)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulation);
		}

		private void BindTraceBuffers(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, BufferHandle causticAccumulation)
		{
			BindAccumulationBuffer(targetCommandBuffer, compute, kernel, causticAccumulation);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsLights", _causticLightParamsBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsMaterials", _causticMaterialParamsBuffer);
		}

		private static void DispatchCompute(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		public Texture GetSharpCausticsTexture()
		{
			if (!isActiveAndEnabled)
			{
				return Texture2D.blackTexture;
			}

			return temporalCaustics.denoisingEnabled ? (Texture)temporalCaustics.causticTemporalTexture : causticResolvedTexture;
		}

		private class CausticsComputePassData
		{
			public ParticleFluidDirectLight directLight;
			public ParticleFluidLighting2D.FrameContext context;
			public int frameIndex;
			public TextureHandle transport;
			public TextureHandle materialNormal;
			public TextureHandle gradientAtlas;
			public TextureHandle causticResolved;
			public BufferHandle causticAccumulation;
		}

		private class CausticsPassData
		{
			public ParticleFluidDirectLight directLight;
			public ParticleFluidLighting2D.FrameContext context;
		}
	}
}
