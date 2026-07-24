using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Seb.Helpers;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidRenderUtils
	{
		private static readonly int BlurRadius = Shader.PropertyToID("blurRadius");
		private static readonly int BlurDirection = Shader.PropertyToID("blurDirection");
		private static readonly int FluidBlurSource = Shader.PropertyToID("_FluidBlurSource");
		private static readonly int FluidBlurSourceTexelSize = Shader.PropertyToID("_FluidBlurSource_TexelSize");
		private static Mesh _quadMesh;

		private sealed class GaussianBlurPassData
		{
			public TextureHandle source;
			public Material material;
			public Vector2 direction;
			public Vector4 texelSize;
			public float radius;
		}

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
		
		public static void RecordGaussianBlur(RenderGraph renderGraph, string passName, float blurRadius, Material blurMaterial, TextureHandle source, TextureHandle scratch, Vector2Int textureSize)
		{
			if (blurRadius < 0.001f || blurMaterial == null || !source.IsValid() || !scratch.IsValid() || textureSize.x <= 0 || textureSize.y <= 0)
			{
				return;
			}

			Vector4 texelSize = new(1f / textureSize.x, 1f / textureSize.y, textureSize.x, textureSize.y);
			RecordGaussianBlurDirection(renderGraph, $"{passName} Horizontal", source, scratch, blurMaterial, blurRadius, Vector2.right, texelSize);
			RecordGaussianBlurDirection(renderGraph, $"{passName} Vertical", scratch, source, blurMaterial, blurRadius, Vector2.up, texelSize);
		}

		private static void RecordGaussianBlurDirection(RenderGraph renderGraph, string passName, TextureHandle source, TextureHandle destination, Material material, float radius, Vector2 direction, Vector4 texelSize)
		{
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(passName, out GaussianBlurPassData passData);
			passData.source = source;
			passData.material = material;
			passData.direction = direction;
			passData.texelSize = texelSize;
			passData.radius = radius;
			builder.UseTexture(source, AccessFlags.Read);
			builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (GaussianBlurPassData data, RasterGraphContext context) =>
			{
				RasterCommandBuffer cmd = context.cmd;
				cmd.SetGlobalFloat(BlurRadius, data.radius);
				cmd.SetGlobalVector(BlurDirection, data.direction);
				cmd.SetGlobalVector(FluidBlurSourceTexelSize, data.texelSize);
				cmd.SetGlobalTexture(FluidBlurSource, data.source);
				cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
			});
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
