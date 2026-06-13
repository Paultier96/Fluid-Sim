using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidLightingRenderer2D
	{
		const int LightingPass = 0;

		Material lightingMaterial;
		Texture materialAlbedoTexture = Texture2D.blackTexture;
		Texture materialNormal0Texture = Texture2D.blackTexture;
		Texture materialNormal1Texture = Texture2D.blackTexture;

		public Material Material => lightingMaterial;
		public bool IsReady => lightingMaterial != null;

		public void EnsureMaterial(Shader shader)
		{
			if (shader == null || (lightingMaterial != null && lightingMaterial.shader == shader))
			{
				return;
			}

			if (lightingMaterial != null)
			{
				Object.DestroyImmediate(lightingMaterial);
			}

			lightingMaterial = new Material(shader);
		}

		public void SetMaterialTextures(Texture albedo, Texture normal0, Texture normal1)
		{
			materialAlbedoTexture = albedo != null ? albedo : Texture2D.blackTexture;
			materialNormal0Texture = normal0 != null ? normal0 : Texture2D.blackTexture;
			materialNormal1Texture = normal1 != null ? normal1 : Texture2D.blackTexture;
			BindMaterialTextures();
		}

		public void BindMaterialTextures()
		{
			if (lightingMaterial == null)
			{
				return;
			}

			lightingMaterial.SetTexture("MaterialAlbedoTex", materialAlbedoTexture != null ? materialAlbedoTexture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("MaterialNormalTex", materialNormal0Texture != null ? materialNormal0Texture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("MaterialNormalTex1", materialNormal1Texture != null ? materialNormal1Texture : Texture2D.blackTexture);
			int width = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.width : 1, 1);
			int height = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.height : 1, 1);
			lightingMaterial.SetVector("MaterialAlbedoTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
		}

		public void ApplySettings(
			ParticleDisplay2D display,
			ParticleFluidLighting2D settings,
			Camera cam,
			bool renderCaustics,
			bool renderDirectionalLightField,
			bool renderSoftLight,
			bool radianceCascadeSoftLight,
			Texture causticTexture,
			Texture lightDirectionTexture,
			float analyticBoundaryExpansion,
			Vector3 primaryDirectLightingDirection,
			Vector3 secondaryDirectLightingDirection)
		{
			if (lightingMaterial == null)
			{
				return;
			}

			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);

			BindMaterialTextures();
			lightingMaterial.SetVector("particleFluidWorldCenter", new Vector4(worldCenter.x, worldCenter.y, 0f, 0f));
			lightingMaterial.SetVector("particleFluidWorldSize", new Vector4(worldWidth, worldHeight, 0f, 0f));
			lightingMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			lightingMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			lightingMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			lightingMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			lightingMaterial.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
			lightingMaterial.SetInt("particleFluidCausticsEnabled", renderCaustics ? 1 : 0);
			lightingMaterial.SetTexture("CausticTex", causticTexture != null ? causticTexture : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			lightingMaterial.SetTexture("LightDirectionTex", lightDirectionTexture != null ? lightDirectionTexture : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			lightingMaterial.SetInt("particleFluidSoftLightPhase0Only", radianceCascadeSoftLight ? 1 : 0);
			lightingMaterial.SetFloat("particleFluidRadianceCascadeDirectCausticStrength", settings.radianceCascadeDirectCausticStrength);
			lightingMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			lightingMaterial.SetColor("particleFluidPhase0DiffuseLightTint", settings.phase0Material.diffuseLightTint);
			lightingMaterial.SetColor("particleFluidPhase1DiffuseLightTint", settings.phase1Material.diffuseLightTint);
			lightingMaterial.SetFloat("particleFluidIridescenceIntensity", settings.iridescenceIntensity);
			lightingMaterial.SetFloat("particleFluidIridescenceScale", settings.iridescenceScale);
			lightingMaterial.SetVector("particleBaseLightDirection", settings.LightDirection);
			lightingMaterial.SetVector("particleLightDirection", primaryDirectLightingDirection);
			lightingMaterial.SetVector("particleSecondaryBaseLightDirection", settings.SecondaryLightDirection);
			lightingMaterial.SetVector("particleSecondaryLightDirection", secondaryDirectLightingDirection);
			lightingMaterial.SetInt("particleSecondaryLightEnabled", settings.secondaryLightEnabled && settings.SecondaryLight.intensity > 0f ? 1 : 0);
			lightingMaterial.SetColor("particleLightColor", settings.EffectiveLightColor);
			lightingMaterial.SetColor("particleSecondaryLightColor", settings.EffectiveSecondaryLightColor);
			lightingMaterial.SetFloat("particleAmbientLight", settings.ambientLight);
			lightingMaterial.SetFloat("particleLightIntensity", settings.PrimaryLight.intensity);
			lightingMaterial.SetFloat("particleSecondaryLightIntensity", settings.SecondaryLight.intensity);
			lightingMaterial.SetFloat("particlePhase0Reflectance", settings.phase0Material.reflectance);
			lightingMaterial.SetFloat("particlePhase1Reflectance", settings.phase1Material.reflectance);
			lightingMaterial.SetFloat("particlePhase0Roughness", settings.phase0Material.roughness);
			lightingMaterial.SetFloat("particlePhase1Roughness", settings.phase1Material.roughness);
			lightingMaterial.SetFloat("particlePhase0Metallic", settings.phase0Material.metallic);
			lightingMaterial.SetFloat("particlePhase1Metallic", settings.phase1Material.metallic);
			lightingMaterial.SetColor("particleFresnelColor", settings.fresnelColor);
			lightingMaterial.SetFloat("particleFresnelIntensity", settings.fresnelIntensity);
			lightingMaterial.SetFloat("particleFresnelPower", settings.fresnelPower);
			lightingMaterial.SetFloat("screenSpaceReflectionStrength0", settings.phase0Material.screenSpaceReflectionStrength);
			lightingMaterial.SetFloat("screenSpaceReflectionStrength1", settings.phase1Material.screenSpaceReflectionStrength);
			lightingMaterial.SetFloat("screenSpaceReflectionDistance", settings.screenSpaceReflectionDistance * display.GetZoomScale(cam));
			lightingMaterial.SetFloat("screenSpaceReflectionEdgePower", settings.screenSpaceReflectionEdgePower);
			lightingMaterial.SetFloat("particleTransmissionIntensity", settings.transmissionIntensity);
			lightingMaterial.SetFloat("particleTransmissionPower", settings.transmissionPower);
			lightingMaterial.SetFloat("particleEdgeDarkening", settings.edgeDarkening);
			lightingMaterial.SetFloat("particleEdgeDarkeningPower", settings.edgeDarkeningPower);
		}

		public void SetSoftLightTexture(Texture texture)
		{
			if (lightingMaterial != null)
			{
				lightingMaterial.SetTexture("SoftLightTex", texture != null ? texture : Texture2D.blackTexture);
			}
		}

		public void Render(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}

			BindMaterialTextures();
			commandBuffer.BeginSample("Particle Fluid/Final Lighting");
			commandBuffer.Blit(null, finalTarget, lightingMaterial, LightingPass);
			commandBuffer.EndSample("Particle Fluid/Final Lighting");
		}

		public void Release()
		{
			if (lightingMaterial != null)
			{
				Object.DestroyImmediate(lightingMaterial);
				lightingMaterial = null;
			}
		}
	}
}
