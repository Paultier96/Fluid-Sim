using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
		public bool radianceCascadeEnabled = false;
		[Range(0.25f, 1f)] public float radianceCascadeTextureScale = 0.5f;
		[Range(1, 6)] public int radianceCascadeCount = 4;
		[Min(0.0001f)] public float radianceCascadeRayRange = 1.25f;
		[Range(1, 64)] public int radianceCascadeRaySteps = 16;
		[Min(0f)] public float radianceCascadeIntensity = 1f;
		[Min(0f)] public float radianceCascadeBlobEmissionStrength = 1f;
		public bool radianceCascadeDirectionalLightEnabled = true;
		[Min(0f)] public float radianceCascadeDirectionalLightStrength = 1f;
		[Range(0f, 1f)] public float radianceCascadeDirectionalLightCascadeStart = 1f;
		[Min(0f)] public float radianceCascadeSdfPhase0InsetPixels = 1.5f;
		public bool radianceCascadeSdfBoundarySourceMultiplyAlbedo = true;
		public bool radianceCascadeSdfApproximateAbsorption = false;
		[Range(0f, 1f)] public float radianceCascadeDirectCausticStrength = 1f;

		internal ParticleFluidLighting2D owner;
		internal Material radianceCascadeSdfMaterial;
		internal RenderTexture radianceCascadeTexture0;
		internal RenderTexture radianceCascadeTexture1;
		internal RenderTexture softLightPhase0Texture;
		internal RenderTexture radianceCascadeSdfSeedA;
		internal RenderTexture radianceCascadeSdfSeedB;
		internal RenderTexture radianceCascadeSdfPayloadA;
		internal RenderTexture radianceCascadeSdfPayloadB;
		internal RenderTexture radianceCascadeSdfNormalA;
		internal RenderTexture radianceCascadeSdfNormalB;
		internal Texture CurrentPhase1Texture;

		void Awake()
		{
			owner = GetComponent<ParticleFluidLighting2D>();
			ResetDebugOutputs();
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref radianceCascadeSdfMaterial, radianceCascadeSdfShader);
		}

		internal void EnsureResources(Vector2Int causticSize, bool renderRadianceCascade)
		{
			if (renderRadianceCascade)
			{
				int width = Mathf.Max(1, Mathf.RoundToInt(causticSize.x * radianceCascadeTextureScale));
				int height = Mathf.Max(1, Mathf.RoundToInt(causticSize.y * radianceCascadeTextureScale));
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 0");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Radiance Cascade 1");
				ComputeHelper.CreateRenderTexture(ref softLightPhase0Texture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Soft Light Phase 0");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed B");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload A");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload B");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfNormalA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Result");
				ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfNormalB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload");
				return;
			}

			ComputeHelper.Release(radianceCascadeTexture0, radianceCascadeTexture1, softLightPhase0Texture, radianceCascadeSdfSeedA, radianceCascadeSdfSeedB, radianceCascadeSdfPayloadA, radianceCascadeSdfPayloadB, radianceCascadeSdfNormalA, radianceCascadeSdfNormalB);
			radianceCascadeTexture0 = null;
			radianceCascadeTexture1 = null;
			softLightPhase0Texture = null;
			radianceCascadeSdfSeedA = null;
			radianceCascadeSdfSeedB = null;
			radianceCascadeSdfPayloadA = null;
			radianceCascadeSdfPayloadB = null;
			radianceCascadeSdfNormalA = null;
			radianceCascadeSdfNormalB = null;
		}

		internal void ResetDebugOutputs()
		{
			CurrentPhase1Texture = Texture2D.blackTexture;
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, bool useGaussianBoundarySource)
		{
			Texture phase1Texture = RenderRadianceCascadeSdfLight(context, targetCommandBuffer, sharpCaustics, useGaussianBoundarySource);
			CurrentPhase1Texture = phase1Texture;
			return phase1Texture;
		}

		internal void Release()
		{
			ReleaseResources();
			ParticleFluidRenderUtils.DestroyMaterial(ref radianceCascadeSdfMaterial);
		}

		internal void ReleaseResources()
		{
			EnsureResources(default, false);
			ResetDebugOutputs();
		}

		Texture RenderRadianceCascadeSdfLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, bool useGaussianBoundarySource)
		{
			if (radianceCascadeSdfMaterial == null || !BuildRadianceCascadeSdfField(context, targetCommandBuffer, out RenderTexture sdfResult, out RenderTexture sdfPayload))
			{
				return Texture2D.blackTexture;
			}

			return RenderRadianceCascadePass(context, targetCommandBuffer, sharpCaustics, sdfResult, sdfPayload, radianceCascadeSdfMaterial, "Metaballs/Radiance Cascades SDF", useGaussianBoundarySource);
		}

		Texture RenderRadianceCascadePass(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, Material material, string sampleName, bool useGaussianBoundarySource)
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

		bool BuildRadianceCascadeSdfField(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, out RenderTexture resultTexture, out RenderTexture payloadTexture)
		{
			resultTexture = null;
			payloadTexture = null;
			ParticleDisplay2D display = context.display;
			if (radianceCascadeSdfCompute == null
			    || radianceCascadeSdfSeedA == null
			    || radianceCascadeSdfSeedB == null
			    || radianceCascadeSdfPayloadA == null
			    || radianceCascadeSdfPayloadB == null
			    || owner.materialTransportTexture == null)
			{
				return false;
			}

			int width = radianceCascadeSdfSeedA.width;
			int height = radianceCascadeSdfSeedA.height;
			ComputeShader compute = radianceCascadeSdfCompute;
			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("SeedBoundary");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			int resolveDistanceKernel = compute.FindKernel("ResolveDistance");

			targetCommandBuffer.SetComputeIntParam(compute, "_Width", width);
			targetCommandBuffer.SetComputeIntParam(compute, "_Height", height);
			targetCommandBuffer.SetComputeIntParam(compute, "transportWidth", owner.materialTransportTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "transportHeight", owner.materialTransportTexture.height);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldSize", context.renderRegion.size);

			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "Result", radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultPayload", radianceCascadeSdfPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			targetCommandBuffer.DispatchCompute(compute, clearKernel, gx, gy, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "MaterialTransportTex", owner.materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "Result", radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultPayload", radianceCascadeSdfPayloadA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "MaterialTransportTex", owner.materialTransportTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ColourMap", display.gradientTexture);
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
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "Result", radianceCascadeSdfNormalA);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ResultPayload", radianceCascadeSdfNormalB);
			targetCommandBuffer.DispatchCompute(compute, resolveDistanceKernel, gx, gy, 1);

			resultTexture = radianceCascadeSdfNormalA;
			payloadTexture = radianceCascadeSdfNormalB;
			return true;
		}

		void SetCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, int width, int height, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, bool useGaussianBoundarySource)
		{
			ParticleDisplay2D display = context.display;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			bool useBoundarySourceTexture = false;
			Texture boundarySourceTexture = Texture2D.blackTexture;
			if (owner.directLight != null && owner.directLight.lightingMode == ParticleFluidLighting2D.LightingMode.Caustics)
			{
				if (useGaussianBoundarySource && softLightPhase0Texture != null)
				{
					boundarySourceTexture = softLightPhase0Texture;
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
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(radianceCascadeRayRange * display.GetZoomScale(context.cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(radianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", radianceCascadeIntensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", radianceCascadeBlobEmissionStrength);
			ApplyLightGlobals(targetCommandBuffer, context.display.sim.analyticBoundary);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", radianceCascadeDirectionalLightStrength);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightCascadeStart", radianceCascadeDirectionalLightCascadeStart);
			targetCommandBuffer.SetGlobalFloat("_SdfPhase0InsetPixels", Mathf.Max(radianceCascadeSdfPhase0InsetPixels, 0f));
			targetCommandBuffer.SetGlobalInt("_UseBoundarySourceTex", useBoundarySourceTexture ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_SdfBoundarySourceMultiplyAlbedo", radianceCascadeSdfBoundarySourceMultiplyAlbedo ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_SdfApproximateAbsorption", radianceCascadeSdfApproximateAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_HybridPhase1Only", useGaussianBoundarySource ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("domainWorldCenter", context.renderRegion.center);
			targetCommandBuffer.SetGlobalVector("domainWorldSize", context.renderRegion.size);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", materials[0].absorption);
			targetCommandBuffer.SetGlobalVector("radianceCascadePhase0AbsorptionTint", materials[0].diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("radianceCascadePhase0AbsorptionTintBlend", materials[0].radianceCascadeAbsorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", owner.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", owner.absorptionAlbedoSaturationInfluence);
		}

		void ApplyLightGlobals(CommandBuffer targetCommandBuffer, ParticleFluidAnalyticBoundary2D analyticBoundary)
		{
			ParticleFluidDirectionalLight2D skyLight = owner.lightManager.GetMainDirectionalLight();
			bool useDirectionalLight = radianceCascadeDirectionalLightEnabled && skyLight != null;
			Vector3 direction = skyLight != null ? skyLight.GetBoundaryRefractedDirection(owner.PhaseMaterials[1].indexOfRefraction, analyticBoundary) : Vector3.down;
			Vector4 color = useDirectionalLight ? skyLight.EffectiveColor : Vector4.zero;
			float intensity = useDirectionalLight ? skyLight.intensity : 0f;
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightDirection", direction);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightColor", color);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightIntensity", intensity);
		}

	}
}

