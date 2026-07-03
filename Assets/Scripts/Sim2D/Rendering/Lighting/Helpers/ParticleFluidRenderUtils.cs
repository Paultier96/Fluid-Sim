using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidRenderUtils
	{
		internal static void EnsureMaterial(ref Material material, Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			DestroyMaterial(ref material);
			material = new Material(shader);
		}

		internal static void DestroyMaterial(ref Material material)
		{
			if (material != null)
			{
				Object.DestroyImmediate(material);
				material = null;
			}
		}
		
		public static void GaussianBlur(CommandBuffer targetCommandBuffer, float causticBlurRadius, Material blurMaterial, RenderTexture source, RenderTexture target)
		{
			blurMaterial.SetFloat("blurRadius", causticBlurRadius);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(source, target, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(target, source, blurMaterial);
		}

		public static void Swap(ref RenderTexture a, ref RenderTexture b)
		{
			(a, b) = (b, a);
		}
	}
}
