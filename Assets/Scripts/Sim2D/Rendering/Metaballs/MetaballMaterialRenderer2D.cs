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
			using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass("Material Maps", out MaterialMapPassData _);
			UseIfValid(builder, combinedHandle, AccessFlags.Read);
			UseIfValid(builder, normalHandle, AccessFlags.Read);
			UseIfValid(builder, gradientAtlasHandle, AccessFlags.Read);
			UseIfValid(builder, materialAlbedoHandle, AccessFlags.Write);
			UseIfValid(builder, materialNormalHandle, AccessFlags.Write);
			UseIfValid(builder, materialTransportHandle, AccessFlags.Write);
			builder.AllowPassCulling(false);
			builder.SetRenderFunc((MaterialMapPassData _, UnsafeGraphContext context) =>
			{
				CommandBuffer nativeCommandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
				ParticleFluidRenderBindings.ApplyMetaballMaterialGlobals(nativeCommandBuffer, lighting, frameContext);
				if (!materialMaps.IsAllocated || nativeCommandBuffer == null || _material == null || frameContext.cam == null)
				{
					return;
				}

				_material.SetTexture(CombinedTex, combinedTexture ?? Texture2D.blackTexture);
				_material.SetTexture(NormalTex, normalTexture ?? Texture2D.blackTexture);

				_materialMapTargets[0] = materialMaps.albedoTexture;
				_materialMapTargets[1] = materialMaps.normalTexture;
				_materialMapTargets[2] = materialMaps.transportTexture;

				nativeCommandBuffer.BeginSample("Particle Fluid/Build Material Maps");
				ParticleFluidRenderUtils.DrawRegionQuad(
					nativeCommandBuffer,
					_materialMapTargets,
					_material,
					MaterialMapsPass,
					frameContext.renderRegion,
					frameContext.cam,
					true,
					Color.clear);
				nativeCommandBuffer.EndSample("Particle Fluid/Build Material Maps");
			});
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

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		private class MaterialMapPassData
		{
		}
	}
}
