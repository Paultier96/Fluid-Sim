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
		public RenderTexture transportTexture;
		
		public bool IsAllocated => albedoTexture != null && normalTexture != null && transportTexture != null;

		public void EnsureRenderTextures(int materialWidth, int materialHeight, int transportWidth, int transportHeight, string namePrefix)
		{
			ComputeHelper.CreateRenderTexture(ref albedoTexture, materialWidth, materialHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Albedo");
			ComputeHelper.CreateRenderTexture(ref normalTexture, materialWidth, materialHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal");
			ComputeHelper.CreateRenderTexture(ref transportTexture, transportWidth, transportHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Transport");
		}

		public void RenderSurfaceMaps(CommandBuffer commandBuffer, Material material, int albedoPass, int normalPass, Bounds materialRegion, Camera camera)
		{
			if (!IsAllocated || commandBuffer == null || material == null || camera == null)
			{
				return;
			}

			commandBuffer.BeginSample("Particle Fluid/Build Surface Maps");
			ParticleFluidRenderUtils.DrawRegionQuad(commandBuffer, albedoTexture, material, albedoPass, materialRegion, camera, true, Color.clear);
			ParticleFluidRenderUtils.DrawRegionQuad(commandBuffer, normalTexture, material, normalPass, materialRegion, camera, true, Color.clear);
			commandBuffer.EndSample("Particle Fluid/Build Surface Maps");
		}

		public void RenderTransportMap(CommandBuffer commandBuffer, Material material, int transportPass, Bounds transportRegion, Camera camera)
		{
			if (!IsAllocated || commandBuffer == null || material == null || camera == null)
			{
				return;
			}

			commandBuffer.BeginSample("Particle Fluid/Build Transport Map");
			ParticleFluidRenderUtils.DrawRegionQuad(commandBuffer, transportTexture, material, transportPass, transportRegion, camera, true, Color.clear);
			commandBuffer.EndSample("Particle Fluid/Build Transport Map");
		}

		public void RenderUnlit(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier finalTarget, int unlitPass, Bounds region)
		{
			if (!IsAllocated || commandBuffer == null || material == null)
			{
				return;
			}

			material.SetTexture("MaterialAlbedoTex", albedoTexture);
			commandBuffer.BeginSample("Particle Fluid/Unlit Fallback");
			commandBuffer.SetRenderTarget(finalTarget);
			commandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), region.CreateRegionMatrix(), material, 0, unlitPass);
			commandBuffer.EndSample("Particle Fluid/Unlit Fallback");
		}

		public ParticleFluidLightingInputSet CreateLightingInputs(Bounds renderRegion, Vector2Int sourceSize, Texture velocityPhase0Texture = null, Texture velocityPhase1Texture = null)
		{
			return new ParticleFluidLightingInputSet(albedoTexture, normalTexture, transportTexture, renderRegion, sourceSize, velocityPhase0Texture, velocityPhase1Texture);
		}

		public void Release()
		{
			ComputeHelper.Release(albedoTexture, normalTexture, transportTexture);
			albedoTexture = null;
			normalTexture = null;
			transportTexture = null;
		}
	}
}

