using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballMaterialRenderer2D
	{
		const int AlbedoPass = 0;
		const int NormalPass = 1;
		const int UnlitPass = 2;

		Material material;
		readonly ParticleFluidMaterialMapSet materialMaps = new ();

		public ParticleFluidMaterialMapSet MaterialMaps => materialMaps;
		public bool IsReady => material != null && materialMaps.IsAllocated;

		public void EnsureMaterial(Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			if (material != null)
			{
				Object.DestroyImmediate(material);
			}

			material = new Material(shader);
		}

		public void EnsureRenderTextures(ParticleFluidRenderRegion2D renderRegion)
		{
			materialMaps.EnsureRenderTextures(renderRegion.PixelWidth, renderRegion.PixelHeight, "Particle2D");
		}

		public void ApplySettings(
			ParticleDisplay2D display,
			Camera cam,
			RenderTexture combinedTexture,
			RenderTexture normalTexture,
			ParticleFluidRenderRegion2D renderRegion,
			float analyticBoundaryExpansion,
			float effectiveNormalStrength,
			ParticleFluidLighting2D lighting)
		{
			if (material == null)
			{
				return;
			}

			material.SetTexture("CombinedTex", combinedTexture);
			material.SetTexture("NormalTex", normalTexture);
			material.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
		}

		public void Render(CommandBuffer commandBuffer)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}
			materialMaps.Render(commandBuffer, material, AlbedoPass, NormalPass);
		}

		public void RenderUnlit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, ParticleFluidRenderRegion2D renderRegion)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}
			materialMaps.RenderUnlit(commandBuffer, material, finalTarget, UnlitPass, renderRegion);
		}

		public void Release()
		{
			materialMaps.Release();
			if (material != null)
			{
				Object.DestroyImmediate(material);
				material = null;
			}
		}
	}
}
