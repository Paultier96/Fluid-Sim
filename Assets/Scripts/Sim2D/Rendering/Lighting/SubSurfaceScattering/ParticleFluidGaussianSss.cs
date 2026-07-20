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
		[Min(0f)] public float gaussianDiffuseScatterStrength = 0.33f;
		[Min(0f)] public float gaussianDiffuseRadius = 50f;
		[Range(0.01f, 1f)] public float gaussianDiffuseTextureScale = 0.25f;
		[Tooltip("When enabled, only phase 0 seeds the Gaussian blur. Disable to blur the full sharp caustic texture before the final phase mask.")]
		public bool gaussianDiffuseMaskInputToPhase0 = true;

		private Material _phaseDiffuseLightInitMaterial;
		private Material _gaussianDiffuseBlurMaterial;
		internal RenderTexture gaussianSoftLightTexture0;
		internal RenderTexture gaussianSoftLightTexture1;

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
			return isActiveAndEnabled && gaussianDiffuseScatterStrength > 0f && gaussianDiffuseRadius > 0f;
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
				return;
			}
			
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1);
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, Texture transportTexture, float directLightTextureScale)
		{
			ParticleDisplay2D.MetaballSettings metaballSettings = context.display.metaballs;
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			if (_phaseDiffuseLightInitMaterial == null || _gaussianDiffuseBlurMaterial == null || gaussianSoftLightTexture0 == null || gaussianSoftLightTexture1 == null)
			{
				targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
				return Texture2D.blackTexture;
			}
			ParticleFluidLayoutBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, metaballSettings);
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(targetCommandBuffer, context.display.sim.analyticBoundary, context.renderRegion);

			_phaseDiffuseLightInitMaterial.SetTexture("SharpCausticsTex", sharpCaustics);
			_phaseDiffuseLightInitMaterial.SetTexture("MaterialTransportTex", transportTexture != null ? transportTexture : Texture2D.blackTexture);
			_phaseDiffuseLightInitMaterial.SetVector("softLightSize", new Vector4(gaussianSoftLightTexture0.width, gaussianSoftLightTexture0.height));
			_phaseDiffuseLightInitMaterial.SetFloat("scatterStrengthA", gaussianDiffuseScatterStrength);
			_phaseDiffuseLightInitMaterial.SetFloat("lightIntensity", 1f);
			_phaseDiffuseLightInitMaterial.SetInt("maskInputToPhase0", gaussianDiffuseMaskInputToPhase0 ? 1 : 0);
			ParticleFluidRenderUtils.DrawRegionQuad(targetCommandBuffer, gaussianSoftLightTexture0, _phaseDiffuseLightInitMaterial, 0, context.renderRegion, context.cam, true, Color.clear);

			float gaussianRadiusScale = metaballSettings.renderTextureScale * directLightTextureScale * gaussianDiffuseTextureScale;
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer,(gaussianDiffuseRadius * gaussianRadiusScale),_gaussianDiffuseBlurMaterial,gaussianSoftLightTexture0,gaussianSoftLightTexture1);
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return gaussianSoftLightTexture0;
		}


		internal void Release()
		{
			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1);
			ParticleFluidRenderUtils.DestroyMaterial(ref _phaseDiffuseLightInitMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _gaussianDiffuseBlurMaterial);
		}
	}
}