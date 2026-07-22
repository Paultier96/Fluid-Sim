using UnityEngine;
using UnityEngine.Rendering;
using Seb.Helpers;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidRenderUtils
	{
		private static readonly int BlurRadius = Shader.PropertyToID("blurRadius");
		private static readonly int BlurDirection = Shader.PropertyToID("blurDirection");
		private static Mesh _quadMesh;

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
		
		public static void GaussianBlur(CommandBuffer targetCommandBuffer, float blurRadius, Material blurMaterial, RenderTexture source, RenderTexture target, string name)
		{
			if (blurRadius < 0.001f)
			{
				return;
			}
			targetCommandBuffer.BeginSample(name);
			targetCommandBuffer.SetGlobalFloat(BlurRadius, blurRadius);
			targetCommandBuffer.SetGlobalVector(BlurDirection, new Vector2(1, 0));
			targetCommandBuffer.Blit(source, target, blurMaterial);
			targetCommandBuffer.SetGlobalVector(BlurDirection, new Vector2(0, 1));
			targetCommandBuffer.Blit(target, source, blurMaterial);
			targetCommandBuffer.EndSample(name);
		}

		public static void Swap(ref RenderTexture a, ref RenderTexture b)
		{
			(a, b) = (b, a);
		}

		internal static Mesh GetQuadMesh()
		{
			if (_quadMesh == null)
			{
				_quadMesh = QuadGenerator.GenerateQuadMesh();
			}

			return _quadMesh;
		}

		internal static void DrawRegionQuad(CommandBuffer commandBuffer, RenderTargetIdentifier target, Material material, int pass, Bounds region, Camera restoreCamera, bool clear = false, Color? clearColor = null)
		{
			commandBuffer.SetRenderTarget(target);
			if (clear)
			{
				commandBuffer.ClearRenderTarget(false, true, clearColor ?? Color.clear);
			}

			commandBuffer.SetViewProjectionMatrices(Matrix4x4.identity, region.CreateRegionProjection());
			commandBuffer.DrawMesh(GetQuadMesh(), region.CreateRegionMatrix(), material, 0, pass);
			commandBuffer.SetViewProjectionMatrices(restoreCamera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(restoreCamera.projectionMatrix, false));
		}

		internal static void DrawRegionQuad(CommandBuffer commandBuffer, RenderTargetIdentifier[] targets, Material material, int pass, Bounds region, Camera restoreCamera, bool clear = false, Color? clearColor = null)
		{
			commandBuffer.SetRenderTarget(targets, BuiltinRenderTextureType.None);
			if (clear)
			{
				commandBuffer.ClearRenderTarget(false, true, clearColor ?? Color.clear);
			}

			commandBuffer.SetViewProjectionMatrices(Matrix4x4.identity, region.CreateRegionProjection());
			commandBuffer.DrawMesh(GetQuadMesh(), region.CreateRegionMatrix(), material, 0, pass);
			commandBuffer.SetViewProjectionMatrices(restoreCamera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(restoreCamera.projectionMatrix, false));
		}
	}
}
