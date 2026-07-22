using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidMaterialMapSet
	{
		public RenderTexture albedoTexture;
		public RenderTexture normalTexture;
		public RenderTexture transportTexture;

		private static readonly GraphicsFormat TransportFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16_SFloat, GraphicsFormatUsage.Render)
			? GraphicsFormat.R16G16B16_SFloat
			: GraphicsFormat.R16G16B16A16_SFloat;
		
		public bool IsAllocated => albedoTexture != null && normalTexture != null && transportTexture != null;

		public void EnsureRenderTextures(Vector2Int materialSize, string namePrefix)
		{
			ComputeHelper.CreateRenderTexture(ref albedoTexture, materialSize, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Albedo");
			ComputeHelper.CreateRenderTexture(ref normalTexture, materialSize, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, $"{namePrefix} Material Normal");
			ComputeHelper.CreateRenderTexture(ref transportTexture, materialSize, FilterMode.Bilinear, TransportFormat, $"{namePrefix} Material Transport");
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
