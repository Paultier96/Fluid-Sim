using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		const string CommandBufferName = "Sim2D Metaball Render";
		const int MaxCausticTraceThreads = 65535;
		const int CausticTraceThreadGroupSize = 64;

		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		Material velocityBlurMaterial;
		Material causticBlurMaterial;
		Material causticMotionBlurMaterial;
		Material lightDirectionBlurMaterial;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		RenderTexture velocityPhase0AccumulationTexture;
		RenderTexture velocityPhase0BlurTexture;
		RenderTexture velocityPhase1AccumulationTexture;
		RenderTexture velocityPhase1BlurTexture;
		ComputeBuffer causticAccumulationBuffer;
		ComputeBuffer causticMotionAccumulationBuffer;
		ComputeBuffer lightDirectionAccumulationBuffer;
		ComputeBuffer lightDirectionAccumulationFallbackBuffer;
		RenderTexture lightDirectionResultFallbackTexture;
		RenderTexture causticResolvedTexture;
		RenderTexture causticBlurTexture;
		RenderTexture causticMotionTexture;
		RenderTexture causticMotionDilatedTexture;
		RenderTexture causticMotionDilationScratchTexture;
		RenderTexture lightDirectionTexture;
		RenderTexture lightDirectionBlurTexture;
		RenderTexture softLightTexture0;
		RenderTexture softLightTexture1;
		RenderTexture causticHistoryTexture;
		RenderTexture causticTemporalTexture;
		RenderTexture lightDirectionHistoryTexture;
		RenderTexture lightDirectionTemporalTexture;
		CommandBuffer commandBuffer;
		bool commandBufferAttached;
		bool clearCausticHistory;
		bool hasPreviousCausticCamera;
		int causticFrameIndex;
		int causticTemporalFrameCount;
		Vector2 previousCausticWorldCenter;
		Vector2 previousCausticWorldSize;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null || velocityBlurMaterial == null || causticBlurMaterial == null || causticMotionBlurMaterial == null || lightDirectionBlurMaterial == null)
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
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null || velocityBlurMaterial == null || causticBlurMaterial == null || causticMotionBlurMaterial == null || lightDirectionBlurMaterial == null || cam == null || targetCommandBuffer == null || display.mesh == null || display.argsBuffer == null)
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
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture, velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityPhase1AccumulationTexture, velocityPhase1BlurTexture);
			ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer);
			causticAccumulationBuffer = null;
			causticMotionAccumulationBuffer = null;
			ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
			ReleaseOptionalCausticFallbackTextures();
			ReleaseLightDirectionTextures();
			ReleasePhaseDiffuseLightTextures();

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

			if (velocityBlurMaterial != null)
			{
				Object.DestroyImmediate(velocityBlurMaterial);
				velocityBlurMaterial = null;
			}

			if (causticBlurMaterial != null)
			{
				Object.DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}

			if (causticMotionBlurMaterial != null)
			{
				Object.DestroyImmediate(causticMotionBlurMaterial);
				causticMotionBlurMaterial = null;
			}

			if (lightDirectionBlurMaterial != null)
			{
				Object.DestroyImmediate(lightDirectionBlurMaterial);
				lightDirectionBlurMaterial = null;
			}

		}

		public void ClearCausticHistory()
		{
			clearCausticHistory = true;
			hasPreviousCausticCamera = false;
			causticTemporalFrameCount = 0;
		}

		void EnsureMaterials(ParticleDisplay2D display)
		{
			EnsureMaterial(ref metaballMaterial, display.metaballShader);
			EnsureMaterial(ref compositeMaterial, display.metaballs.compositeShader);
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref velocityBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticMotionBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref lightDirectionBlurMaterial, display.metaballs.blurShader);
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

		void ReleasePhaseDiffuseLightTextures()
		{
			ComputeHelper.Release(softLightTexture0, softLightTexture1);
			softLightTexture0 = null;
			softLightTexture1 = null;
		}

		void ReleaseLightDirectionTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationBuffer);
			lightDirectionAccumulationBuffer = null;
			ComputeHelper.Release(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionHistoryTexture, lightDirectionTemporalTexture);
			lightDirectionTexture = null;
			lightDirectionBlurTexture = null;
			lightDirectionHistoryTexture = null;
			lightDirectionTemporalTexture = null;
		}

		void ReleaseOptionalCausticFallbackTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationFallbackBuffer);
			lightDirectionAccumulationFallbackBuffer = null;
			ComputeHelper.Release(lightDirectionResultFallbackTexture);
			lightDirectionResultFallbackTexture = null;
		}

		void ApplyMetaballMaterialSettings(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			display.BindSimulationBuffers(metaballMaterial);
			display.ApplyCommonParticleSettings(metaballMaterial);
			metaballMaterial.SetFloat("metaballSharpness", settings.sharpness);
			metaballMaterial.SetFloat("metaballIntensity", settings.intensity);
			metaballMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			metaballMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			metaballMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			metaballMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
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
			ComputeHelper.CreateRenderTexture(ref velocityPhase0AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Accumulation");
			ComputeHelper.CreateRenderTexture(ref velocityPhase0BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Blur");
			ComputeHelper.CreateRenderTexture(ref velocityPhase1AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Accumulation");
			ComputeHelper.CreateRenderTexture(ref velocityPhase1BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Blur");

			if (ShouldRenderCaustics(settings))
			{
				bool renderDirectionalLightField = ShouldRenderDirectionalLightField(settings);
				bool renderPhaseDiffuseLight = ShouldRenderPhaseDiffuseLight(settings);
				int causticWidth = Mathf.Max(1, Mathf.RoundToInt(width * settings.textureScale));
				int causticHeight = Mathf.Max(1, Mathf.RoundToInt(height * settings.textureScale));
				int causticAccumulationCount = causticWidth * causticHeight * 4;
				ComputeHelper.CreateStructuredBuffer<uint>(ref causticAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<int>(ref causticMotionAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<uint>(ref lightDirectionAccumulationFallbackBuffer, 4);
				ComputeHelper.CreateRenderTexture(ref lightDirectionResultFallbackTexture, 1, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Result Fallback");
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
				ComputeHelper.CreateRenderTexture(ref causticMotionTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilatedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilated");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilationScratchTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilation Scratch");
				if (renderDirectionalLightField)
				{
					ComputeHelper.CreateStructuredBuffer<uint>(ref lightDirectionAccumulationBuffer, causticAccumulationCount);
					ComputeHelper.CreateRenderTexture(ref lightDirectionTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction");
					ComputeHelper.CreateRenderTexture(ref lightDirectionBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Blur");
				}
				else
				{
					ReleaseLightDirectionTextures();
				}
				if (renderPhaseDiffuseLight)
				{
					int softLightWidth = Mathf.Max(1, Mathf.RoundToInt(causticWidth * settings.phaseDiffuseLightTextureScale));
					int softLightHeight = Mathf.Max(1, Mathf.RoundToInt(causticHeight * settings.phaseDiffuseLightTextureScale));
					ComputeHelper.CreateRenderTexture(ref softLightTexture0, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 0");
					ComputeHelper.CreateRenderTexture(ref softLightTexture1, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 1");
				}
				else
				{
					ReleasePhaseDiffuseLightTextures();
				}
				if (settings.temporalEnabled)
				{
					bool historyChanged = ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
					ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
					if (renderDirectionalLightField)
					{
						historyChanged |= ComputeHelper.CreateRenderTexture(ref lightDirectionHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction History");
						ComputeHelper.CreateRenderTexture(ref lightDirectionTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Temporal");
					}
					else
					{
						ComputeHelper.Release(lightDirectionHistoryTexture, lightDirectionTemporalTexture);
						lightDirectionHistoryTexture = null;
						lightDirectionTemporalTexture = null;
					}
					clearCausticHistory |= historyChanged;
				}
				else
				{
					ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture, lightDirectionHistoryTexture, lightDirectionTemporalTexture);
					causticHistoryTexture = null;
					causticTemporalTexture = null;
					lightDirectionHistoryTexture = null;
					lightDirectionTemporalTexture = null;
					clearCausticHistory = true;
					hasPreviousCausticCamera = false;
					causticTemporalFrameCount = 0;
				}
			}
			else
			{
				ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer);
				causticAccumulationBuffer = null;
				causticMotionAccumulationBuffer = null;
				ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
				ReleaseOptionalCausticFallbackTextures();
				ReleaseLightDirectionTextures();
				ReleasePhaseDiffuseLightTextures();
				causticResolvedTexture = null;
				causticBlurTexture = null;
				causticMotionTexture = null;
				causticMotionDilatedTexture = null;
				causticMotionDilationScratchTexture = null;
				causticHistoryTexture = null;
				causticTemporalTexture = null;
				clearCausticHistory = true;
				hasPreviousCausticCamera = false;
				causticTemporalFrameCount = 0;
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
			compositeMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			compositeMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			compositeMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			compositeMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			compositeMaterial.SetFloat("analyticBoundaryExpansion", GetAnalyticBoundaryExpansion(display));
			compositeMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			compositeMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture);
			compositeMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture);
			compositeMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			compositeMaterial.SetTexture("ColourMap", display.gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", display.gradientTexture2);
			compositeMaterial.SetTexture("DebugHeatMap", display.debugHeatMapTexture);
			compositeMaterial.SetTexture("DebugSignedHeatMap", display.debugSignedHeatMapTexture);

			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			float effectiveRefractionStrength = settings.refractionStrength * display.GetZoomScale(cam);
			float effectiveConfiguredBlurRadius = display.EffectiveConfiguredBlurRadius;
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			compositeMaterial.SetVector("metaballWorldCenter", new Vector4(worldCenter.x, worldCenter.y, 0f, 0f));
			compositeMaterial.SetVector("metaballWorldSize", new Vector4(worldWidth, worldHeight, 0f, 0f));
			compositeMaterial.SetFloat("metaballRefractionStrength", effectiveRefractionStrength);
			compositeMaterial.SetFloat("metaballRefractionEdgeFade", settings.refractionEdgeFade);
			compositeMaterial.SetInt("screenSpaceRefractionCanCrossPhases", settings.screenSpaceRefractionCanCrossPhases ? 1 : 0);
			compositeMaterial.SetFloat("metaballIridescenceIntensity", settings.iridescenceIntensity);
			compositeMaterial.SetFloat("metaballIridescenceScale", settings.iridescenceScale);
			bool renderCaustics = ShouldRenderCaustics(settings);
			compositeMaterial.SetInt("metaballCausticsEnabled", renderCaustics ? 1 : 0);
			compositeMaterial.SetTexture("CausticTex", settings.temporalEnabled ? causticTemporalTexture : causticResolvedTexture);
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField(settings);
			compositeMaterial.SetInt("metaballDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			Texture lightDirectionTex = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTex = settings.temporalEnabled ? lightDirectionTemporalTexture : lightDirectionTexture;
			}
			compositeMaterial.SetTexture("LightDirectionTex", lightDirectionTex);
			bool renderPhaseDiffuseLight = ShouldRenderPhaseDiffuseLight(settings);
			compositeMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderPhaseDiffuseLight ? 1 : 0);
			compositeMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			compositeMaterial.SetColor("metaballPhase0DiffuseLightTint", settings.phase0Material.diffuseLightTint);
			compositeMaterial.SetColor("metaballPhase1DiffuseLightTint", settings.phase1Material.diffuseLightTint);
			compositeMaterial.SetFloat("metaballPhase0DiffuseAlbedoTintBlend", settings.phase0Material.diffuseAlbedoTintBlend);
			compositeMaterial.SetFloat("metaballPhase1DiffuseAlbedoTintBlend", settings.phase1Material.diffuseAlbedoTintBlend);
			compositeMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : settings.temporalHistoryWeight);
			compositeMaterial.SetInt("causticTemporalMotionSource", (int)settings.temporalMotionSource);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			compositeMaterial.SetInt("debugMode", (int)display.debugMode);
			compositeMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			compositeMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(compositeMaterial);
			compositeMaterial.SetVector("particleBaseLightDirection", settings.LightDirection);
			compositeMaterial.SetVector("particleLightDirection", GetDirectLightingDirection(display));
			compositeMaterial.SetColor("particleLightColor", settings.EffectiveLightColor);
			compositeMaterial.SetFloat("particleAmbientLight", settings.ambientLight);
			compositeMaterial.SetFloat("particleLightIntensity", settings.lightIntensity);
			compositeMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			compositeMaterial.SetFloat("particlePhase0Reflectance", settings.phase0Material.reflectance);
			compositeMaterial.SetFloat("particlePhase1Reflectance", settings.phase1Material.reflectance);
			compositeMaterial.SetFloat("particlePhase0Roughness", settings.phase0Material.roughness);
			compositeMaterial.SetFloat("particlePhase1Roughness", settings.phase1Material.roughness);
			compositeMaterial.SetFloat("particlePhase0Metallic", settings.phase0Material.metallic);
			compositeMaterial.SetFloat("particlePhase1Metallic", settings.phase1Material.metallic);
			compositeMaterial.SetColor("particleFresnelColor", settings.fresnelColor);
			compositeMaterial.SetFloat("particleFresnelIntensity", settings.fresnelIntensity);
			compositeMaterial.SetFloat("particleFresnelPower", settings.fresnelPower);
			compositeMaterial.SetFloat("screenSpaceReflectionStrength0", settings.phase0Material.screenSpaceReflectionStrength);
			compositeMaterial.SetFloat("screenSpaceReflectionStrength1", settings.phase1Material.screenSpaceReflectionStrength);
			compositeMaterial.SetFloat("screenSpaceReflectionDistance", settings.screenSpaceReflectionDistance * display.GetZoomScale(cam));
			compositeMaterial.SetFloat("screenSpaceReflectionEdgePower", settings.screenSpaceReflectionEdgePower);
			compositeMaterial.SetVector("particleGlowDirection", new Vector4(settings.glowDirection.x, settings.glowDirection.y, 0f, 0f));
			compositeMaterial.SetColor("particleGlowColor", settings.glowColor);
			compositeMaterial.SetFloat("particleGlowIntensity", settings.glowIntensity);
			compositeMaterial.SetFloat("particleGlowPower", settings.glowPower);
			compositeMaterial.SetFloat("particleTransmissionIntensity", settings.transmissionIntensity);
			compositeMaterial.SetFloat("particleTransmissionPower", settings.transmissionPower);
			compositeMaterial.SetFloat("particleEdgeDarkening", settings.edgeDarkening);
			compositeMaterial.SetFloat("particleEdgeDarkeningPower", settings.edgeDarkeningPower);
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
			targetCommandBuffer.SetRenderTarget(velocityPhase0AccumulationTexture);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 2, display.argsBuffer);
			targetCommandBuffer.SetRenderTarget(velocityPhase1AccumulationTexture);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 3, display.argsBuffer);

			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);
			float effectiveMotionBlurRadius = display.GetEffectiveMotionBlurRadius(cam);
			if (effectiveMotionBlurRadius > 0.001f)
			{
				velocityBlurMaterial.SetFloat("blurRadius", effectiveMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(velocityPhase0BlurTexture, velocityPhase0AccumulationTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(velocityPhase1AccumulationTexture, velocityPhase1BlurTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(velocityPhase1BlurTexture, velocityPhase1AccumulationTexture, velocityBlurMaterial);
			}

			if (ShouldRenderCaustics(display.metaballs))
			{
				BuildCaustics(display, cam, targetCommandBuffer);
			}

			targetCommandBuffer.Blit(null, finalTarget, compositeMaterial, 0);

			display.AppendVectorFieldDraw(targetCommandBuffer);
		}

		void BuildCaustics(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			ComputeShader compute = settings.computeShader;
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField(settings);
			bool renderCausticMotion = ShouldRenderCausticMotion(display, settings);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			Vector3 lightDirection = settings.LightDirection;
			float analyticBoundaryExpansion = GetAnalyticBoundaryExpansion(display);

			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 currentWorldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			Vector2 currentWorldSize = new Vector2(worldWidth, worldHeight);
			GetCausticRayRange(display, settings, width, height, lightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float rayStartOffset, out int rayCount);
			int raysPerPixel = Mathf.Max(1, settings.raysPerPixel);
			int uncappedRayCount = rayCount;
			int maxRayCount = Mathf.Max(1, MaxCausticTraceThreads / raysPerPixel);
			rayCount = Mathf.Min(rayCount, maxRayCount);
			float raySpacing = uncappedRayCount > 1 && rayCount > 1
				? (uncappedRayCount - 1f) / (rayCount - 1f)
				: 1f;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", rayCount);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayStartOffset", rayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRaySpacing", raySpacing);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", settings.raySteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", settings.rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", settings.colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", settings.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", settings.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsStepPixels", settings.stepPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassIndexOfRefraction", settings.boundaryMaterial.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassReflectance", settings.boundaryMaterial.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0IndexOfRefraction", settings.phase0Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1IndexOfRefraction", settings.phase1Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Reflectance", settings.phase0Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Reflectance", settings.phase1Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Metallic", settings.phase0Material.metallic);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Metallic", settings.phase1Material.metallic);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsFresnelStrength", settings.fresnelStrength);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", settings.stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", settings.dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionRotation", settings.dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightAngularRadius", settings.lightAngularRadiusDegrees * Mathf.Deg2Rad);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Absorption", settings.phase0Material.absorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Absorption", settings.phase1Material.absorption);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase0AbsorptionTint", settings.phase0Material.diffuseLightTint);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase1AbsorptionTint", settings.phase1Material.diffuseLightTint);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0AbsorptionTintBlend", settings.phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1AbsorptionTintBlend", settings.phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoBrightnessInfluence", settings.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoSaturationInfluence", settings.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", settings.rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", settings.temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceNormalJitterPixels", settings.surfaceNormalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", causticFrameIndex++);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && settings.useAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));

			BindCausticAccumulationTextures(targetCommandBuffer, compute, clearKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, clearKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, clearKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, clearKernel, width, height);

			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex0", velocityPhase0AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex1", velocityPhase1AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap2", display.gradientTexture2);
			BindCausticAccumulationTextures(targetCommandBuffer, compute, traceKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, traceKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, traceKernel, renderDirectionalLightField);
			DispatchCausticTrace(targetCommandBuffer, compute, traceKernel, rayCount, raysPerPixel);

			BindCausticAccumulationTextures(targetCommandBuffer, compute, resolveKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, resolveKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, resolveKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, resolveKernel, width, height);

			float rayTextureBlurScale = GetRayTextureBlurScale(settings);
			float causticBlurRadius = settings.blur * rayTextureBlurScale;
			float directionalLightFieldBlurRadius = settings.directionalLightFieldBlur * rayTextureBlurScale;
			float temporalMotionBlurRadius = settings.temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				causticBlurMaterial.SetFloat("blurRadius", causticBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(causticResolvedTexture, causticBlurTexture, causticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(causticBlurTexture, causticResolvedTexture, causticBlurMaterial);
			}
			if (renderDirectionalLightField && directionalLightFieldBlurRadius > 0.001f)
			{
				lightDirectionBlurMaterial.SetFloat("blurRadius", directionalLightFieldBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(lightDirectionBlurTexture, lightDirectionTexture, lightDirectionBlurMaterial);
			}
			RenderTexture temporalMotionTexture = causticMotionTexture;
			bool useCausticMotion = settings.temporalMotionSource == ParticleDisplay2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && settings.temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, settings.temporalMotionDilationIterations);
				float motionDilationRadius = settings.temporalMotionDilationRadius * GetRayTextureBlurScale(settings) / dilationIterations;
				compositeMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					compositeMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, compositeMaterial, 2);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == causticMotionDilatedTexture ? causticMotionDilationScratchTexture : causticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && causticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == causticMotionDilationScratchTexture
					? causticMotionDilatedTexture
					: causticMotionDilationScratchTexture;
				causticMotionBlurMaterial.SetFloat("blurRadius", temporalMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(temporalMotionTexture, motionBlurScratch, causticMotionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(motionBlurScratch, temporalMotionTexture, causticMotionBlurMaterial);
			}
			compositeMaterial.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : causticMotionTexture);

			if (settings.temporalEnabled)
			{
				if (clearCausticHistory || !hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionHistoryTexture);
					}
					clearCausticHistory = false;
					causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : settings.temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					compositeMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					compositeMaterial.SetInt("causticTemporalMotionSource", (int)settings.temporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalTexture("CausticHistoryTex", causticHistoryTexture);
					targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, compositeMaterial, 1);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						targetCommandBuffer.SetGlobalTexture("CausticHistoryTex", lightDirectionHistoryTexture);
						targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture, compositeMaterial, 1);
						targetCommandBuffer.Blit(lightDirectionTemporalTexture, lightDirectionHistoryTexture);
					}
					causticTemporalFrameCount = nextFrameCount;
				}

				previousCausticWorldCenter = currentWorldCenter;
				previousCausticWorldSize = currentWorldSize;
				hasPreviousCausticCamera = true;
			}
			else
			{
				hasPreviousCausticCamera = false;
				causticTemporalFrameCount = 0;
			}

			if (ShouldRenderPhaseDiffuseLight(settings))
			{
				RenderPhaseDiffuseLight(display, targetCommandBuffer, settings, settings.temporalEnabled ? causticTemporalTexture : causticResolvedTexture, currentWorldCenter, currentWorldSize);
			}
		}

		void RenderPhaseDiffuseLight(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			ComputeShader compute = settings.phaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int diffuseKernel = compute.FindKernel("Diffuse");
			int width = softLightTexture0.width;
			int height = softLightTexture0.height;

			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, diffuseKernel, width, height, sharpCaustics, settings, currentWorldCenter, currentWorldSize);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", softLightTexture0);
			DispatchCaustics(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = softLightTexture0;
			RenderTexture target = softLightTexture1;
			int iterations = Mathf.Max(1, settings.phaseDiffuseLightIterations);
			for (int i = 0; i < iterations; i++)
			{
				SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, diffuseKernel, width, height, sharpCaustics, settings, currentWorldCenter, currentWorldSize);
				targetCommandBuffer.SetComputeTextureParam(compute, diffuseKernel, "SoftLightRead", source);
				targetCommandBuffer.SetComputeTextureParam(compute, diffuseKernel, "SoftLightWrite", target);
				DispatchCaustics(targetCommandBuffer, compute, diffuseKernel, width, height);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			compositeMaterial.SetTexture("SoftLightTex", source);
		}

		void SetPhaseDiffuseCommonParams(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int diffuseKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings settings, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, diffuseKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, diffuseKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", sharpCaustics.width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", sharpCaustics.height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", settings.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", settings.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBlendWidth", settings.phaseBlendWidth);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", settings.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", settings.phaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", settings.phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", settings.phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", settings.lightIntensity);
			targetCommandBuffer.SetComputeFloatParam(compute, "diffusionRateA", settings.phase0Material.diffuseDiffusionRate);
			targetCommandBuffer.SetComputeFloatParam(compute, "diffusionRateB", settings.phase1Material.diffuseDiffusionRate);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
		}

		void DispatchCaustics(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchCausticTrace(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), MaxCausticTraceThreads);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)CausticTraceThreadGroupSize), 1, 1);
			}
		}

		float GetAnalyticBoundaryExpansion(ParticleDisplay2D display)
		{
			return display.metaballs.analyticBoundaryPadding;
		}

		static float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings settings)
		{
			return Mathf.Max(settings.renderTextureScale * settings.textureScale, 0.0001f);
		}

		Vector3 GetDirectLightingDirection(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			Vector3 lightDirection = settings.LightDirection;
			if (!settings.refractDirectLightAtAnalyticBoundary || !display.sim.useEllipticalBounds)
			{
				return lightDirection;
			}

			Vector2 lightXY = new Vector2(lightDirection.x, lightDirection.y);
			float planarLength = lightXY.magnitude;
			if (planarLength <= 0.0001f)
			{
				return lightDirection;
			}

			Vector2 directionToLight = lightXY / planarLength;
			if (!TryGetAnalyticBoundaryHitFromCenter(display, directionToLight, out Vector2 hitPoint, out Vector2 outwardNormal))
			{
				return lightDirection;
			}

			Vector2 incomingRayDirection = -directionToLight;
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(settings.boundaryMaterial.indexOfRefraction, 1.0001f));
			Vector2 refractedLightXY = -refractedRayDirection * planarLength;
			return new Vector3(refractedLightXY.x, refractedLightXY.y, lightDirection.z).normalized;
		}

		bool TryGetAnalyticBoundaryHitFromCenter(ParticleDisplay2D display, Vector2 directionToLight, out Vector2 hitPoint, out Vector2 outwardNormal)
		{
			float expansion = GetAnalyticBoundaryExpansion(display);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			float cutY = display.sim.obstacleY - expansion;
			float topY = center.y + radii.y;
			Vector2 start = new Vector2(center.x, (topY + cutY) * 0.5f);
			hitPoint = Vector2.zero;
			outwardNormal = Vector2.up;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return false;
			}

			float bestT = float.PositiveInfinity;
			bool hasHit = false;

			float invRx2 = 1f / (radii.x * radii.x);
			float invRy2 = 1f / (radii.y * radii.y);
			float a = directionToLight.x * directionToLight.x * invRx2 + directionToLight.y * directionToLight.y * invRy2;
			Vector2 startRel = start - center;
			float b = 2f * (startRel.x * directionToLight.x * invRx2 + startRel.y * directionToLight.y * invRy2);
			float c = startRel.x * startRel.x * invRx2 + startRel.y * startRel.y * invRy2 - 1f;
			float discriminant = b * b - 4f * a * c;
			if (discriminant >= 0f && a > 0.000001f)
			{
				float sqrtDiscriminant = Mathf.Sqrt(discriminant);
				TryUseAnalyticBoundaryCandidate(start, (-b - sqrtDiscriminant) / (2f * a), directionToLight, center, radii, cutY, false, ref bestT, ref hitPoint, ref outwardNormal, ref hasHit);
				TryUseAnalyticBoundaryCandidate(start, (-b + sqrtDiscriminant) / (2f * a), directionToLight, center, radii, cutY, false, ref bestT, ref hitPoint, ref outwardNormal, ref hasHit);
			}

			if (directionToLight.y < -0.0001f)
			{
				float cutT = (cutY - start.y) / directionToLight.y;
				TryUseAnalyticBoundaryCandidate(start, cutT, directionToLight, center, radii, cutY, true, ref bestT, ref hitPoint, ref outwardNormal, ref hasHit);
			}

			return hasHit;
		}

		void TryUseAnalyticBoundaryCandidate(Vector2 start, float t, Vector2 direction, Vector2 center, Vector2 radii, float cutY, bool isCut, ref float bestT, ref Vector2 hitPoint, ref Vector2 outwardNormal, ref bool hasHit)
		{
			if (t <= 0.0001f || t >= bestT)
			{
				return;
			}

			Vector2 point = start + direction * t;
			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			if (isCut)
			{
				if (ellipseValue > 1.0001f)
				{
					return;
				}

				outwardNormal = Vector2.down;
			}
			else
			{
				if (point.y < cutY - 0.0001f)
				{
					return;
				}

				Vector2 ellipseNormal = new Vector2(rel.x / (radii.x * radii.x), rel.y / (radii.y * radii.y));
				if (ellipseNormal.sqrMagnitude <= 0.000001f)
				{
					return;
				}

				outwardNormal = ellipseNormal.normalized;
			}

			bestT = t;
			hitPoint = point;
			hasHit = true;
		}

		Vector2 Refract2D(Vector2 rayDirection, Vector2 normal, float eta)
		{
			if (Vector2.Dot(rayDirection, normal) > 0f)
			{
				normal = -normal;
			}

			float cosI = Vector2.Dot(-rayDirection, normal);
			float sinT2 = eta * eta * Mathf.Max(0f, 1f - cosI * cosI);
			if (sinT2 > 1f)
			{
				return (rayDirection - 2f * Vector2.Dot(rayDirection, normal) * normal).normalized;
			}

			float cosT = Mathf.Sqrt(Mathf.Max(0f, 1f - sinT2));
			return (eta * rayDirection + (eta * cosI - cosT) * normal).normalized;
		}

		void GetCausticRayRange(ParticleDisplay2D display, ParticleDisplay2D.MetaballSettings settings, int width, int height, Vector3 lightDirection, Vector2 worldCenter, Vector2 worldSize, float analyticBoundaryExpansion, out float startOffset, out int rayCount)
		{
			Vector2 lightXY = new Vector2(-lightDirection.x, -lightDirection.y);
			Vector2 rayDir = lightXY.sqrMagnitude > 0.0001f ? lightXY.normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(width * width + height * height);
			float screenMinOffset = -fullSpan * 0.5f;
			float screenMaxOffset = fullSpan * 0.5f;
			startOffset = screenMinOffset;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!display.sim.useEllipticalBounds || !settings.useAnalyticBoundary)
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

			float angularPadding = Mathf.Sin(settings.lightAngularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(settings.stepPixels * 4f + angularPadding, 2f);
			float clippedMinOffset = Mathf.Max(minOffset - padding, screenMinOffset);
			float clippedMaxOffset = Mathf.Min(maxOffset + padding, screenMaxOffset);
			if (clippedMaxOffset <= clippedMinOffset)
			{
				rayCount = 0;
				return;
			}

			startOffset = Mathf.Floor(clippedMinOffset);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(clippedMaxOffset - startOffset));
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
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulationBuffer);
		}

		void BindCausticMotionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticMotionAccum", causticMotionAccumulationBuffer);
		}

		void BindLightDirectionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, bool renderDirectionalLightField)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "LightDirectionAccum", renderDirectionalLightField ? lightDirectionAccumulationBuffer : lightDirectionAccumulationFallbackBuffer);
		}

		bool ShouldRenderCaustics(ParticleDisplay2D.MetaballSettings settings)
		{
			return settings.enabled && settings.computeShader != null;
		}

		bool ShouldRenderPhaseDiffuseLight(ParticleDisplay2D.MetaballSettings settings)
		{
			return ShouldRenderCaustics(settings)
			       && settings.phaseDiffuseLightEnabled
			       && settings.phaseDiffuseLightCompute != null
			       && (
				       settings.phase0Material.diffuseScatterStrength > 0f
				       || settings.phase1Material.diffuseScatterStrength > 0f
			       );
		}

		bool ShouldRenderDirectionalLightField(ParticleDisplay2D.MetaballSettings settings)
		{
			return ShouldRenderCaustics(settings) && settings.directionalLightFieldEnabled;
		}

		bool ShouldRenderCausticMotion(ParticleDisplay2D display, ParticleDisplay2D.MetaballSettings settings)
		{
			return ShouldRenderCaustics(settings)
			       && (settings.temporalMotionSource == ParticleDisplay2D.TemporalMotionSource.CausticMotion
			           || display.debugMode == ParticleDisplay2D.DebugVisualization.CausticMotion);
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
