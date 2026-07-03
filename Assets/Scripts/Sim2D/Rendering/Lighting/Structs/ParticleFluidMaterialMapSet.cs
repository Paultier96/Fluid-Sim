using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidMaterialMapSet
	{
		public RenderTexture albedoTexture;
		public RenderTexture normalTexture;
		
		public bool IsAllocated => albedoTexture != null && normalTexture != null;

		public void EnsureRenderTextures(int width, int height, string namePrefix)
		{
			ComputeHelper.CreateRenderTexture(ref albedoTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Albedo");
			ComputeHelper.CreateRenderTexture(ref normalTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal");
		}

		public void Render(CommandBuffer commandBuffer, Material material, int albedoPass, int normalPass)
		{
			if (!IsAllocated || commandBuffer == null || material == null)
			{
				return;
			}

			commandBuffer.BeginSample("Particle Fluid/Build Material Maps");
			material.SetInt("metaballCompositeRegionEnabled", 0);
			material.SetInt("metaballClipRegionEnabled", 0);
			commandBuffer.Blit(null, albedoTexture, material, albedoPass);
			commandBuffer.Blit(null, normalTexture, material, normalPass);
			commandBuffer.EndSample("Particle Fluid/Build Material Maps");
		}

		public void RenderUnlit(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier finalTarget, int unlitPass, ParticleFluidRenderRegion2D renderRegion)
		{
			if (!IsAllocated || commandBuffer == null || material == null)
			{
				return;
			}

			material.SetTexture("MaterialAlbedoTex", albedoTexture);
			material.SetInt("metaballCompositeRegionEnabled", renderRegion.IsCropped ? 1 : 0);
			material.SetVector("metaballCompositeUvRect", renderRegion.SourceUvRect);
			material.SetInt("metaballClipRegionEnabled", 0);
			material.SetVector("metaballClipRect", renderRegion.SourceUvRect);
			commandBuffer.BeginSample("Particle Fluid/Unlit Fallback");
			commandBuffer.Blit(null, finalTarget, material, unlitPass);
			commandBuffer.EndSample("Particle Fluid/Unlit Fallback");
		}

		public void BindTo(ParticleFluidLighting2D lighting, ParticleFluidRenderRegion2D renderRegion)
		{
			if (lighting == null)
			{
				return;
			}
			lighting.materialAlbedoTexture = albedoTexture;
			lighting.materialNormalTexture = normalTexture;
			lighting.materialRenderRegion = renderRegion;
			lighting.BindMaterialTextures();
		}

		public void Release()
		{
			ComputeHelper.Release(albedoTexture, normalTexture);
			albedoTexture = null;
			normalTexture = null;
		}
	}
}
