using Seb.Helpers;
using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidGaussianSss : MonoBehaviour
	{
		private static readonly int SharpCausticsTex = Shader.PropertyToID("SharpCausticsTex");
		private static readonly int MaterialTransportTex = Shader.PropertyToID("MaterialTransportTex");
		private static readonly int SoftLightSize = Shader.PropertyToID("softLightSize");
		private static readonly int ScatterStrengthA = Shader.PropertyToID("scatterStrengthA");
		private static readonly int LightIntensity = Shader.PropertyToID("lightIntensity");
		private static readonly int MaskInputToPhase0 = Shader.PropertyToID("maskInputToPhase0");

		[Header("Shaders")]
		public Shader phaseDiffuseLightInitShader;
		public Shader gaussianDiffuseBlurShader;

		[Header("Subsurface Scattering")]
		[Min(0f)] public float gaussianDiffuseScatterStrength = 0.33f;
		[Min(0f)] public float gaussianDiffuseRadius = 50f;
		[Range(0.01f, 1f)] public float gaussianDiffuseTextureScale = 0.25f;
		[Tooltip("When enabled, only phase 0 seeds the Gaussian blur. Disable to blur the full sharp caustic texture before the final phase mask.")]
		public bool gaussianDiffuseMaskInputToPhase0 = true;

		private Material _phaseDiffuseLightInitMaterial;
		private Material _gaussianDiffuseBlurMaterial;
		internal RenderTexture gaussianSoftLightTexture0;
		internal RenderTexture gaussianSoftLightTexture1;

		private void OnEnable()
		{
			EnsureMaterials();
		}

		private void OnDisable()
		{
			Release();
		}

		internal bool ShouldRender => isActiveAndEnabled && gaussianDiffuseScatterStrength > 0f && gaussianDiffuseRadius > 0f;

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _phaseDiffuseLightInitMaterial, phaseDiffuseLightInitShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _gaussianDiffuseBlurMaterial, gaussianDiffuseBlurShader);
		}

		internal void EnsureResources(Vector2Int causticSize)
		{
			if (ShouldRender)
			{
				int width = Mathf.Max(1, Mathf.RoundToInt(causticSize.x * gaussianDiffuseTextureScale));
				int height = Mathf.Max(1, Mathf.RoundToInt(causticSize.y * gaussianDiffuseTextureScale));
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 0");
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 1");
				return;
			}
			
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1);
		}

		internal void RecordRenderGraph(
			RenderGraph renderGraph,
			ParticleFluidLighting2D.FrameContext context,
			LightingInputHandles inputs,
			LightingResourceHandles resources,
			Texture sharpCaustics,
			Texture transportTexture,
			float directLightTextureScale)
		{
			using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Gaussian SSS", out GaussianSssPassData passData);
			passData.context = context;
			passData.sharpCaustics = sharpCaustics != null ? sharpCaustics : Texture2D.blackTexture;
			passData.transportTexture = transportTexture != null ? transportTexture : Texture2D.blackTexture;
			passData.directLightTextureScale = directLightTextureScale;
			UseIfValid(builder, inputs.transport, AccessFlags.Read);
			UseIfValid(builder, resources.causticResolved, AccessFlags.Read);
			UseIfValid(builder, resources.causticTemporal, AccessFlags.Read);
			UseIfValid(builder, resources.gaussianSoftLight0, AccessFlags.ReadWrite);
			UseIfValid(builder, resources.gaussianSoftLight1, AccessFlags.ReadWrite);
			builder.AllowPassCulling(false);
			builder.SetRenderFunc((GaussianSssPassData data, UnsafeGraphContext context) =>
			{
				CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
				MetaballRenderer2D metaballSettings = data.context.display.metaballs;
				nativeCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
				if (_phaseDiffuseLightInitMaterial == null || _gaussianDiffuseBlurMaterial == null || gaussianSoftLightTexture0 == null || gaussianSoftLightTexture1 == null)
				{
					nativeCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
					return;
				}
				ParticleFluidRenderBindings.ApplyPhaseSplitGlobals(nativeCommandBuffer, metaballSettings);
				ParticleFluidRenderBindings.ApplyLayoutGlobals(nativeCommandBuffer, data.context.display.sim.analyticBoundary, data.context.renderRegion);

				_phaseDiffuseLightInitMaterial.SetTexture(SharpCausticsTex, data.sharpCaustics);
				_phaseDiffuseLightInitMaterial.SetTexture(MaterialTransportTex, data.transportTexture != null ? data.transportTexture : Texture2D.blackTexture);
				_phaseDiffuseLightInitMaterial.SetVector(SoftLightSize, new Vector4(gaussianSoftLightTexture0.width, gaussianSoftLightTexture0.height));
				_phaseDiffuseLightInitMaterial.SetFloat(ScatterStrengthA, gaussianDiffuseScatterStrength);
				_phaseDiffuseLightInitMaterial.SetFloat(LightIntensity, 1f);
				_phaseDiffuseLightInitMaterial.SetInt(MaskInputToPhase0, gaussianDiffuseMaskInputToPhase0 ? 1 : 0);
				ParticleFluidRenderUtils.DrawRegionQuad(nativeCommandBuffer, gaussianSoftLightTexture0, _phaseDiffuseLightInitMaterial, 0, data.context.renderRegion, data.context.cam, true, Color.clear);

				float blurRadius = gaussianDiffuseRadius * metaballSettings.renderTextureScale * data.directLightTextureScale * gaussianDiffuseTextureScale;
				ParticleFluidRenderUtils.GaussianBlur(nativeCommandBuffer, blurRadius, _gaussianDiffuseBlurMaterial,gaussianSoftLightTexture0,gaussianSoftLightTexture1, "Metaballs/Phase Diffuse Light");
			});
		}

		internal Texture GetOutputTexture()
		{
			return gaussianSoftLightTexture0 != null ? gaussianSoftLightTexture0 : Texture2D.blackTexture;
		}

		internal void Release()
		{
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1);
			gaussianSoftLightTexture0 = null;
			gaussianSoftLightTexture1 = null;
			ParticleFluidRenderUtils.DestroyMaterial(ref _phaseDiffuseLightInitMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _gaussianDiffuseBlurMaterial);
		}

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		private class GaussianSssPassData
		{
			public ParticleFluidLighting2D.FrameContext context;
			public Texture sharpCaustics;
			public Texture transportTexture;
			public float directLightTextureScale;
		}
	}
}
