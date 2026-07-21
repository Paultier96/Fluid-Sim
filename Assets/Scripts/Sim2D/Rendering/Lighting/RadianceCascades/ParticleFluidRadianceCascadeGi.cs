using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidRadianceCascadeGi : MonoBehaviour
	{
		[Header("Shaders")]
		public ComputeShader radianceCascadeSdfCompute;
		public Shader radianceCascadeSdfShader;

		[Header("Global Illumination")]
		[FormerlySerializedAs("radianceCascadeTextureScale")] [Range(0.25f, 1f)] public float TextureScale = 0.5f;
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

		void Awake()
		{
			_lighting = GetComponent<ParticleFluidLighting2D>();
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
				int width = RoundUpToMultiple(Mathf.Max(1, Mathf.RoundToInt(causticSize.x * TextureScale)), cascadeTileDivisor);
				int height = RoundUpToMultiple(Mathf.Max(1, Mathf.RoundToInt(causticSize.y * TextureScale)), cascadeTileDivisor);
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 0");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 1");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed B");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload B");
				return;
			}

			ComputeHelper.Release(radianceCascadeTexture0, radianceCascadeTexture1, radianceCascadeSdfSeedA, radianceCascadeSdfSeedB, radianceCascadeSdfPayloadA, radianceCascadeSdfPayloadB);
		}

		static int RoundUpToMultiple(int value, int multiple)
		{
			multiple = Mathf.Max(1, multiple);
			return Mathf.Max(multiple, ((value + multiple - 1) / multiple) * multiple);
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, bool useGaussianBoundarySource)
		{
			Texture phase1Texture = RenderRadianceCascadeSdfLight(context, targetCommandBuffer, sharpCaustics, useGaussianBoundarySource);
			return phase1Texture;
		}

		internal void Release()
		{
			ReleaseResources();
			ParticleFluidRenderUtils.DestroyMaterial(ref _radianceCascadeSdfMaterial);
		}

		private void ReleaseResources()
		{
			EnsureResources(default, false);
		}

		private Texture RenderRadianceCascadeSdfLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, bool useGaussianBoundarySource)
		{
			BuildRadianceCascadeSdfField(context, targetCommandBuffer, 
				out RenderTexture sdfResult, 
				out RenderTexture sdfPayload, 
				_lighting.materialTransportTexture);
			return RenderRadianceCascadePass(context, targetCommandBuffer, sharpCaustics, sdfResult, sdfPayload, _radianceCascadeSdfMaterial, "Metaballs/Radiance Cascades SDF", useGaussianBoundarySource);
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
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
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
			Texture colourMap = display != null && display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture;

			int width = radianceCascadeSdfSeedA.width;
			int height = radianceCascadeSdfSeedA.height;
			ComputeShader compute = radianceCascadeSdfCompute;
			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("SeedBoundary");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			int resolveDistanceKernel = compute.FindKernel("ResolveDistance");

			targetCommandBuffer.SetComputeIntParam(compute, "_Width", width);
			targetCommandBuffer.SetComputeIntParam(compute, "_Height", height);
			targetCommandBuffer.SetComputeIntParam(compute, "transportWidth", materialTransportTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "transportHeight", materialTransportTexture.height);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldSize", context.renderRegion.size);

			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "Result", radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultPayload", radianceCascadeSdfPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			targetCommandBuffer.DispatchCompute(compute, clearKernel, gx, gy, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "MaterialTransportTex", materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "Result", radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultPayload", radianceCascadeSdfPayloadA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap", colourMap);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "MaterialTransportTex", materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ColourMap", colourMap);
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
				targetCommandBuffer.SetComputeIntParam(compute, "_Step", s);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcTex", src);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcPayloadTex", payloadSrc);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstTex", dst);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstPayloadTex", payloadDst);
				targetCommandBuffer.DispatchCompute(compute, jumpFloodKernel, gx, gy, 1);
				ParticleFluidRenderUtils.Swap(ref src, ref dst);
				ParticleFluidRenderUtils.Swap(ref payloadSrc, ref payloadDst);
			}

			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcTex", src);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcPayloadTex", payloadSrc);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "Result", dst);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ResultPayload", payloadDst);
			targetCommandBuffer.DispatchCompute(compute, resolveDistanceKernel, gx, gy, 1);

			resultTexture = dst;
			payloadTexture = payloadDst;
		}

		void SetCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, int width, int height, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, bool useGaussianBoundarySource)
		{
			ParticleDisplay2D display = context.display;
			bool useBoundarySourceTexture = false;
			float boundarySourceNormalization = 1f;
			Texture boundarySourceTexture = Texture2D.blackTexture;
			if (_lighting.directLight != null && _lighting.directLight.IsCausticsEnabled)
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

			targetCommandBuffer.SetGlobalTexture("_BoundarySourceTex", boundarySourceTexture);
			targetCommandBuffer.SetGlobalTexture("_ResultTex", sdfResult != null ? sdfResult : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("_PayloadTex", sdfPayload);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector2(width, height));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(rayRange * display.GetZoomScale(context.cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(raySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", intensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", blobEmissionStrength);
			targetCommandBuffer.SetGlobalFloat("_RadianceDistanceAttenuation", Mathf.Max(distanceAttenuation, 0f));
			targetCommandBuffer.SetGlobalFloat("_RadianceBoundaryCullPaddingPixels", Mathf.Max(boundaryCullPaddingPixels, 0f));
			ApplyLightGlobals(targetCommandBuffer, context.display.sim.analyticBoundary);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", directionalLightStrength);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightCascadeStart", directionalLightCascadeStart);
			targetCommandBuffer.SetGlobalFloat("_SdfPhase0InsetPixels", Mathf.Max(sdfPhase0InsetPixels, 0f));
			targetCommandBuffer.SetGlobalFloat("_SdfPhase0OutlinePixels", Mathf.Max(sdfPhase0OutlinePixels, 0f));
			targetCommandBuffer.SetGlobalInt("_UseBoundarySourceTex", useBoundarySourceTexture ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("_BoundarySourceNormalization", boundarySourceNormalization);
			targetCommandBuffer.SetGlobalInt("_SdfBoundarySourceMultiplyAlbedo", sdfBoundarySourceMultiplyAlbedo ? 1 : 0);
			ParticleFluidLayoutBindings.ApplyBoundaryGlobals(targetCommandBuffer, context.display.sim.analyticBoundary);
			targetCommandBuffer.SetGlobalVector("domainWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetGlobalVector("domainWorldSize", context.renderRegion.size);
		}

		void ApplyLightGlobals(CommandBuffer targetCommandBuffer, ParticleFluidAnalyticBoundary2D analyticBoundary)
		{
			ParticleFluidDirectionalLight2D skyLight = _lighting.lightManager.GetMainDirectionalLight();
			bool useDirectionalLight = directionalLightEnabled && skyLight != null;
			Vector3 direction = skyLight != null ? skyLight.GetBoundaryRefractedDirection(_lighting.PhaseMaterials[1].indexOfRefraction, analyticBoundary) : Vector3.down;
			Vector4 color = useDirectionalLight ? skyLight.EffectiveColor : Vector4.zero;
			float intensity = useDirectionalLight ? skyLight.intensity : 0f;
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightDirection", direction);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightColor", color);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightIntensity", intensity);
		}

	}
}
