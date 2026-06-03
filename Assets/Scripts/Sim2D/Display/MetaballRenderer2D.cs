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
		Material causticBlurMaterial;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		RenderTexture causticAccumulationRTexture;
		RenderTexture causticAccumulationGTexture;
		RenderTexture causticAccumulationBTexture;
		RenderTexture causticResolvedTexture;
		RenderTexture causticBlurTexture;
		RenderTexture causticHistoryTexture;
		RenderTexture causticTemporalTexture;
		readonly RenderTexture[] bloomDownTextures = new RenderTexture[MaxCustomBloomLevels];
		readonly RenderTexture[] bloomUpTextures = new RenderTexture[MaxCustomBloomLevels];
		CommandBuffer commandBuffer;
		bool commandBufferAttached;
		bool clearCausticHistory;
		bool hasPreviousCausticCamera;
		int causticFrameIndex;
		Vector2 previousCausticWorldCenter;
		Vector2 previousCausticWorldSize;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null || causticBlurMaterial == null)
			{
				RemoveCommandBuffer();
				return;
			}

			EnsureCommandBuffer(cam);
			commandBuffer.Clear();
			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyCompositeSettings(display, cam);
			BuildCommandBuffer(display, cam, commandBuffer, BuiltinRenderTextureType.CameraTarget);
		}

		public void Record(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null || causticBlurMaterial == null || cam == null || targetCommandBuffer == null || display.mesh == null || display.argsBuffer == null)
			{
				return;
			}

			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyCompositeSettings(display, cam);
			BuildCommandBuffer(display, cam, targetCommandBuffer, finalTarget);
		}

		public void RemoveCommandBuffer()
		{
			if (RenderPipelineManager.currentPipeline != null)
			{
				commandBufferAttached = false;
				return;
			}

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
			if (RenderPipelineManager.currentPipeline != null)
			{
				return;
			}

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
			ComputeHelper.Release(causticAccumulationRTexture, causticAccumulationGTexture, causticAccumulationBTexture, causticResolvedTexture, causticBlurTexture, causticHistoryTexture, causticTemporalTexture);
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

			if (causticBlurMaterial != null)
			{
				Object.DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}
		}

		void EnsureMaterials(ParticleDisplay2D display)
		{
			EnsureMaterial(ref metaballMaterial, display.metaballShader);
			EnsureMaterial(ref compositeMaterial, display.metaballs.compositeShader);
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticBlurMaterial, display.metaballs.blurShader);
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

			if (!commandBufferAttached && cam != null)
			{
				cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
				cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterForwardAlpha);
				RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects);
#if UNITY_EDITOR
				RemoveFromCamera(ParticleDisplay2D.GetSceneViewCamera());
