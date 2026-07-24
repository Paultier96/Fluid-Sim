using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballMaterialRenderer2D
	{
		private static readonly int CombinedTex = Shader.PropertyToID("CombinedTex");
		private static readonly int NormalTex = Shader.PropertyToID("NormalTex");
		private static readonly int MaterialAlbedoTex = Shader.PropertyToID("MaterialAlbedoTex");
		private const int MaterialMapsPass = 0;
		private const int UnlitPass = 1;

		private Material _material;
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


		public void RecordRenderGraph(
			RenderGraph renderGraph,
			TextureHandle combinedHandle,
			TextureHandle normalHandle,
			TextureHandle gradientAtlasHandle,
			TextureHandle materialAlbedoHandle,
			TextureHandle materialNormalHandle,
			TextureHandle materialTransportHandle,
			ParticleFluidLighting2D.FrameContext frameContext,
			ParticleFluidLighting2D lighting,
			Texture combinedTexture,
			Texture normalTexture)
		{
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Material Maps", out MaterialMapPassData passData);
			passData.lighting = lighting;
			passData.frameContext = frameContext;
			passData.material = _material;
			passData.combinedTexture = combinedTexture;
			passData.normalTexture = normalTexture;
			passData.gradientAtlas = gradientAtlasHandle;
			UseIfValid(builder, combinedHandle, AccessFlags.Read);
			UseIfValid(builder, normalHandle, AccessFlags.Read);
			UseIfValid(builder, gradientAtlasHandle, AccessFlags.Read);
			builder.SetRenderAttachment(materialAlbedoHandle, 0, AccessFlags.WriteAll);
			builder.SetRenderAttachment(materialNormalHandle, 1, AccessFlags.WriteAll);
			builder.SetRenderAttachment(materialTransportHandle, 2, AccessFlags.WriteAll);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (MaterialMapPassData data, RasterGraphContext context) =>
			{
				RasterCommandBuffer cmd = context.cmd;
				ParticleFluidRenderBindings.ApplyMetaballMaterialGlobals(cmd, data.lighting, data.frameContext, data.gradientAtlas);
				if (data.material == null || data.frameContext.cam == null)
				{
					return;
				}

				data.material.SetTexture(CombinedTex, data.combinedTexture ?? Texture2D.blackTexture);
				data.material.SetTexture(NormalTex, data.normalTexture ?? Texture2D.blackTexture);

				cmd.SetViewProjectionMatrices(Matrix4x4.identity, data.frameContext.renderRegion.CreateRegionProjection());
				cmd.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), data.frameContext.renderRegion.CreateRegionMatrix(), data.material, 0, MaterialMapsPass);
				cmd.SetViewProjectionMatrices(data.frameContext.cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(data.frameContext.cam.projectionMatrix, false));
			});
		}

		public void RenderUnlit(RasterCommandBuffer commandBuffer, Bounds region)
		{
			if (!materialMaps.IsAllocated || commandBuffer == null || _material == null)
			{
				return;
			}

			_material.SetTexture(MaterialAlbedoTex, materialMaps.albedoTexture);
			commandBuffer.BeginSample("Particle Fluid/Unlit Fallback");
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

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		private class MaterialMapPassData
		{
			public ParticleFluidLighting2D lighting;
			public ParticleFluidLighting2D.FrameContext frameContext;
			public Material material;
			public Texture combinedTexture;
			public Texture normalTexture;
			public TextureHandle gradientAtlas;
		}
	}
}
