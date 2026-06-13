using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidMaterialMapSet
	{
		RenderTexture albedoTexture;
		RenderTexture normal0Texture;
		RenderTexture normal1Texture;

		public RenderTexture AlbedoRenderTexture => albedoTexture;
		public RenderTexture Normal0RenderTexture => normal0Texture;
		public RenderTexture Normal1RenderTexture => normal1Texture;
		public Texture AlbedoTexture => albedoTexture != null ? (Texture)albedoTexture : Texture2D.blackTexture;
		public Texture Normal0Texture => normal0Texture != null ? (Texture)normal0Texture : Texture2D.blackTexture;
		public Texture Normal1Texture => normal1Texture != null ? (Texture)normal1Texture : Texture2D.blackTexture;
		public bool IsAllocated => albedoTexture != null && normal0Texture != null && normal1Texture != null;

		public void EnsureRenderTextures(int width, int height, string namePrefix)
		{
			ComputeHelper.CreateRenderTexture(ref albedoTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Albedo");
			ComputeHelper.CreateRenderTexture(ref normal0Texture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal 0");
			ComputeHelper.CreateRenderTexture(ref normal1Texture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal 1");
		}

		public void Render(CommandBuffer commandBuffer, Material material, int albedoPass, int normal0Pass, int normal1Pass)
		{
			if (!IsAllocated || commandBuffer == null || material == null)
			{
				return;
			}

			commandBuffer.BeginSample("Particle Fluid/Build Material Maps");
			commandBuffer.Blit(null, albedoTexture, material, albedoPass);
			commandBuffer.Blit(null, normal0Texture, material, normal0Pass);
			commandBuffer.Blit(null, normal1Texture, material, normal1Pass);
			commandBuffer.EndSample("Particle Fluid/Build Material Maps");
		}

		public void RenderUnlit(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier finalTarget, int unlitPass)
		{
			if (!IsAllocated || commandBuffer == null || material == null)
			{
				return;
			}

			material.SetTexture("MaterialAlbedoTex", albedoTexture);
			commandBuffer.BeginSample("Particle Fluid/Unlit Fallback");
			commandBuffer.Blit(null, finalTarget, material, unlitPass);
			commandBuffer.EndSample("Particle Fluid/Unlit Fallback");
		}

		public void BindTo(ParticleFluidLighting2D lighting)
		{
			if (lighting == null)
			{
				return;
			}

			lighting.SetMaterialTextures(albedoTexture, normal0Texture, normal1Texture);
		}

		public void Release()
		{
			ComputeHelper.Release(albedoTexture, normal0Texture, normal1Texture);
			albedoTexture = null;
			normal0Texture = null;
			normal1Texture = null;
		}
	}
}
