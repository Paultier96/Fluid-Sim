using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballMaterialRenderer2D
	{
		const int MaterialMapsPass = 0;
		const int UnlitPass = 1;

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

		public void EnsureRenderTextures(Vector2Int materialSize, Vector2Int transportSize)
		{
			materialMaps.EnsureRenderTextures(materialSize.x, materialSize.y, transportSize.x, transportSize.y, "Particle2D");
		}

		public void ApplySharedSettings(float analyticBoundaryExpansion)
		{
			if (material == null)
			{
				return;
			}

			material.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
		}

		public void SetSourceTextures(Texture combinedTexture, Texture normalTexture)
		{
			if (material == null)
			{
				return;
			}

			material.SetTexture("CombinedTex", combinedTexture != null ? combinedTexture : Texture2D.blackTexture);
			material.SetTexture("NormalTex", normalTexture != null ? normalTexture : Texture2D.blackTexture);
		}


		public void RenderMaterialMaps(CommandBuffer commandBuffer, Bounds materialRegion, Camera camera)
		{
			if (!IsReady || commandBuffer == null || camera == null)
			{
				return;
			}
			materialMaps.RenderMaterialMaps(commandBuffer, material, MaterialMapsPass, materialRegion, camera);
		}

		public void RenderUnlit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Bounds region)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}
			materialMaps.RenderUnlit(commandBuffer, material, finalTarget, UnlitPass, region);
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

