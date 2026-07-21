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
		static readonly GraphicsFormat TransportFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16_SFloat, GraphicsFormatUsage.Render)
			? GraphicsFormat.R16G16B16_SFloat
			: GraphicsFormat.R16G16B16A16_SFloat;
		
		public bool IsAllocated => albedoTexture != null && normalTexture != null && transportTexture != null;

		public void EnsureRenderTextures(Vector2Int materialSize, string namePrefix)
		{
			ComputeHelper.CreateRenderTexture(ref albedoTexture, materialSize.x, materialSize.y, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Albedo");
			ComputeHelper.CreateRenderTexture(ref normalTexture, materialSize.x, materialSize.y, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal");
			ComputeHelper.CreateRenderTexture(ref transportTexture, materialSize.x, materialSize.y, FilterMode.Bilinear, TransportFormat, $"{namePrefix} Material Transport");
		}

		public void RenderMaterialMaps(CommandBuffer commandBuffer, Material material, int materialMapsPass, Bounds materialRegion, Camera camera)
		{
			if (!IsAllocated || commandBuffer == null || material == null || camera == null)
			{
				return;
			}

			RenderTargetIdentifier[] targets =
			{
				albedoTexture,
				normalTexture,
				transportTexture
			};
			commandBuffer.BeginSample("Particle Fluid/Build Material Maps");
			ParticleFluidRenderUtils.DrawRegionQuad(commandBuffer, targets, material, materialMapsPass, materialRegion, camera, true, Color.clear);
			commandBuffer.EndSample("Particle Fluid/Build Material Maps");
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

		public ParticleFluidLightingInputSet CreateLightingInputs(Bounds renderRegion, Vector2Int materialSize, Texture velocityTexture = null)
		{
			return new ParticleFluidLightingInputSet(albedoTexture, normalTexture, transportTexture, renderRegion, materialSize, velocityTexture);
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

