using Seb.Fluid2D.Simulation;
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
		public bool gaussianDiffuseEnabled = false;
		[Min(0f)] public float gaussianDiffuseScatterStrength = 0f;
		[Min(0f)] public float gaussianDiffuseRadius = 24f;
		[Range(0.01f, 1f)] public float gaussianDiffuseTextureScale = 0.5f;

		internal Material phaseDiffuseLightInitMaterial;
		internal Material gaussianDiffuseBlurMaterial;
		internal RenderTexture gaussianSoftLightTexture0;
		internal RenderTexture gaussianSoftLightTexture1;
		internal RenderTexture gaussianSoftLightInitTexture;
		internal Texture CurrentInitTexture;
		internal Texture CurrentBlurTexture;

		void Awake()
		{
			ResetDebugOutputs();
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
			ParticleFluidRenderUtils.EnsureMaterial(ref phaseDiffuseLightInitMaterial, phaseDiffuseLightInitShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref gaussianDiffuseBlurMaterial, gaussianDiffuseBlurShader);
		}

		internal void EnsureResources(ParticleFluidRenderRegion2D causticRegion, bool renderGaussian)
		{
			if (renderGaussian)
			{
				int width = Mathf.Max(1, Mathf.RoundToInt(causticRegion.PixelWidth * gaussianDiffuseTextureScale));
				int height = Mathf.Max(1, Mathf.RoundToInt(causticRegion.PixelHeight * gaussianDiffuseTextureScale));
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture0, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 0");
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightTexture1, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light 1");
				ComputeHelper.CreateRenderTexture(ref gaussianSoftLightInitTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Gaussian Soft Light Init");
				return;
			}

			ComputeHelper.Release(gaussianSoftLightTexture0, gaussianSoftLightTexture1, gaussianSoftLightInitTexture);
		}

		internal void ResetDebugOutputs()
		{
			CurrentInitTexture = Texture2D.blackTexture;
			CurrentBlurTexture = Texture2D.blackTexture;
		}

		internal Texture Render(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, RenderTexture combinedAccumulationTexture, float directLightTextureScale)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			if (phaseDiffuseLightInitMaterial == null || gaussianDiffuseBlurMaterial == null || gaussianSoftLightTexture0 == null || gaussianSoftLightTexture1 == null)
			{
				CurrentInitTexture = Texture2D.blackTexture;
				CurrentBlurTexture = Texture2D.blackTexture;
				targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
				return Texture2D.blackTexture;
			}
			ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, context.display.sim.analyticBoundary);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, surface);
			ParticleFluidRasterLayoutBindings.ApplySoftLightGlobals(targetCommandBuffer, context.renderLayout.Caustic, new Vector4(0f, 0f, 1f, 1f));
			phaseDiffuseLightInitMaterial.SetTexture("SharpCausticsTex", sharpCaustics);
			phaseDiffuseLightInitMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			phaseDiffuseLightInitMaterial.SetVector("softLightSize", new Vector4(gaussianSoftLightTexture0.width, gaussianSoftLightTexture0.height));
			phaseDiffuseLightInitMaterial.SetFloat("scatterStrengthA", gaussianDiffuseScatterStrength);
			phaseDiffuseLightInitMaterial.SetFloat("lightIntensity", 1f);
			targetCommandBuffer.Blit(null, gaussianSoftLightTexture0, phaseDiffuseLightInitMaterial, 0);
			if (gaussianSoftLightInitTexture != null)
			{
				targetCommandBuffer.Blit(gaussianSoftLightTexture0, gaussianSoftLightInitTexture);
			}

			RenderTexture source = gaussianSoftLightTexture0;
			RenderTexture target = gaussianSoftLightTexture1;
			float gaussianRadiusScale = surface.renderTextureScale * directLightTextureScale * Mathf.Max(gaussianDiffuseTextureScale, 0.0001f);
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer,(gaussianDiffuseRadius * gaussianRadiusScale),gaussianDiffuseBlurMaterial,source,target);
			CurrentInitTexture = gaussianSoftLightInitTexture != null ? gaussianSoftLightInitTexture : Texture2D.blackTexture;
			CurrentBlurTexture = source;
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return source;
		}


		internal void Release()
		{
			EnsureResources(default, false);
			ResetDebugOutputs();
			ParticleFluidRenderUtils.DestroyMaterial(ref phaseDiffuseLightInitMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref gaussianDiffuseBlurMaterial);
		}
	}
}
