using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballMaterialRenderer2D
	{
		private static readonly int AnalyticBoundaryExpansion = Shader.PropertyToID("analyticBoundaryExpansion");
		private static readonly int CombinedTex = Shader.PropertyToID("CombinedTex");
		private static readonly int NormalTex = Shader.PropertyToID("NormalTex");
		private static readonly int MaterialAlbedoTex = Shader.PropertyToID("MaterialAlbedoTex");
		private const int MaterialMapsPass = 0;
		private const int UnlitPass = 1;

		private Material _material;
		private readonly RenderTargetIdentifier[] _materialMapTargets = new RenderTargetIdentifier[3];
		public readonly ParticleFluidMaterialMapSet materialMaps = new ();

		public void EnsureMaterial(Shader shader)
		{
			if (_material != null && _material.shader == shader)
			{
				return;
			}

			if (_material != null)
			{
				Object.DestroyImmediate(_material);
			}

			_material = new Material(shader);
		}

		public void SetSourceTextures(Texture combinedTexture, Texture normalTexture)
		{
			_material.SetTexture(CombinedTex, combinedTexture != null ? combinedTexture : Texture2D.blackTexture);
			_material.SetTexture(NormalTex, normalTexture != null ? normalTexture : Texture2D.blackTexture);
		}


		public void RenderMaterialMaps(CommandBuffer commandBuffer, Bounds materialRegion, Camera camera)
		{
			if (!materialMaps.IsAllocated || commandBuffer == null || _material == null || camera == null)
			{
				return;
			}

			_materialMapTargets[0] = materialMaps.albedoTexture;
			_materialMapTargets[1] = materialMaps.normalTexture;
			_materialMapTargets[2] = materialMaps.transportTexture;

			commandBuffer.BeginSample("Particle Fluid/Build Material Maps");
			ParticleFluidRenderUtils.DrawRegionQuad(commandBuffer, _materialMapTargets, _material, MaterialMapsPass, materialRegion, camera, true, Color.clear);
			commandBuffer.EndSample("Particle Fluid/Build Material Maps");
		}

		public void RenderUnlit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Bounds region)
		{
			if (!materialMaps.IsAllocated || commandBuffer == null || _material == null)
			{
				return;
			}

			_material.SetTexture(MaterialAlbedoTex, materialMaps.albedoTexture);
			commandBuffer.BeginSample("Particle Fluid/Unlit Fallback");
			commandBuffer.SetRenderTarget(finalTarget);
			commandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), region.CreateRegionMatrix(), _material, 0, UnlitPass);
			commandBuffer.EndSample("Particle Fluid/Unlit Fallback");
		}

		public void Release()
		{
			materialMaps.Release();
			if (_material != null)
			{
				Object.DestroyImmediate(_material);
				_material = null;
			}
		}
	}
}
