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
				return;
			}
			
			ComputeHelper.Release(gaussianSoftLightTexture0);
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
			if (_phaseDiffuseLightInitMaterial == null || _gaussianDiffuseBlurMaterial == null || gaussianSoftLightTexture0 == null ||
			    !resources.gaussianSoftLight0.IsValid() || !resources.gaussianSoftLight1.IsValid())
			{
				return;
			}

			Vector2Int textureSize = new(gaussianSoftLightTexture0.width, gaussianSoftLightTexture0.height);
			using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Gaussian SSS Seed", out GaussianSssPassData passData))
			{
				passData.context = context;
				passData.sharpCaustics = sharpCaustics != null ? sharpCaustics : Texture2D.blackTexture;
				passData.transportTexture = transportTexture != null ? transportTexture : Texture2D.blackTexture;
				passData.material = _phaseDiffuseLightInitMaterial;
				passData.textureSize = textureSize;
				passData.scatterStrength = gaussianDiffuseScatterStrength;
				passData.maskInputToPhase0 = gaussianDiffuseMaskInputToPhase0;
				UseIfValid(builder, inputs.transport, AccessFlags.Read);
				UseIfValid(builder, resources.selectedCaustics, AccessFlags.Read);
				builder.SetRenderAttachment(resources.gaussianSoftLight0, 0, AccessFlags.WriteAll);
				builder.AllowGlobalStateModification(true);
				builder.SetRenderFunc(static (GaussianSssPassData data, RasterGraphContext graphContext) =>
				{
					RasterCommandBuffer cmd = graphContext.cmd;
					MetaballRenderer2D metaballSettings = data.context.display.metaballs;
					ParticleFluidRenderBindings.ApplyPhaseSplitGlobals(cmd, metaballSettings);
					ParticleFluidRenderBindings.ApplyLayoutGlobals(cmd, data.context.display.sim.analyticBoundary, data.context.renderRegion);
					data.material.SetTexture(SharpCausticsTex, data.sharpCaustics);
					data.material.SetTexture(MaterialTransportTex, data.transportTexture);
					data.material.SetVector(SoftLightSize, new Vector4(data.textureSize.x, data.textureSize.y));
					data.material.SetFloat(ScatterStrengthA, data.scatterStrength);
					data.material.SetFloat(LightIntensity, 1f);
					data.material.SetInt(MaskInputToPhase0, data.maskInputToPhase0 ? 1 : 0);
					cmd.SetViewProjectionMatrices(Matrix4x4.identity, data.context.renderRegion.CreateRegionProjection());
					cmd.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), data.context.renderRegion.CreateRegionMatrix(), data.material, 0, 0);
					cmd.SetViewProjectionMatrices(data.context.cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(data.context.cam.projectionMatrix, false));
				});
			}

			float blurRadius = gaussianDiffuseRadius * context.display.metaballs.renderTextureScale * directLightTextureScale * gaussianDiffuseTextureScale;
			ParticleFluidRenderUtils.RecordGaussianBlur(renderGraph, "Gaussian SSS Blur", blurRadius, _gaussianDiffuseBlurMaterial, resources.gaussianSoftLight0, resources.gaussianSoftLight1, textureSize);
		}

		internal Texture GetOutputTexture()
		{
			return gaussianSoftLightTexture0 != null ? gaussianSoftLightTexture0 : Texture2D.blackTexture;
		}

		internal void Release()
		{
			ComputeHelper.Release(gaussianSoftLightTexture0);
			gaussianSoftLightTexture0 = null;
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
			public Material material;
			public Vector2Int textureSize;
			public float scatterStrength;
			public bool maskInputToPhase0;
		}
	}
}
