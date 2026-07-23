using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidRadianceCascadeGi : MonoBehaviour
	{
		private static readonly int CascadeLevel = Shader.PropertyToID("_CascadeLevel");
		private static readonly int UpperCascadeTex = Shader.PropertyToID("_UpperCascadeTex");
		private static readonly int Width = Shader.PropertyToID("_Width");
		private static readonly int Height = Shader.PropertyToID("_Height");
		private static readonly int TransportWidth = Shader.PropertyToID("transportWidth");
		private static readonly int TransportHeight = Shader.PropertyToID("transportHeight");
		private static readonly int MetaballWorldCenter = Shader.PropertyToID("metaballWorldCenter");
		private static readonly int MetaballWorldSize = Shader.PropertyToID("metaballWorldSize");
		private static readonly int Result = Shader.PropertyToID("Result");
		private static readonly int ResultPayload = Shader.PropertyToID("ResultPayload");
		private static readonly int MaterialTransportTex = Shader.PropertyToID("MaterialTransportTex");
		private static readonly int GradientAtlas = Shader.PropertyToID("GradientAtlas");
		private static readonly int Step = Shader.PropertyToID("_Step");
		private static readonly int SrcTex = Shader.PropertyToID("_SrcTex");
		private static readonly int SrcPayloadTex = Shader.PropertyToID("_SrcPayloadTex");
		private static readonly int DstTex = Shader.PropertyToID("_DstTex");
		private static readonly int DstPayloadTex = Shader.PropertyToID("_DstPayloadTex");
		private static readonly int BoundarySourceTex = Shader.PropertyToID("_BoundarySourceTex");
		private static readonly int ResultTex = Shader.PropertyToID("_ResultTex");
		private static readonly int PayloadTex = Shader.PropertyToID("_PayloadTex");
		private static readonly int CascadeResolution = Shader.PropertyToID("_CascadeResolution");
		private static readonly int RayRange = Shader.PropertyToID("_RayRange");
		private static readonly int CascadeCount = Shader.PropertyToID("_CascadeCount");
		private static readonly int RaySteps = Shader.PropertyToID("_RaySteps");
		private static readonly int RadianceIntensity = Shader.PropertyToID("_RadianceIntensity");
		private static readonly int BlobEmissionStrength = Shader.PropertyToID("_BlobEmissionStrength");
		private static readonly int RadianceDistanceAttenuation = Shader.PropertyToID("_RadianceDistanceAttenuation");
		private static readonly int RadianceBoundaryCullPaddingPixels = Shader.PropertyToID("_RadianceBoundaryCullPaddingPixels");
		private static readonly int DirectionalLightEnabled = Shader.PropertyToID("_DirectionalLightEnabled");
		private static readonly int DirectionalLightDirection = Shader.PropertyToID("_DirectionalLightDirection");
		private static readonly int DirectionalLightColor = Shader.PropertyToID("_DirectionalLightColor");
		private static readonly int DirectionalLightIntensity = Shader.PropertyToID("_DirectionalLightIntensity");
		private static readonly int DirectionalLightStrength = Shader.PropertyToID("_DirectionalLightStrength");
		private static readonly int DirectionalLightCascadeStart = Shader.PropertyToID("_DirectionalLightCascadeStart");
		private static readonly int SdfPhase0InsetPixels = Shader.PropertyToID("_SdfPhase0InsetPixels");
		private static readonly int SdfPhase0OutlinePixels = Shader.PropertyToID("_SdfPhase0OutlinePixels");
		private static readonly int UseBoundarySourceTex = Shader.PropertyToID("_UseBoundarySourceTex");
		private static readonly int BoundarySourceNormalization = Shader.PropertyToID("_BoundarySourceNormalization");
		private static readonly int SdfBoundarySourceMultiplyAlbedo = Shader.PropertyToID("_SdfBoundarySourceMultiplyAlbedo");
		private static readonly int DomainWorldCenter = Shader.PropertyToID("domainWorldCenter");
		private static readonly int DomainWorldSize = Shader.PropertyToID("domainWorldSize");

		[Header("Shaders")]
		public ComputeShader radianceCascadeSdfCompute;
		public Shader radianceCascadeSdfShader;

		[FormerlySerializedAs("TextureScale")]
		[Header("Global Illumination")]
		[Range(0.25f, 1f)] public float textureScale = 0.5f;
		[Range(1, 6)] public int radianceCascadeCount = 4;
		[Min(0.0001f)] public float rayRange = 1.25f;
		[Range(1, 64)] public int raySteps = 16;
		[Min(0f)] public float intensity = 1f;
		[Min(0f)] public float blobEmissionStrength = 1f;
		[Min(0f)] public float distanceAttenuation = 0f;
		[Min(0f)] public float boundaryCullPaddingPixels = 8f;
		public bool directionalLightEnabled = true;
		[Min(0f)] public float directionalLightStrength = 1f;
		[Range(0f, 1f)] public float directionalLightCascadeStart = 1f;
		public float sdfPhase0InsetPixels = 1.5f;
		public float sdfPhase0OutlinePixels = 3f;
		public bool sdfBoundarySourceMultiplyAlbedo = true;
		[Range(0f, 1f)] public float directCausticStrength = 1f;

		private ParticleFluidLighting2D _lighting;
		private Material _radianceCascadeSdfMaterial;
		internal RenderTexture radianceCascadeTexture0;
		internal RenderTexture radianceCascadeTexture1;
		internal RenderTexture radianceCascadeSdfSeedA;
		internal RenderTexture radianceCascadeSdfSeedB;
		internal RenderTexture radianceCascadeSdfPayloadA;
		internal RenderTexture radianceCascadeSdfPayloadB;

		private void Awake()
		{
			_lighting = GetComponent<ParticleFluidLighting2D>();
		}

		private void OnEnable()
		{
			_lighting ??= GetComponent<ParticleFluidLighting2D>();
			EnsureMaterials();
		}

		private void OnDisable()
		{
			Release();
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _radianceCascadeSdfMaterial, radianceCascadeSdfShader);
		}

		internal void EnsureResources(Vector2Int causticSize, bool renderRadianceCascade)
		{
			if (renderRadianceCascade)
			{
				int cascadeTileDivisor = 1 << (Mathf.Clamp(radianceCascadeCount, 1, 6) - 1);
				int width = RoundUpToMultiple(Mathf.Max(1, Mathf.RoundToInt(causticSize.x * textureScale)), cascadeTileDivisor);
				int height = RoundUpToMultiple(Mathf.Max(1, Mathf.RoundToInt(causticSize.y * textureScale)), cascadeTileDivisor);
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 0");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 1");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed B");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload B");
				return;
			}

			ComputeHelper.Release(radianceCascadeTexture0, radianceCascadeTexture1, radianceCascadeSdfSeedA, radianceCascadeSdfSeedB, radianceCascadeSdfPayloadA, radianceCascadeSdfPayloadB);
			radianceCascadeTexture0 = null;
			radianceCascadeTexture1 = null;
			radianceCascadeSdfSeedA = null;
			radianceCascadeSdfSeedB = null;
			radianceCascadeSdfPayloadA = null;
			radianceCascadeSdfPayloadB = null;
		}

		private static int RoundUpToMultiple(int value, int multiple)
		{
			multiple = Mathf.Max(1, multiple);
			return Mathf.Max(multiple, ((value + multiple - 1) / multiple) * multiple);
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, bool useGaussianBoundarySource)
		{
			BuildRadianceCascadeSdfField(context, targetCommandBuffer, out RenderTexture sdfResult, out RenderTexture sdfPayload, _lighting.materialTransportTexture);
			Texture phase1Texture = RenderRadianceCascadePass(context, targetCommandBuffer, sharpCaustics, sdfResult, sdfPayload, _radianceCascadeSdfMaterial, "Metaballs/Radiance Cascades SDF", useGaussianBoundarySource);
			return phase1Texture;
		}

		internal void RecordRenderGraph(
			RenderGraph renderGraph,
			ParticleFluidLighting2D.FrameContext context,
			LightingInputHandles inputs,
			LightingResourceHandles resources,
			Texture sharpCaustics,
			bool useGaussianBoundarySource)
		{
			using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Radiance Cascades GI", out RadianceCascadePassData passData);
			passData.radianceCascadeGi = this;
			passData.context = context;
			passData.sharpCaustics = sharpCaustics != null ? sharpCaustics : Texture2D.blackTexture;
			passData.useGaussianBoundarySource = useGaussianBoundarySource;
			UseIfValid(builder, inputs.transport, AccessFlags.Read);
			UseIfValid(builder, inputs.gradientAtlas, AccessFlags.Read);
			UseIfValid(builder, resources.causticResolved, AccessFlags.Read);
			UseIfValid(builder, resources.causticTemporal, AccessFlags.Read);
			if (useGaussianBoundarySource)
			{
				UseIfValid(builder, resources.gaussianSoftLight0, AccessFlags.Read);
			}
			UseIfValid(builder, resources.radianceCascade0, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.radianceCascade1, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.radianceCascadeSdfSeedA, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.radianceCascadeSdfSeedB, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.radianceCascadeSdfPayloadA, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.radianceCascadeSdfPayloadB, AccessFlags.ReadWrite);
			builder.AllowPassCulling(false);
			builder.SetRenderFunc(static (RadianceCascadePassData data, UnsafeGraphContext context) =>
			{
				CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
				data.radianceCascadeGi.Render(data.context, nativeCommandBuffer, data.sharpCaustics, data.useGaussianBoundarySource);
			});
		}

		internal Texture GetOutputTexture()
		{
			if (radianceCascadeTexture0 == null || radianceCascadeTexture1 == null)
			{
				return Texture2D.blackTexture;
			}

			int cascadeCount = Mathf.Clamp(radianceCascadeCount, 1, 6);
			return cascadeCount % 2 == 0 ? radianceCascadeTexture0 : radianceCascadeTexture1;
		}

		internal void Release()
		{
			EnsureResources(default, false);
			ParticleFluidRenderUtils.DestroyMaterial(ref _radianceCascadeSdfMaterial);
		}

		private Texture RenderRadianceCascadePass(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, Material material, string sampleName, bool useGaussianBoundarySource)
		{
			targetCommandBuffer.BeginSample(sampleName);
			if (radianceCascadeTexture0 == null || radianceCascadeTexture1 == null)
			{
				targetCommandBuffer.EndSample(sampleName);
				return Texture2D.blackTexture;
			}

			int width = radianceCascadeTexture0.width;
			int height = radianceCascadeTexture0.height;
			int cascadeCount = Mathf.Clamp(radianceCascadeCount, 1, 6);
			RenderTexture source = radianceCascadeTexture0;
			RenderTexture target = radianceCascadeTexture1;
			targetCommandBuffer.SetRenderTarget(source);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);

			SetCommonParams(context, targetCommandBuffer, width, height, sharpCaustics, sdfResult, sdfPayload, useGaussianBoundarySource);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt(CascadeLevel, level);
				targetCommandBuffer.SetGlobalTexture(UpperCascadeTex, source);
				ParticleFluidRenderUtils.DrawRegionQuad(targetCommandBuffer, target, material, 0, context.renderRegion, context.cam, false);
				ParticleFluidRenderUtils.Swap(ref source, ref target);
			}

			targetCommandBuffer.EndSample(sampleName);
			return source;
		}

		private void BuildRadianceCascadeSdfField(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, out RenderTexture resultTexture, out RenderTexture payloadTexture, Texture materialTransportTexture)
		{
			resultTexture = null;
			payloadTexture = null;
			ParticleDisplay2D display = context.display;
			materialTransportTexture ??= Texture2D.blackTexture;
			Texture gradientAtlas = display != null && display.gradientAtlasTexture != null ? display.gradientAtlasTexture : Texture2D.blackTexture;

			int width = radianceCascadeSdfSeedA.width;
			int height = radianceCascadeSdfSeedA.height;
			ComputeShader compute = radianceCascadeSdfCompute;
			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("SeedBoundary");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			int resolveDistanceKernel = compute.FindKernel("ResolveDistance");

			targetCommandBuffer.SetComputeIntParam(compute, Width, width);
			targetCommandBuffer.SetComputeIntParam(compute, Height, height);
			targetCommandBuffer.SetComputeIntParam(compute, TransportWidth, materialTransportTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, TransportHeight, materialTransportTexture.height);
			targetCommandBuffer.SetComputeVectorParam(compute, MetaballWorldCenter, context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(compute, MetaballWorldSize, context.renderRegion.size);

			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, Result, radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, ResultPayload, radianceCascadeSdfPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			targetCommandBuffer.DispatchCompute(compute, clearKernel, gx, gy, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, MaterialTransportTex, materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, Result, radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, ResultPayload, radianceCascadeSdfPayloadA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, GradientAtlas, gradientAtlas);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, MaterialTransportTex, materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, GradientAtlas, gradientAtlas);
			targetCommandBuffer.DispatchCompute(compute, seedKernel, gx, gy, 1);

			RenderTexture src = radianceCascadeSdfSeedA;
			RenderTexture dst = radianceCascadeSdfSeedB;
			RenderTexture payloadSrc = radianceCascadeSdfPayloadA;
			RenderTexture payloadDst = radianceCascadeSdfPayloadB;
			int maxDim = Mathf.Max(width, height);
			int step = 1;
			while ((step << 1) < maxDim)
			{
				step <<= 1;
			}

			for (int s = step; s >= 1; s >>= 1)
			{
				targetCommandBuffer.SetComputeIntParam(compute, Step, s);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, SrcTex, src);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, SrcPayloadTex, payloadSrc);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, DstTex, dst);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, DstPayloadTex, payloadDst);
				targetCommandBuffer.DispatchCompute(compute, jumpFloodKernel, gx, gy, 1);
				ParticleFluidRenderUtils.Swap(ref src, ref dst);
				ParticleFluidRenderUtils.Swap(ref payloadSrc, ref payloadDst);
			}

			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, SrcTex, src);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, SrcPayloadTex, payloadSrc);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, Result, dst);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, ResultPayload, payloadDst);
			targetCommandBuffer.DispatchCompute(compute, resolveDistanceKernel, gx, gy, 1);

			resultTexture = dst;
			payloadTexture = payloadDst;
		}

		private void SetCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer commandBuffer, int width, int height, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, bool useGaussianBoundarySource)
		{
			ParticleDisplay2D display = context.display;
			bool useBoundarySourceTexture = false;
			float boundarySourceNormalization = 1f;
			Texture boundarySourceTexture = Texture2D.blackTexture;
			if (_lighting.directLight != null && _lighting.directLight.isActiveAndEnabled)
			{
				if (useGaussianBoundarySource && _lighting.gaussianSss.gaussianSoftLightTexture0 != null)
				{
					boundarySourceTexture = _lighting.gaussianSss.gaussianSoftLightTexture0;
					boundarySourceNormalization = 1f / Mathf.Max(_lighting.gaussianSss.gaussianDiffuseScatterStrength, 0.0001f);
					useBoundarySourceTexture = true;
				}
				else if (sharpCaustics != null)
				{
					boundarySourceTexture = sharpCaustics;
					useBoundarySourceTexture = true;
				}
			}

			commandBuffer.SetGlobalTexture(BoundarySourceTex, boundarySourceTexture);
			commandBuffer.SetGlobalTexture(ResultTex, sdfResult != null ? sdfResult : Texture2D.blackTexture);
			commandBuffer.SetGlobalTexture(PayloadTex, sdfPayload);
			commandBuffer.SetGlobalVector(CascadeResolution, new Vector2(width, height));
			commandBuffer.SetGlobalFloat(RayRange, Mathf.Max(rayRange * display.GetZoomScale(context.cam), 0.001f));
			commandBuffer.SetGlobalInt(CascadeCount, Mathf.Clamp(radianceCascadeCount, 1, 6));
			commandBuffer.SetGlobalInt(RaySteps, Mathf.Max(raySteps, 1));
			commandBuffer.SetGlobalFloat(RadianceIntensity, intensity);
			commandBuffer.SetGlobalFloat(BlobEmissionStrength, blobEmissionStrength);
			commandBuffer.SetGlobalFloat(RadianceDistanceAttenuation, Mathf.Max(distanceAttenuation, 0f));
			commandBuffer.SetGlobalFloat(RadianceBoundaryCullPaddingPixels, Mathf.Max(boundaryCullPaddingPixels, 0f));
			ParticleFluidDirectionalLight2D skyLight = _lighting.lightManager.GetMainDirectionalLight();
			bool useDirectionalLight = directionalLightEnabled && skyLight != null;
			Vector3 direction = skyLight != null ? skyLight.GetBoundaryRefractedDirection(_lighting.PhaseMaterials[1].indexOfRefraction, context.display.sim.analyticBoundary) : Vector3.down;
			Vector4 color = useDirectionalLight ? skyLight.EffectiveColor : Vector4.zero;
			float intensity1 = useDirectionalLight ? skyLight.intensity : 0f;
			commandBuffer.SetGlobalInt(DirectionalLightEnabled, useDirectionalLight ? 1 : 0);
			commandBuffer.SetGlobalVector(DirectionalLightDirection, direction);
			commandBuffer.SetGlobalVector(DirectionalLightColor, color);
			commandBuffer.SetGlobalFloat(DirectionalLightIntensity, intensity1);
			commandBuffer.SetGlobalFloat(DirectionalLightStrength, directionalLightStrength);
			commandBuffer.SetGlobalFloat(DirectionalLightCascadeStart, directionalLightCascadeStart);
			commandBuffer.SetGlobalFloat(SdfPhase0InsetPixels, Mathf.Max(sdfPhase0InsetPixels, 0f));
			commandBuffer.SetGlobalFloat(SdfPhase0OutlinePixels, Mathf.Max(sdfPhase0OutlinePixels, 0f));
			commandBuffer.SetGlobalInt(UseBoundarySourceTex, useBoundarySourceTexture ? 1 : 0);
			commandBuffer.SetGlobalFloat(BoundarySourceNormalization, boundarySourceNormalization);
			commandBuffer.SetGlobalInt(SdfBoundarySourceMultiplyAlbedo, sdfBoundarySourceMultiplyAlbedo ? 1 : 0);
			ParticleFluidRenderBindings.ApplyBoundaryGlobals(commandBuffer, context.display.sim.analyticBoundary);
			commandBuffer.SetGlobalVector(DomainWorldCenter, context.renderRegion.center);
			commandBuffer.SetGlobalVector(DomainWorldSize, context.renderRegion.size);
		}

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		private class RadianceCascadePassData
		{
			public ParticleFluidRadianceCascadeGi radianceCascadeGi;
			public ParticleFluidLighting2D.FrameContext context;
			public Texture sharpCaustics;
			public bool useGaussianBoundarySource;
		}
	}
}
