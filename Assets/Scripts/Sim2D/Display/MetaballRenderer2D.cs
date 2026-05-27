using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		const int MaxCustomBloomLevels = 4;
		const string CommandBufferName = "Sim2D Metaball Render";

		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		readonly RenderTexture[] bloomDownTextures = new RenderTexture[MaxCustomBloomLevels];
		readonly RenderTexture[] bloomUpTextures = new RenderTexture[MaxCustomBloomLevels];
		CommandBuffer commandBuffer;
		bool commandBufferAttached;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null)
			{
				RemoveCommandBuffer();
				return;
			}

			EnsureCommandBuffer(cam);
			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyCompositeSettings(display, cam);
			BuildCommandBuffer(display);
		}

		public void RemoveCommandBuffer()
		{
			if (commandBuffer != null)
			{
				RemoveFromCamera(Camera.main);
#if UNITY_EDITOR
				RemoveFromCamera(ParticleDisplay2D.GetSceneViewCamera());
#endif
			}

			commandBufferAttached = false;
		}

		public void RemoveFromCamera(Camera cam)
		{
			if (cam == null || commandBuffer == null)
			{
				return;
			}

			cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
			cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
			cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
			RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything);
			RemoveCommandBuffersByName(cam, CameraEvent.AfterForwardAlpha);
			RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects);
		}

		public void Release()
		{
			RemoveCommandBuffer();
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture);
			ReleaseBloomTextures();

			if (commandBuffer != null)
			{
				commandBuffer.Release();
				commandBuffer = null;
			}

			if (metaballMaterial != null)
			{
				Object.DestroyImmediate(metaballMaterial);
				metaballMaterial = null;
			}

			if (compositeMaterial != null)
			{
				Object.DestroyImmediate(compositeMaterial);
				compositeMaterial = null;
			}

			if (blurMaterial != null)
			{
				Object.DestroyImmediate(blurMaterial);
				blurMaterial = null;
			}
		}

		void EnsureMaterials(ParticleDisplay2D display)
		{
			EnsureMaterial(ref metaballMaterial, display.metaballShader);
			EnsureMaterial(ref compositeMaterial, display.metaballs.compositeShader);
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
		}

		static void EnsureMaterial(ref Material material, Shader shader)
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

		void ApplyMetaballMaterialSettings(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			display.BindSimulationBuffers(metaballMaterial);
			display.ApplyCommonParticleSettings(metaballMaterial);
			metaballMaterial.SetFloat("metaballSharpness", settings.sharpness);
			metaballMaterial.SetFloat("metaballIntensity", settings.intensity);
			metaballMaterial.SetFloat("convexCurvatureMetaballBoost", settings.convexCurvatureBoost);
			metaballMaterial.SetFloat("convexCurvatureBoostMax", settings.convexCurvatureBoostMax);
			metaballMaterial.SetFloat("convexCurvatureBoostStartBlurRadius", settings.convexCurvatureBoostStartBlurRadius);
			metaballMaterial.SetFloat("convexCurvatureBoostBlurRange", settings.convexCurvatureBoostBlurRange);
			metaballMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			metaballMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			metaballMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			metaballMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			metaballMaterial.SetFloat("metaballGhostBoundaryCornerBlendWidth", settings.ghostBoundaryCornerBlendWidth);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalWidth", settings.ghostBoundaryNormalWidth);
		}

		void EnsureCommandBuffer(Camera cam)
		{
			if (commandBuffer == null)
			{
				commandBuffer = new CommandBuffer { name = CommandBufferName };
			}

#if UNITY_EDITOR
			RemoveFromCamera(ParticleDisplay2D.GetSceneViewCamera());
#endif

			if (!commandBufferAttached && cam != null)
			{
				cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
				cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterForwardAlpha);
				RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects);
				cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				commandBufferAttached = true;
			}
		}

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * settings.renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * settings.renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Accumulation");
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Blur");

			if (settings.bloomEnabled)
			{
				int bloomLevels = GetCustomBloomLevelCount(display);
				int bloomWidth = Mathf.Max(1, Mathf.RoundToInt(width * settings.bloomRenderTextureScale));
				int bloomHeight = Mathf.Max(1, Mathf.RoundToInt(height * settings.bloomRenderTextureScale));
				for (int i = 0; i < MaxCustomBloomLevels; i++)
				{
					if (i < bloomLevels)
					{
						int mipWidth = Mathf.Max(1, bloomWidth >> i);
						int mipHeight = Mathf.Max(1, bloomHeight >> i);
						ComputeHelper.CreateRenderTexture(ref bloomDownTextures[i], mipWidth, mipHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Bloom Down " + i);
						ComputeHelper.CreateRenderTexture(ref bloomUpTextures[i], mipWidth, mipHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Bloom Up " + i);
					}
					else
					{
						ComputeHelper.Release(bloomDownTextures[i], bloomUpTextures[i]);
						bloomDownTextures[i] = null;
						bloomUpTextures[i] = null;
					}
				}
			}
			else
			{
				ReleaseBloomTextures();
			}
		}

		void ApplyCompositeSettings(ParticleDisplay2D display, Camera cam)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			compositeMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", settings.edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			compositeMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			compositeMaterial.SetFloat("phaseBiasNormalStrength", settings.phaseBiasNormalStrength);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			compositeMaterial.SetTexture("ColourMap", display.gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", display.gradientTexture2);
			compositeMaterial.SetTexture("DebugHeatMap", display.debugHeatMapTexture);
			compositeMaterial.SetTexture("DebugSignedHeatMap", display.debugSignedHeatMapTexture);

			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			float effectiveRefractionStrength = settings.refractionStrength * display.GetZoomScale(cam);
			float effectiveBloomSampleScale = Mathf.Max(0.5f, settings.bloomRadius / 8f);
			float effectiveConfiguredBlurRadius = display.EffectiveConfiguredBlurRadius;
			metaballMaterial.SetFloat("metaballBlurRadius", effectiveConfiguredBlurRadius);
			compositeMaterial.SetFloat("metaballRefractionStrength", effectiveRefractionStrength);
			compositeMaterial.SetFloat("metaballRefractionEdgeFade", settings.refractionEdgeFade);
			compositeMaterial.SetFloat("metaballIridescenceIntensity", settings.iridescenceIntensity);
			compositeMaterial.SetFloat("metaballIridescenceScale", settings.iridescenceScale);
			compositeMaterial.SetFloat("customBloomThreshold", settings.bloomThreshold);
			compositeMaterial.SetFloat("customBloomSoftKnee", settings.bloomSoftKnee);
			compositeMaterial.SetFloat("customBloomIntensity", settings.bloomIntensity);
			compositeMaterial.SetFloat("customBloomResponse", settings.bloomResponse);
			compositeMaterial.SetFloat("customBloomSampleScale", Mathf.Max(0.5f, effectiveBloomSampleScale));
			compositeMaterial.SetInt("customBloomEnabled", settings.bloomEnabled ? 1 : 0);
			compositeMaterial.SetInt("metaballTonemapEnabled", settings.tonemapEnabled ? 1 : 0);
			compositeMaterial.SetFloat("metaballTonemapExposure", settings.tonemapExposure);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			compositeMaterial.SetInt("debugMode", (int)display.debugMode);
			display.ApplyDebugClipSettings(compositeMaterial);
			compositeMaterial.SetFloat("ditherStrength", settings.ditherStrength);
			compositeMaterial.SetVector("particleLightDirection", settings.lightDirection);
			compositeMaterial.SetColor("particleLightColor", settings.lightColor);
			compositeMaterial.SetFloat("particleAmbientLight", settings.ambientLight);
			compositeMaterial.SetFloat("particleDirectionalLightIntensity", settings.directionalLightIntensity);
			compositeMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			compositeMaterial.SetColor("particleSpecularColor", settings.specularColor);
			compositeMaterial.SetFloat("particleSpecularIntensity", settings.specularIntensity);
			compositeMaterial.SetFloat("particleSpecularPower", settings.specularPower);
			compositeMaterial.SetColor("particleFresnelColor", settings.fresnelColor);
			compositeMaterial.SetFloat("particleFresnelIntensity", settings.fresnelIntensity);
			compositeMaterial.SetFloat("particleFresnelPower", settings.fresnelPower);
			compositeMaterial.SetVector("particleGlowDirection", new Vector4(settings.glowDirection.x, settings.glowDirection.y, 0f, 0f));
			compositeMaterial.SetColor("particleGlowColor", settings.glowColor);
			compositeMaterial.SetFloat("particleGlowIntensity", settings.glowIntensity);
			compositeMaterial.SetFloat("particleGlowPower", settings.glowPower);
			compositeMaterial.SetFloat("particleTransmissionIntensity", settings.transmissionIntensity);
			compositeMaterial.SetFloat("particleTransmissionPower", settings.transmissionPower);
			compositeMaterial.SetFloat("particleEdgeDarkening", settings.edgeDarkening);
			compositeMaterial.SetFloat("particleEdgeDarkeningPower", settings.edgeDarkeningPower);
			compositeMaterial.SetColor("particleSubsurfaceColor", settings.subsurfaceColor);
			compositeMaterial.SetFloat("particleSubsurfaceIntensity", settings.subsurfaceIntensity);
			compositeMaterial.SetFloat("particleSubsurfacePower", settings.subsurfacePower);
			compositeMaterial.SetFloat("particleSubsurfaceThickness", settings.subsurfaceThickness);
			compositeMaterial.SetFloat("particleSubsurfaceEdgeBoost", settings.subsurfaceEdgeBoost);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void BuildCommandBuffer(ParticleDisplay2D display)
		{
			commandBuffer.Clear();
			commandBuffer.SetRenderTarget(combinedAccumulationTexture);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			commandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 0, display.argsBuffer);
			commandBuffer.SetRenderTarget(normalAccumulationTexture);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			commandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 1, display.argsBuffer);

			commandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			commandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			commandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			commandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			commandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);

			if (display.metaballs.bloomEnabled)
			{
				int bloomLevels = GetCustomBloomLevelCount(display);
				commandBuffer.SetRenderTarget(bloomDownTextures[0]);
				commandBuffer.ClearRenderTarget(false, true, Color.clear);
				commandBuffer.Blit(null, bloomDownTextures[0], compositeMaterial, 1);
				for (int i = 1; i < bloomLevels; i++)
				{
					commandBuffer.Blit(bloomDownTextures[i - 1], bloomDownTextures[i], compositeMaterial, 3);
				}

				RenderTexture lastBloom = bloomDownTextures[bloomLevels - 1];
				for (int i = bloomLevels - 2; i >= 0; i--)
				{
					commandBuffer.SetGlobalTexture("BloomTex", bloomDownTextures[i]);
					commandBuffer.Blit(lastBloom, bloomUpTextures[i], compositeMaterial, 4);
					lastBloom = bloomUpTextures[i];
				}

				commandBuffer.SetGlobalTexture("BloomTex", lastBloom);
			}

			commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0);

			display.AppendVectorFieldDraw(commandBuffer);
		}

		int GetCustomBloomLevelCount(ParticleDisplay2D display)
		{
			return Mathf.Clamp(display.metaballs.bloomIterations, 1, MaxCustomBloomLevels);
		}

		void ReleaseBloomTextures()
		{
			for (int i = 0; i < MaxCustomBloomLevels; i++)
			{
				ComputeHelper.Release(bloomDownTextures[i], bloomUpTextures[i]);
				bloomDownTextures[i] = null;
				bloomUpTextures[i] = null;
			}
		}

		void RemoveCommandBuffersByName(Camera cam, CameraEvent evt)
		{
			if (cam == null)
			{
				return;
			}

			CommandBuffer[] commandBuffers = cam.GetCommandBuffers(evt);
			for (int i = 0; i < commandBuffers.Length; i++)
			{
				CommandBuffer candidate = commandBuffers[i];
				if (candidate != null && candidate.name == CommandBufferName)
				{
					cam.RemoveCommandBuffer(evt, candidate);
				}
			}
		}
	}
}