#endif
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

			if (ShouldRenderCaustics(settings))
			{
				int causticWidth = Mathf.Max(1, Mathf.RoundToInt(width * settings.causticsRenderTextureScale));
				int causticHeight = Mathf.Max(1, Mathf.RoundToInt(height * settings.causticsRenderTextureScale));
				ComputeHelper.CreateRenderTexture(ref causticAccumulationRTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation R");
				ComputeHelper.CreateRenderTexture(ref causticAccumulationGTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation G");
				ComputeHelper.CreateRenderTexture(ref causticAccumulationBTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation B");
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
				if (settings.causticsTemporalEnabled)
				{
					bool historyChanged = ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
					ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
					clearCausticHistory |= historyChanged;
				}
				else
				{
					ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture);
					causticHistoryTexture = null;
					causticTemporalTexture = null;
					clearCausticHistory = true;
					hasPreviousCausticCamera = false;
				}
			}
			else
			{
				ComputeHelper.Release(causticAccumulationRTexture, causticAccumulationGTexture, causticAccumulationBTexture, causticResolvedTexture, causticBlurTexture, causticHistoryTexture, causticTemporalTexture);
				causticAccumulationRTexture = null;
				causticAccumulationGTexture = null;
				causticAccumulationBTexture = null;
				causticResolvedTexture = null;
				causticBlurTexture = null;
				causticHistoryTexture = null;
				causticTemporalTexture = null;
				clearCausticHistory = true;
				hasPreviousCausticCamera = false;
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
			compositeMaterial.SetInt("metaballTonemapUseAces", settings.tonemapUseAces ? 1 : 0);
			compositeMaterial.SetFloat("metaballTonemapHighlightDesaturation", settings.tonemapHighlightDesaturation);
			bool renderCaustics = ShouldRenderCaustics(settings);
			compositeMaterial.SetInt("metaballCausticsEnabled", renderCaustics ? 1 : 0);
			compositeMaterial.SetTexture("CausticTex", settings.causticsTemporalEnabled ? causticTemporalTexture : causticResolvedTexture);
			compositeMaterial.SetFloat("metaballCausticsIntensity", settings.causticsIntensity);
			compositeMaterial.SetFloat("metaballCausticsLightFieldIntensity", settings.causticsLightFieldIntensity);
			compositeMaterial.SetFloat("metaballCausticsAdditiveBlend", settings.causticsAdditiveBlend);
			compositeMaterial.SetColor("metaballCausticsColor", settings.lightColor);
			compositeMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : settings.causticsTemporalHistoryWeight);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			compositeMaterial.SetInt("debugMode", (int)display.debugMode);
			display.ApplyDebugClipSettings(compositeMaterial);
			compositeMaterial.SetFloat("ditherStrength", settings.ditherStrength);
			compositeMaterial.SetVector("particleLightDirection", settings.LightDirection);
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

		void BuildCommandBuffer(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			targetCommandBuffer.SetRenderTarget(combinedAccumulationTexture);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 0, display.argsBuffer);
			targetCommandBuffer.SetRenderTarget(normalAccumulationTexture);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 1, display.argsBuffer);

			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);

			if (ShouldRenderCaustics(display.metaballs))
			{
				BuildCaustics(display, cam, targetCommandBuffer);
			}

			if (display.metaballs.bloomEnabled)
			{
				int bloomLevels = GetCustomBloomLevelCount(display);
				targetCommandBuffer.SetRenderTarget(bloomDownTextures[0]);
				targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
				targetCommandBuffer.Blit(null, bloomDownTextures[0], compositeMaterial, 1);
				for (int i = 1; i < bloomLevels; i++)
				{
					targetCommandBuffer.Blit(bloomDownTextures[i - 1], bloomDownTextures[i], compositeMaterial, 3);
				}

				RenderTexture lastBloom = bloomDownTextures[bloomLevels - 1];
				for (int i = bloomLevels - 2; i >= 0; i--)
				{
					targetCommandBuffer.SetGlobalTexture("BloomTex", bloomDownTextures[i]);
					targetCommandBuffer.Blit(lastBloom, bloomUpTextures[i], compositeMaterial, 4);
					lastBloom = bloomUpTextures[i];
				}

				targetCommandBuffer.SetGlobalTexture("BloomTex", lastBloom);
			}

			targetCommandBuffer.Blit(null, finalTarget, compositeMaterial, 0);

			display.AppendVectorFieldDraw(targetCommandBuffer);
		}

		void BuildCaustics(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			ComputeShader compute = settings.causticsComputeShader;
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticAccumulationRTexture.width;
			int height = causticAccumulationRTexture.height;
			Vector3 lightDirection = settings.LightDirection;
			float analyticBoundaryExpansion = GetCausticAnalyticBoundaryExpansion(display);

			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 currentWorldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			Vector2 currentWorldSize = new Vector2(worldWidth, worldHeight);
			GetCausticRayRange(display, settings, width, height, lightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float rayStartOffset, out int rayCount);

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", rayCount);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayStartOffset", rayStartOffset);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", settings.causticsRaySteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", settings.causticsRayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", settings.causticsRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsUseSoftSplat", settings.causticsUseSoftSplat ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", settings.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", settings.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsStepPixels", settings.causticsStepPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsIndexOfRefraction", settings.causticsIndexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassIndexOfRefraction", settings.causticsGlassIndexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0IndexOfRefraction", settings.causticsPhase0IndexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1IndexOfRefraction", settings.causticsPhase1IndexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceTransmittance", settings.causticsSurfaceTransmittance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsFresnelStrength", settings.causticsFresnelStrength);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", settings.causticsStochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", settings.causticsDispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightAngularRadius", settings.causticsLightAngularRadiusDegrees * Mathf.Deg2Rad);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Absorption", settings.causticsPhase0Absorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Absorption", settings.causticsPhase1Absorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0ColorAbsorption", settings.causticsPhase0ColorAbsorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1ColorAbsorption", settings.causticsPhase1ColorAbsorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTintBoost", settings.causticsTintBoost);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", settings.causticsRayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", settings.causticsTemporalEnabled ? settings.causticsTemporalJitterPixels : 0f);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalIorJitter", settings.causticsTemporalEnabled ? settings.causticsTemporalIorJitter : 0f);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", causticFrameIndex++);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && settings.causticsUseAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));

			BindCausticAccumulationTextures(targetCommandBuffer, compute, clearKernel);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticResult", causticResolvedTexture);
			DispatchCaustics(targetCommandBuffer, compute, clearKernel, width, height);

			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap2", display.gradientTexture2);
			BindCausticAccumulationTextures(targetCommandBuffer, compute, traceKernel);
			DispatchCausticTrace(targetCommandBuffer, compute, traceKernel, rayCount, Mathf.Max(1, settings.causticsRaysPerPixel));

			BindCausticAccumulationTextures(targetCommandBuffer, compute, resolveKernel);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			DispatchCaustics(targetCommandBuffer, compute, resolveKernel, width, height);

			if (settings.causticsBlurRadius > 0.001f)
			{
				causticBlurMaterial.SetFloat("blurRadius", settings.causticsBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(causticResolvedTexture, causticBlurTexture, causticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(causticBlurTexture, causticResolvedTexture, causticBlurMaterial);
			}

			if (settings.causticsTemporalEnabled)
			{
				if (clearCausticHistory)
				{
					targetCommandBuffer.SetRenderTarget(causticHistoryTexture);
					targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
					clearCausticHistory = false;
					hasPreviousCausticCamera = false;
				}

				Vector2 historyWorldCenter = hasPreviousCausticCamera ? previousCausticWorldCenter : currentWorldCenter;
				Vector2 historyWorldSize = hasPreviousCausticCamera ? previousCausticWorldSize : currentWorldSize;
				targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
				targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(historyWorldCenter.x, historyWorldCenter.y, 0f, 0f));
				targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(historyWorldSize.x, historyWorldSize.y, 0f, 0f));
				targetCommandBuffer.SetGlobalTexture("CausticHistoryTex", causticHistoryTexture);
				targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, compositeMaterial, 5);
				targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);

				previousCausticWorldCenter = currentWorldCenter;
				previousCausticWorldSize = currentWorldSize;
				hasPreviousCausticCamera = true;
			}
			else
			{
				hasPreviousCausticCamera = false;
			}

		}

		void DispatchCaustics(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchCausticTrace(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			DispatchCaustics(targetCommandBuffer, compute, kernel, rayCount * raysPerPixel, 1);
		}

		float GetCausticAnalyticBoundaryExpansion(ParticleDisplay2D display)
		{
			float spacing = Mathf.Sqrt(1f / (display.sim.spawner2D.ghostDensity * display.sim.ResolvedResolutionFactor));
			int numLayers = Mathf.CeilToInt(display.sim.EffectiveSmoothingRadius / spacing);
			return spacing * numLayers + display.metaballs.causticsAnalyticBoundaryPadding;
		}

		void GetCausticRayRange(ParticleDisplay2D display, ParticleDisplay2D.MetaballSettings settings, int width, int height, Vector3 lightDirection, Vector2 worldCenter, Vector2 worldSize, float analyticBoundaryExpansion, out float startOffset, out int rayCount)
		{
			Vector2 lightXY = new Vector2(-lightDirection.x, -lightDirection.y);
			Vector2 rayDir = lightXY.sqrMagnitude > 0.0001f ? lightXY.normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(width * width + height * height);
			startOffset = -fullSpan * 0.5f;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!display.sim.useEllipticalBounds || !settings.causticsUseAnalyticBoundary)
			{
				return;
			}

			float minOffset = float.PositiveInfinity;
			float maxOffset = float.NegativeInfinity;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * analyticBoundaryExpansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return;
			}

			for (int i = 0; i < 128; i++)
			{
				float angle = i * Mathf.PI * 2f / 128f;
				Vector2 world = display.sim.ellipseBoundsCenter + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
				if (world.y >= display.sim.obstacleY - analyticBoundaryExpansion)
				{
					IncludeCausticLaunchPoint(world, worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				}
			}

			float expandedObstacleY = display.sim.obstacleY - analyticBoundaryExpansion;
			float cutRelY = (expandedObstacleY - display.sim.ellipseBoundsCenter.y) / radii.y;
			if (Mathf.Abs(cutRelY) <= 1f)
			{
				float cutX = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY));
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x - cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x + cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
			}

			if (float.IsNaN(minOffset) || float.IsInfinity(minOffset) || float.IsNaN(maxOffset) || float.IsInfinity(maxOffset))
			{
				return;
			}

			float angularPadding = Mathf.Sin(settings.causticsLightAngularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(settings.causticsStepPixels * 4f + angularPadding, 2f);
			startOffset = Mathf.Floor(minOffset - padding);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(maxOffset + padding - startOffset));
		}

		void IncludeCausticLaunchPoint(Vector2 world, Vector2 worldCenter, Vector2 worldSize, int width, int height, Vector2 tangent, ref float minOffset, ref float maxOffset)
		{
			Vector2 uv = new Vector2(
				(world.x - worldCenter.x) / Mathf.Max(worldSize.x, 0.0001f) + 0.5f,
				(world.y - worldCenter.y) / Mathf.Max(worldSize.y, 0.0001f) + 0.5f
			);
			Vector2 pixel = new Vector2(uv.x * width, uv.y * height);
			Vector2 centredPixel = pixel - new Vector2(width, height) * 0.5f;
			float offset = Vector2.Dot(centredPixel, tangent);
			minOffset = Mathf.Min(minOffset, offset);
			maxOffset = Mathf.Max(maxOffset, offset);
		}

		void BindCausticAccumulationTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumR", causticAccumulationRTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumG", causticAccumulationGTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumB", causticAccumulationBTexture);
		}

		int GetCustomBloomLevelCount(ParticleDisplay2D display)
		{
			return Mathf.Clamp(display.metaballs.bloomIterations, 1, MaxCustomBloomLevels);
		}

		bool ShouldRenderCaustics(ParticleDisplay2D.MetaballSettings settings)
		{
			return settings.causticsEnabled && settings.causticsComputeShader != null;
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
