using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidDiffuseColorBleed
	{
		readonly ParticleFluidLighting2D owner;

		static readonly int FinalLightingTempId = Shader.PropertyToID("_ParticleFluidFinalLightingTemp");
		static readonly int ColorBleedTemp0Id = Shader.PropertyToID("_ParticleFluidColorBleed0");
		static readonly int ColorBleedTemp1Id = Shader.PropertyToID("_ParticleFluidColorBleed1");

		public ParticleFluidDiffuseColorBleed(ParticleFluidLighting2D owner)
		{
			this.owner = owner;
		}

		public void Render(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam)
		{
			Texture albedoTexture = owner.materialAlbedoTexture != null ? owner.materialAlbedoTexture : Texture2D.blackTexture;
			int materialWidth = Mathf.Max(albedoTexture.width, 1);
			int materialHeight = Mathf.Max(albedoTexture.height, 1);
			int sourceWidth = owner.materialRenderRegion.IsCropped ? materialWidth : Mathf.Max(cam.pixelWidth, 1);
			int sourceHeight = owner.materialRenderRegion.IsCropped ? materialHeight : Mathf.Max(cam.pixelHeight, 1);
			float bleedScale = Mathf.Clamp(owner.diffuseColorBleedTextureScale, 0.125f, 1f);
			int bleedWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * bleedScale));
			int bleedHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * bleedScale));

			commandBuffer.GetTemporaryRT(FinalLightingTempId, sourceWidth, sourceHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
			commandBuffer.GetTemporaryRT(ColorBleedTemp0Id, bleedWidth, bleedHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
			commandBuffer.GetTemporaryRT(ColorBleedTemp1Id, bleedWidth, bleedHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

			commandBuffer.SetRenderTarget(FinalLightingTempId);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			owner.ConfigureLightingIntermediate();
			commandBuffer.Blit(null, FinalLightingTempId, owner.lightingMaterial, 0);
			BindMaterial(bleedScale, false);
			commandBuffer.SetGlobalTexture("_ParticleFluidSourceTex", FinalLightingTempId);
			commandBuffer.Blit(FinalLightingTempId, ColorBleedTemp0Id, owner.colorBleedMaterial, 0);
			commandBuffer.Blit(ColorBleedTemp0Id, ColorBleedTemp1Id, owner.colorBleedMaterial, 1);
			commandBuffer.Blit(ColorBleedTemp1Id, ColorBleedTemp0Id, owner.colorBleedMaterial, 2);
			BindMaterial(bleedScale, true);
			commandBuffer.SetGlobalTexture("_ParticleFluidSourceTex", FinalLightingTempId);
			commandBuffer.SetGlobalTexture("_ParticleFluidBleedTex", ColorBleedTemp0Id);
			commandBuffer.Blit(FinalLightingTempId, finalTarget, owner.colorBleedMaterial, 3);

			commandBuffer.ReleaseTemporaryRT(ColorBleedTemp1Id);
			commandBuffer.ReleaseTemporaryRT(ColorBleedTemp0Id);
			commandBuffer.ReleaseTemporaryRT(FinalLightingTempId);
		}

		void BindMaterial(float bleedScale, bool compositeToCamera)
		{
			owner.colorBleedMaterial.SetTexture("MaterialAlbedoTex", owner.materialAlbedoTexture != null ? owner.materialAlbedoTexture : Texture2D.blackTexture);
			owner.colorBleedMaterial.SetTexture("MaterialNormalTex", owner.materialNormal0Texture != null ? owner.materialNormal0Texture : Texture2D.blackTexture);
			owner.colorBleedMaterial.SetTexture("MaterialNormalTex1", owner.materialNormal1Texture != null ? owner.materialNormal1Texture : Texture2D.blackTexture);
			owner.colorBleedMaterial.SetInt("particleFluidCompositeRegionEnabled", compositeToCamera && owner.materialRenderRegion.IsCropped ? 1 : 0);
			owner.colorBleedMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			owner.colorBleedMaterial.SetVector("particleFluidCompositeUvRect", owner.materialRenderRegion.SourceUvRect);
			owner.colorBleedMaterial.SetVector("particleFluidClipRect", owner.materialRenderRegion.SourceUvRect);
			owner.colorBleedMaterial.SetFloat("_BleedStrength", Mathf.Max(owner.diffuseColorBleedStrength, 0f));
			owner.colorBleedMaterial.SetFloat("_BleedRadius", Mathf.Max(owner.diffuseColorBleedRadius * owner.currentZoomScale * bleedScale, 0f));
			owner.colorBleedMaterial.SetFloat("_BleedSelfSubtract", Mathf.Clamp01(owner.diffuseColorBleedSelfSubtract));
			owner.colorBleedMaterial.SetFloat("_BleedNormalWeight", Mathf.Clamp01(owner.diffuseColorBleedNormalWeight));
		}
	}
}
