using UnityEngine;
using UnityEngine.Rendering;
using Seb.Helpers;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidRenderUtils
	{
		static Mesh quadMesh;

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

		internal static Mesh GetQuadMesh()
		{
			if (quadMesh == null)
			{
				quadMesh = QuadGenerator.GenerateQuadMesh();
			}

			return quadMesh;
		}

		internal static Matrix4x4 CreateRegionMatrix(ParticleFluidRenderRegion2D region)
		{
			return Matrix4x4.TRS(region.worldBounds.center, Quaternion.identity, region.worldBounds.size);
		}

		internal static Matrix4x4 CreateRegionProjection(ParticleFluidRenderRegion2D region)
		{
			Vector3 min = region.worldBounds.min;
			Vector3 max = region.worldBounds.max;
			float left = min.x;
			float right = max.x;
			float bottom = min.y;
			float top = max.y;
			Matrix4x4 ortho = Matrix4x4.Ortho(left, right, bottom, top, -1f, 1f);
			return GL.GetGPUProjectionMatrix(ortho, false);
		}

		internal static void DrawRegionQuad(CommandBuffer commandBuffer, RenderTargetIdentifier target, Material material, int pass, ParticleFluidRenderRegion2D region, Camera restoreCamera, bool clear = false, Color? clearColor = null)
		{
			commandBuffer.SetRenderTarget(target);
			if (clear)
			{
				commandBuffer.ClearRenderTarget(false, true, clearColor ?? Color.clear);
			}

			commandBuffer.SetViewProjectionMatrices(Matrix4x4.identity, CreateRegionProjection(region));
			commandBuffer.DrawMesh(GetQuadMesh(), CreateRegionMatrix(region), material, 0, pass);
			commandBuffer.SetViewProjectionMatrices(restoreCamera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(restoreCamera.projectionMatrix, false));
		}
	}
}
