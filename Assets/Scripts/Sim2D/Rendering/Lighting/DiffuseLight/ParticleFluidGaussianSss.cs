using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidGaussianSss : MonoBehaviour
	{
		[Header("Shaders")]
		public Shader phaseDiffuseLightInitShader;
		public Shader gaussianDiffuseBlurShader;

		[Header("Subsurface Scattering")]
		public bool gaussianDiffuseEnabled = true;
		[Min(0f)] public float gaussianDiffuseScatterStrength = 0.33f;
		[Min(0f)] public float gaussianDiffuseRadius = 50f;
		[Range(0.01f, 1f)] public float gaussianDiffuseTextureScale = 0.25f;

		private Material _phaseDiffuseLightInitMaterial;
		private Material _gaussianDiffuseBlurMaterial;
		internal RenderTexture gaussianSoftLightTexture0;
		internal RenderTexture gaussianSoftLightTexture1;
		private RenderTexture _gaussianSoftLightInitTexture;
		internal Texture currentInitTexture;

		void Awake()
		{
			currentInitTexture = Texture2D.blackTexture;
		}

		internal void ApplyPhaseLookPreset(ParticleFluidPhaseLookPreset preset)
		{
			if (preset == null)
			{
				return;
			}

			gaussianDiffuseScatterStrength = preset.gaussianDiffuseScatterStrength;
			gaussianDiffuseRadius = preset.gaussianDiffuseRadius;
		}

		internal bool ShouldRender()
		{
			return gaussianDiffuseEnabled && gaussianDiffuseScatterStrength > 0f && gaussianDiffuseRadius > 0f;
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _phaseDiffuseLightInitMaterial, phaseDiffuseLightInitShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _gaussianDiffuseBlurMaterial, gaussianDiffuseBlurShader);
		}

		internal void EnsureResources(Vector2Int causticSize, bool renderGaussian)
		{
			if (renderGaussian)
			{
				int width = Mathf.Max(1, Mathf.RoundToInt(causticSize.x * gaussianDiffuseTextureScale));
				int height = Mathf.Max(1, Mathf.RoundToInt(causticSize.y * gaussianDiffuseTextureScale));
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 0");
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 1");
				ComputeHelper.CreateRenderTexture(ref _gaussianSoftLightInitTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light Init");
				return;
			}
			
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1, _gaussianSoftLightInitTexture);
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, Texture transportTexture, float directLightTextureScale)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			if (_phaseDiffuseLightInitMaterial == null || _gaussianDiffuseBlurMaterial == null || gaussianSoftLightTexture0 == null || gaussianSoftLightTexture1 == null)
			{
				currentInitTexture = Texture2D.blackTexture;
				targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
				return Texture2D.blackTexture;
			}
			ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, context.display.sim.analyticBoundary);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, surface);
			ParticleFluidRasterLayoutBindings.ApplySoftLightGlobals(targetCommandBuffer, context.renderLayout.DomainRegion.WorldCenter, context.renderLayout.DomainRegion.WorldSize);
			_phaseDiffuseLightInitMaterial.SetTexture("SharpCausticsTex", sharpCaustics);
			_phaseDiffuseLightInitMaterial.SetTexture("MaterialTransportTex", transportTexture != null ? transportTexture : Texture2D.blackTexture);
			_phaseDiffuseLightInitMaterial.SetVector("softLightSize", new Vector4(gaussianSoftLightTexture0.width, gaussianSoftLightTexture0.height));
			_phaseDiffuseLightInitMaterial.SetFloat("scatterStrengthA", gaussianDiffuseScatterStrength);
			_phaseDiffuseLightInitMaterial.SetFloat("lightIntensity", 1f);
			ParticleFluidRenderUtils.DrawRegionQuad(targetCommandBuffer, gaussianSoftLightTexture0, _phaseDiffuseLightInitMaterial, 0, context.renderLayout.CausticRegion, context.cam, true, Color.clear);
			if (_gaussianSoftLightInitTexture != null)
			{
				targetCommandBuffer.Blit(gaussianSoftLightTexture0, _gaussianSoftLightInitTexture);
			}

			float gaussianRadiusScale = surface.renderTextureScale * directLightTextureScale * gaussianDiffuseTextureScale;
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer,(gaussianDiffuseRadius * gaussianRadiusScale),_gaussianDiffuseBlurMaterial,gaussianSoftLightTexture0,gaussianSoftLightTexture1);
			currentInitTexture = _gaussianSoftLightInitTexture != null ? _gaussianSoftLightInitTexture : Texture2D.blackTexture;
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return gaussianSoftLightTexture0;
		}


		internal void Release()
		{
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1, _gaussianSoftLightInitTexture);
			currentInitTexture = Texture2D.blackTexture;
			ParticleFluidRenderUtils.DestroyMaterial(ref _phaseDiffuseLightInitMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _gaussianDiffuseBlurMaterial);
		}
	}
}
