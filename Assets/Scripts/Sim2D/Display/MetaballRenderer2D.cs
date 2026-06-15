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
		const float TwoPi = 6.28318530718f;
		const int PointLightBoundaryEllipseSamples = 128;
		const int PointLightBoundaryCutSamples = 31;

		Material metaballMaterial;
		Material debugMaterial;
		Material temporalMaterial;
		Material blurMaterial;
		Material velocityBlurMaterial;
		Material causticBlurMaterial;
		Material causticMotionBlurMaterial;
		Material lightDirectionBlurMaterial;
		Material radianceCascadeMaterial;
		readonly MetaballMaterialRenderer2D materialRenderer = new MetaballMaterialRenderer2D();
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
		readonly float[] pointLightBoundaryAngles = new float[PointLightBoundaryEllipseSamples + PointLightBoundaryCutSamples + 2];
		bool commandBufferAttached;
		bool clearCausticHistory;
		bool hasPreviousCausticCamera;
		int causticFrameIndex;
		int causticTemporalFrameCount;
		Vector2 previousCausticWorldCenter;
		Vector2 previousCausticWorldSize;
		ParticleFluidRenderRegion2D currentMaterialRenderRegion;
		ParticleFluidRenderRegion2D currentCausticRenderRegion;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || blurMaterial == null || velocityBlurMaterial == null || causticBlurMaterial == null || causticMotionBlurMaterial == null || lightDirectionBlurMaterial == null)
			{
				RemoveCommandBuffer();
				return;
			}

			EnsureCommandBuffer(cam);
			commandBuffer.Clear();
			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyDebugSettings(display, cam);
			ApplyTemporalSettings(display);
			ApplyMaterialSettings(display, cam);
			ApplyLightingSettings(display, cam);
			BuildCommandBuffer(display, cam, commandBuffer, BuiltinRenderTextureType.CameraTarget);
		}

		public void Record(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || blurMaterial == null || velocityBlurMaterial == null || causticBlurMaterial == null || causticMotionBlurMaterial == null || lightDirectionBlurMaterial == null || cam == null || targetCommandBuffer == null || display.mesh == null || display.argsBuffer == null)
			{
				return;
			}

			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyDebugSettings(display, cam);
			ApplyTemporalSettings(display);
			ApplyMaterialSettings(display, cam);
			ApplyLightingSettings(display, cam);
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
			materialRenderer.Release();
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

			if (debugMaterial != null)
			{
				Object.DestroyImmediate(debugMaterial);
				debugMaterial = null;
			}

			if (temporalMaterial != null)
			{
				Object.DestroyImmediate(temporalMaterial);
				temporalMaterial = null;
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

			if (radianceCascadeMaterial != null)
			{
				Object.DestroyImmediate(radianceCascadeMaterial);
				radianceCascadeMaterial = null;
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
			Shader debugShader = display.metaballs.debugShader != null ? display.metaballs.debugShader : Shader.Find("Hidden/Particle2DMetaballDebug");
			EnsureMaterial(ref debugMaterial, debugShader);
			Shader materialShader = display.metaballs.materialShader != null ? display.metaballs.materialShader : Shader.Find("Hidden/Particle2DMetaballMaterial");
			materialRenderer.EnsureMaterial(materialShader);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				Shader lightingShader = lighting.lightingShader != null ? lighting.lightingShader : Shader.Find("Hidden/Particle2DParticleFluidLighting");
				lighting.EnsureMaterial(lightingShader);
			}
			Shader temporalShader = lighting != null && lighting.temporalShader != null ? lighting.temporalShader : Shader.Find("Hidden/Particle2DMetaballTemporal");
			EnsureMaterial(ref temporalMaterial, temporalShader);
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref velocityBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticMotionBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref lightDirectionBlurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref radianceCascadeMaterial, lighting != null ? lighting.radianceCascadeShader : null);
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
			metaballMaterial.SetVector("metaballRenderWorldCenter", new Vector4(currentMaterialRenderRegion.WorldCenter.x, currentMaterialRenderRegion.WorldCenter.y, 0f, 0f));
			metaballMaterial.SetVector("metaballRenderWorldSize", new Vector4(currentMaterialRenderRegion.WorldSize.x, currentMaterialRenderRegion.WorldSize.y, 0f, 0f));
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
			int fullWidth = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * settings.renderTextureScale));
			int fullHeight = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * settings.renderTextureScale));
			currentMaterialRenderRegion = GetMaterialRenderRegion(display, cam, fullWidth, fullHeight);
			int width = currentMaterialRenderRegion.PixelWidth;
			int height = currentMaterialRenderRegion.PixelHeight;

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Accumulation");
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Blur");
			ComputeHelper.CreateRenderTexture(ref velocityPhase0AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Accumulation");
			ComputeHelper.CreateRenderTexture(ref velocityPhase0BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Blur");
			ComputeHelper.CreateRenderTexture(ref velocityPhase1AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Accumulation");
			ComputeHelper.CreateRenderTexture(ref velocityPhase1BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Blur");
			if (ShouldUseMaterialPipeline(display))
			{
				materialRenderer.EnsureRenderTextures(currentMaterialRenderRegion);
			}
			else
			{
				materialRenderer.ReleaseMaterialTextures();
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			currentCausticRenderRegion = GetScaledRenderRegion(currentMaterialRenderRegion, lighting != null ? lighting.textureScale : 1f);
			if (ShouldRenderCaustics(lighting) && (lighting != null || ShouldRenderCausticDebug(lighting)))
			{
				bool renderDirectionalLightField = ShouldRenderDirectionalLightField(lighting);
				bool renderPhaseDiffuseLight = ShouldRenderPhaseDiffuseLight(lighting);
				bool renderRadianceCascadeLight = ShouldRenderRadianceCascadeLight(lighting);
				bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
				int causticWidth = currentCausticRenderRegion.PixelWidth;
				int causticHeight = currentCausticRenderRegion.PixelHeight;
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
				if (renderSoftLight)
				{
					float softLightScale = renderRadianceCascadeLight ? lighting.radianceCascadeTextureScale : lighting.phaseDiffuseLightTextureScale;
					int softLightWidth = Mathf.Max(1, Mathf.RoundToInt(causticWidth * softLightScale));
					int softLightHeight = Mathf.Max(1, Mathf.RoundToInt(causticHeight * softLightScale));
					ComputeHelper.CreateRenderTexture(ref softLightTexture0, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 0");
					ComputeHelper.CreateRenderTexture(ref softLightTexture1, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 1");
				}
				else
				{
					ReleasePhaseDiffuseLightTextures();
				}
				if (lighting.temporalEnabled)
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

		void ApplyDebugSettings(ParticleDisplay2D display, Camera cam)
		{
			if (debugMaterial == null)
			{
				return;
			}

			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			debugMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			debugMaterial.SetFloat("edgeSoftness", settings.edgeSoftness);
			debugMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			debugMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			debugMaterial.SetFloat("phaseBiasNormalStrength", settings.phaseBiasNormalStrength);
			debugMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			debugMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			debugMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			debugMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			debugMaterial.SetFloat("analyticBoundaryExpansion", GetAnalyticBoundaryExpansion(display));
			debugMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			debugMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			debugMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			debugMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture);
			debugMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture);
			debugMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			debugMaterial.SetTexture("ColourMap", display.gradientTexture);
			debugMaterial.SetTexture("ColourMap2", display.gradientTexture2);
			debugMaterial.SetTexture("DebugHeatMap", display.debugHeatMapTexture);
			debugMaterial.SetTexture("DebugSignedHeatMap", display.debugSignedHeatMapTexture);

			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			float effectiveConfiguredBlurRadius = display.EffectiveConfiguredBlurRadius;
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			debugMaterial.SetVector("metaballWorldCenter", new Vector4(worldCenter.x, worldCenter.y, 0f, 0f));
			debugMaterial.SetVector("metaballWorldSize", new Vector4(worldWidth, worldHeight, 0f, 0f));
			debugMaterial.SetInt("screenSpaceRefractionCanCrossPhases", lighting != null && lighting.screenSpaceRefractionCanCrossPhases ? 1 : 0);
			debugMaterial.SetTexture("CausticTex", lighting != null && lighting.temporalEnabled ? causticTemporalTexture : causticResolvedTexture);
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField(lighting);
			debugMaterial.SetInt("metaballDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			Texture lightDirectionTex = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTex = lighting.temporalEnabled ? lightDirectionTemporalTexture : lightDirectionTexture;
			}
			debugMaterial.SetTexture("LightDirectionTex", lightDirectionTex);
			bool renderSoftLight = ShouldRenderPhaseDiffuseLight(lighting) || ShouldRenderRadianceCascadeLight(lighting);
			debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			debugMaterial.SetInt("metaballSoftLightPhase0Only", ShouldRenderRadianceCascadeLight(lighting) ? 1 : 0);
			debugMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			debugMaterial.SetColor("metaballPhase0DiffuseLightTint", lighting != null ? lighting.phase0Material.diffuseLightTint : Color.white);
			debugMaterial.SetColor("metaballPhase1DiffuseLightTint", lighting != null ? lighting.phase1Material.diffuseLightTint : Color.white);
			debugMaterial.SetFloat("metaballPhase0DiffuseAlbedoTintBlend", lighting != null ? lighting.phase0Material.diffuseAlbedoTintBlend : 0f);
			debugMaterial.SetFloat("metaballPhase1DiffuseAlbedoTintBlend", lighting != null ? lighting.phase1Material.diffuseAlbedoTintBlend : 0f);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			debugMaterial.SetInt("debugMode", GetDebugShaderMode(display, lighting));
			debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(debugMaterial);
			debugMaterial.SetFloat("particleCausticDebugExposure", GetCausticDebugExposure(lighting));
			debugMaterial.SetInt("particleCausticRegionEnabled", currentCausticRenderRegion.IsCropped ? 1 : 0);
			debugMaterial.SetVector("particleCausticUvRect", currentCausticRenderRegion.SourceUvRect);
			debugMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void ApplyTemporalSettings(ParticleDisplay2D display)
		{
			if (temporalMaterial == null)
			{
				return;
			}

			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			temporalMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture);
			temporalMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			temporalMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			temporalMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			temporalMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			temporalMaterial.SetInt("debugMode", GetDebugShaderMode(display, lighting));
			temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : lighting != null ? lighting.temporalHistoryWeight : 0f);
			temporalMaterial.SetInt("causticTemporalMotionSource", lighting != null ? (int)lighting.temporalMotionSource : 0);
		}

		void ApplyMaterialSettings(ParticleDisplay2D display, Camera cam)
		{
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
			materialRenderer.ApplySettings(display, cam, combinedAccumulationTexture, normalAccumulationTexture, currentMaterialRenderRegion, GetAnalyticBoundaryExpansion(display), effectiveNormalStrength, GetActiveLighting(display));
		}

		void ApplyLightingSettings(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting == null)
			{
				return;
			}

			bool renderCaustics = ShouldRenderCaustics(lighting);
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField(lighting);
			bool renderSoftLight = ShouldRenderPhaseDiffuseLight(lighting) || ShouldRenderRadianceCascadeLight(lighting);
			Texture causticTexture = lighting.temporalEnabled ? causticTemporalTexture : causticResolvedTexture;
			Texture lightDirectionTextureForLighting = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTextureForLighting = lighting.temporalEnabled ? lightDirectionTemporalTexture : lightDirectionTexture;
			}

			lighting.ApplySettings(
				display,
				cam,
				renderCaustics,
				renderDirectionalLightField,
				renderSoftLight,
				ShouldRenderRadianceCascadeLight(lighting),
				causticTexture,
				lightDirectionTextureForLighting,
				GetAnalyticBoundaryExpansion(display),
				currentMaterialRenderRegion,
				currentCausticRenderRegion,
				GetDirectLightingDirection(display, lighting, lighting.LightDirection),
				GetDirectLightingDirection(display, lighting, lighting.SecondaryLightDirection),
				GetDirectLightingDirection(display, lighting, lighting.TertiaryLightDirection)
			);
		}

		void BuildCommandBuffer(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			targetCommandBuffer.BeginSample("Metaballs/Accumulate");
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
			targetCommandBuffer.EndSample("Metaballs/Accumulate");

			targetCommandBuffer.BeginSample("Metaballs/Blur Density And Normals");
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			targetCommandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			targetCommandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);
			targetCommandBuffer.EndSample("Metaballs/Blur Density And Normals");
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float effectiveMotionBlurRadius = display.GetEffectiveMotionBlurRadius(cam, lighting != null ? lighting.motionBlurRadius : 0f);
			if (effectiveMotionBlurRadius > 0.001f)
			{
				targetCommandBuffer.BeginSample("Metaballs/Blur Particle Motion");
				velocityBlurMaterial.SetFloat("blurRadius", effectiveMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(velocityPhase0BlurTexture, velocityPhase0AccumulationTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(velocityPhase1AccumulationTexture, velocityPhase1BlurTexture, velocityBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(velocityPhase1BlurTexture, velocityPhase1AccumulationTexture, velocityBlurMaterial);
				targetCommandBuffer.EndSample("Metaballs/Blur Particle Motion");
			}

			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.IsReady;
			if ((useLighting || ShouldRenderCausticDebug(lighting)) && ShouldRenderCaustics(lighting))
			{
				BuildCaustics(display, cam, targetCommandBuffer);
			}

			if (useMaterialPipeline && materialRenderer.IsReady)
			{
				targetCommandBuffer.BeginSample("Metaballs/Material Pipeline");
				materialRenderer.Render(targetCommandBuffer);
				if (useLighting)
				{
					materialRenderer.MaterialMaps.BindTo(lighting, currentMaterialRenderRegion);
					lighting.Render(targetCommandBuffer, finalTarget, cam);
				}
				else
				{
					materialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, currentMaterialRenderRegion);
				}
				targetCommandBuffer.EndSample("Metaballs/Material Pipeline");
			}
			else
			{
				if (debugMaterial != null)
				{
					targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
					targetCommandBuffer.Blit(null, finalTarget, debugMaterial, 0);
					targetCommandBuffer.EndSample("Metaballs/Debug Composite");
				}
			}

			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.AppendVectorFieldDraw(targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		void BuildCaustics(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Metaballs/Caustics");
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ParticleFluidLighting2D settings = GetActiveLighting(display);
			ComputeShader compute = settings.computeShader;
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField(settings);
			bool renderCausticMotion = ShouldRenderCausticMotion(settings);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			Vector3 lightDirection = settings.LightDirection;
			Vector3 secondaryLightDirection = settings.SecondaryLightDirection;
			Vector3 tertiaryLightDirection = settings.TertiaryLightDirection;
			bool primaryLightEnabled = SupportsCausticRaymarch(settings.PrimaryLight);
			bool secondaryLightEnabled = SupportsCausticRaymarch(settings.SecondaryLight);
			bool tertiaryLightEnabled = SupportsCausticRaymarch(settings.TertiaryLight);
			float analyticBoundaryExpansion = GetAnalyticBoundaryExpansion(display);

			Vector2 currentWorldCenter = currentCausticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = currentCausticRenderRegion.WorldSize;
			GetCausticRayRange(display, settings, width, height, lightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float primaryRayStartOffset, out int primaryRangeRayCount);
			GetCausticRayRange(display, settings, width, height, secondaryLightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float secondaryRayStartOffset, out int secondaryRangeRayCount);
			GetCausticRayRange(display, settings, width, height, tertiaryLightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float tertiaryRayStartOffset, out int tertiaryRangeRayCount);
			GetCausticPointRaySpan(display, settings.PrimaryLight, settings.useAnalyticBoundary, out float primaryPointAngleStart, out float primaryPointAngleRange);
			GetCausticPointRaySpan(display, settings.SecondaryLight, settings.useAnalyticBoundary, out float secondaryPointAngleStart, out float secondaryPointAngleRange);
			GetCausticPointRaySpan(display, settings.TertiaryLight, settings.useAnalyticBoundary, out float tertiaryPointAngleStart, out float tertiaryPointAngleRange);
			if (settings.PrimaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				primaryRangeRayCount = GetCausticPointRayCount(settings.PrimaryLight, currentWorldSize, width, height);
				primaryRayStartOffset = 0f;
			}
			if (settings.SecondaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				secondaryRangeRayCount = GetCausticPointRayCount(settings.SecondaryLight, currentWorldSize, width, height);
				secondaryRayStartOffset = 0f;
			}
			if (settings.TertiaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point)
			{
				tertiaryRangeRayCount = GetCausticPointRayCount(settings.TertiaryLight, currentWorldSize, width, height);
				tertiaryRayStartOffset = 0f;
			}
			int raysPerPixel = Mathf.Max(1, settings.raysPerPixel);
			int maxRayCount = Mathf.Max(1, MaxCausticTraceThreads / raysPerPixel);
			float primaryLightWeight = primaryLightEnabled ? LightSampleWeight(settings.PrimaryLight, settings.EffectiveLightColor) : 0f;
			float secondaryLightWeight = secondaryLightEnabled ? LightSampleWeight(settings.SecondaryLight, settings.EffectiveSecondaryLightColor) : 0f;
			float tertiaryLightWeight = tertiaryLightEnabled ? LightSampleWeight(settings.TertiaryLight, settings.EffectiveTertiaryLightColor) : 0f;
			int enabledRangeRayCount = Mathf.Max(
				primaryLightWeight > 0f ? primaryRangeRayCount : 0,
				secondaryLightWeight > 0f ? secondaryRangeRayCount : 0,
				tertiaryLightWeight > 0f ? tertiaryRangeRayCount : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0
				? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount))
				: 1;
			GetLightRayShares(primaryLightWeight, secondaryLightWeight, tertiaryLightWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare);
			int secondarySubRaysPerPixel = secondaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * secondaryShare)
				: 0;
			secondarySubRaysPerPixel = Mathf.Clamp(secondarySubRaysPerPixel, 0, raysPerPixel);
			int tertiarySubRaysPerPixel = tertiaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * tertiaryShare)
				: 0;
			tertiarySubRaysPerPixel = Mathf.Clamp(tertiarySubRaysPerPixel, 0, raysPerPixel - secondarySubRaysPerPixel);
			if (AnyEnabledCausticPointLight(settings))
			{
				secondarySubRaysPerPixel = 0;
				tertiarySubRaysPerPixel = 0;
			}
			int primarySubRaysPerPixel = primaryShare > 0f ? Mathf.Max(0, raysPerPixel - secondarySubRaysPerPixel - tertiarySubRaysPerPixel) : 0;
			bool splitBySubRay = secondarySubRaysPerPixel > 0 || tertiarySubRaysPerPixel > 0;
			int secondaryRayBudget = secondaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * secondaryShare) : 0;
			secondaryRayBudget = Mathf.Clamp(secondaryRayBudget, 0, Mathf.Min(totalRayBudget, secondaryRangeRayCount));
			int tertiaryRayBudget = tertiaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * tertiaryShare) : 0;
			tertiaryRayBudget = Mathf.Clamp(tertiaryRayBudget, 0, Mathf.Min(totalRayBudget - secondaryRayBudget, tertiaryRangeRayCount));
			int primaryRayBudget = primaryShare > 0f
				? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - secondaryRayBudget - tertiaryRayBudget)
				: 0;
			primaryRayBudget = Mathf.Clamp(primaryRayBudget, 0, primaryRangeRayCount);
			float primaryRaySpacing = primaryRangeRayCount > 1 && primaryRayBudget > 1
				? (primaryRangeRayCount - 1f) / (primaryRayBudget - 1f)
				: 1f;
			int secondarySpacingRayCount = splitBySubRay ? totalRayBudget : secondaryRayBudget;
			float secondaryRaySpacing = secondaryRangeRayCount > 1 && secondarySpacingRayCount > 1
				? (secondaryRangeRayCount - 1f) / (secondarySpacingRayCount - 1f)
				: 1f;
			int tertiarySpacingRayCount = splitBySubRay ? totalRayBudget : tertiaryRayBudget;
			float tertiaryRaySpacing = tertiaryRangeRayCount > 1 && tertiarySpacingRayCount > 1
				? (tertiaryRangeRayCount - 1f) / (tertiarySpacingRayCount - 1f)
				: 1f;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", totalRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimaryRayCount", primaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryRayCount", secondaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryRayCount", tertiaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimarySubRaysPerPixel", primarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondarySubRaysPerPixel", secondarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiarySubRaysPerPixel", tertiarySubRaysPerPixel);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayStartOffset", primaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRaySpacing", primaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRayStartOffset", secondaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRaySpacing", secondaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRayStartOffset", tertiaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRaySpacing", tertiaryRaySpacing);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", settings.raySteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", settings.rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", settings.colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
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
			targetCommandBuffer.SetComputeIntParam(compute, "causticsLightType", (int)settings.PrimaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightTemperatureKelvin", settings.PrimaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightDispersionScale", GetSaturationDispersionScale(settings.PrimaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightPoint", GetPointLightVector(settings.PrimaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointFalloff", settings.PrimaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleStart", primaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleRange", primaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightMultiplier", primaryLightWeight > 0f ? GetCausticMultiplier(settings.EffectiveLightColor, settings.PrimaryLight.intensity) : Vector4.zero);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightEnabled", secondarySubRaysPerPixel > 0 || secondaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightType", (int)settings.SecondaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightTemperatureKelvin", settings.SecondaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightDispersionScale", GetSaturationDispersionScale(settings.SecondaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightPoint", GetPointLightVector(settings.SecondaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointFalloff", settings.SecondaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleStart", secondaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleRange", secondaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightDirection", new Vector4(secondaryLightDirection.x, secondaryLightDirection.y, secondaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightMultiplier", GetCausticMultiplier(settings.EffectiveSecondaryLightColor, settings.SecondaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightEnabled", tertiarySubRaysPerPixel > 0 || tertiaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightType", (int)settings.TertiaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightTemperatureKelvin", settings.TertiaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightDispersionScale", GetSaturationDispersionScale(settings.TertiaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightPoint", GetPointLightVector(settings.TertiaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointFalloff", settings.TertiaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleStart", tertiaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleRange", tertiaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightDirection", new Vector4(tertiaryLightDirection.x, tertiaryLightDirection.y, tertiaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightMultiplier", GetCausticMultiplier(settings.EffectiveTertiaryLightColor, settings.TertiaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && settings.useAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

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
			DispatchCausticTrace(targetCommandBuffer, compute, traceKernel, totalRayBudget, raysPerPixel);

			BindCausticAccumulationTextures(targetCommandBuffer, compute, resolveKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, resolveKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, resolveKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, resolveKernel, width, height);

			float rayTextureBlurScale = GetRayTextureBlurScale(surface, settings);
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
			bool useCausticMotion = temporalMaterial != null && settings.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && settings.temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, settings.temporalMotionDilationIterations);
				float motionDilationRadius = settings.temporalMotionDilationRadius * GetRayTextureBlurScale(surface, settings) / dilationIterations;
				temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, temporalMaterial, 1);
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
			debugMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : causticMotionTexture);

			if (settings.temporalEnabled && temporalMaterial != null)
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
					temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					temporalMaterial.SetInt("causticTemporalMotionSource", (int)settings.temporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						temporalMaterial.SetTexture("CausticHistoryTex", lightDirectionHistoryTexture);
						temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture, temporalMaterial, 0);
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

			if (ShouldRenderRadianceCascadeLight(settings))
			{
				RenderRadianceCascadeLight(display, cam, targetCommandBuffer, surface, settings, settings.temporalEnabled ? causticTemporalTexture : causticResolvedTexture, currentWorldCenter, currentWorldSize);
			}
			else if (ShouldRenderPhaseDiffuseLight(settings))
			{
				RenderPhaseDiffuseLight(display, targetCommandBuffer, surface, settings, settings.temporalEnabled ? causticTemporalTexture : causticResolvedTexture, currentWorldCenter, currentWorldSize);
			}
			targetCommandBuffer.EndSample("Metaballs/Caustics");
		}

		void RenderRadianceCascadeLight(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			targetCommandBuffer.BeginSample("Metaballs/Radiance Cascades");
			int width = softLightTexture0.width;
			int height = softLightTexture0.height;
			int cascadeCount = Mathf.Clamp(settings.radianceCascadeCount, 1, 6);
			RenderTexture source = softLightTexture0;
			RenderTexture target = softLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeCommonParams(display, cam, targetCommandBuffer, surface, settings, sharpCaustics, currentWorldCenter, currentWorldSize, width, height);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, radianceCascadeMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			debugMaterial?.SetTexture("SoftLightTex", source);
			GetActiveLighting(display)?.SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Radiance Cascades");
		}

		void SetRadianceCascadeCommonParams(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize, int width, int height)
		{
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics);
			targetCommandBuffer.SetGlobalTexture("CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("_Aspect", new Vector4(1f, width / Mathf.Max(height, 1f), 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(settings.radianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(settings.radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(settings.radianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", settings.radianceCascadeIntensity);
			targetCommandBuffer.SetGlobalInt("radianceCascadeAbsorption", settings.radianceCascadeAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetGlobalFloat("edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetGlobalFloat("phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthA", settings.phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthB", settings.phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("lightIntensity", 1f);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", settings.phase0Material.absorption);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1Absorption", settings.phase1Material.absorption);
			targetCommandBuffer.SetGlobalVector("causticsPhase0AbsorptionTint", settings.phase0Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalVector("causticsPhase1AbsorptionTint", settings.phase1Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0AbsorptionTintBlend", settings.phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1AbsorptionTintBlend", settings.phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", settings.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", settings.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetGlobalInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetGlobalFloat("analyticBoundaryExpansion", GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		void RenderPhaseDiffuseLight(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			ComputeShader compute = settings.phaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int gaussianHorizontalKernel = compute.FindKernel("MaskedGaussianHorizontal");
			int gaussianVerticalKernel = compute.FindKernel("MaskedGaussianVertical");
			int width = softLightTexture0.width;
			int height = softLightTexture0.height;

			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", softLightTexture0);
			DispatchCaustics(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = softLightTexture0;
			RenderTexture target = softLightTexture1;
			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianHorizontalKernel, width, height);

			RenderTexture previousSource = source;
			source = target;
			target = previousSource;

			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianVerticalKernel, width, height);

			previousSource = source;
			source = target;
			target = previousSource;

			debugMaterial?.SetTexture("SoftLightTex", source);
			GetActiveLighting(display)?.SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
		}

		void SetPhaseDiffuseCommonParams(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int gaussianHorizontalKernel, int gaussianVerticalKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", sharpCaustics.width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", sharpCaustics.height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBlendWidth", surface.phaseBlendWidth);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", settings.phaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", settings.phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", settings.phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", 1f);
			float gaussianRadiusScale = GetRayTextureBlurScale(surface, settings) * Mathf.Max(settings.phaseDiffuseLightTextureScale, 0.0001f);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius0", settings.phase0Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius1", settings.phase1Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
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

		ParticleFluidRenderRegion2D GetMaterialRenderRegion(ParticleDisplay2D display, Camera cam, int fullWidth, int fullHeight)
		{
			ParticleFluidRenderRegion2D fullRegion = ParticleFluidRenderRegion2D.Full(cam, fullWidth, fullHeight);
			if (display == null || cam == null || !display.sim.useEllipticalBounds || !ShouldUseCroppedRenderRegion(display))
			{
				return fullRegion;
			}

			float expansion = GetAnalyticBoundaryExpansion(display);
			float cameraWorldUnitsPerPixel = fullRegion.WorldSize.y / Mathf.Max(fullHeight, 1);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return fullRegion;
			}

			float cutY = display.sim.obstacleY - expansion;
			Vector2 cameraMin = fullRegion.WorldCenter - fullRegion.WorldSize * 0.5f;
			Vector2 cameraMax = fullRegion.WorldCenter + fullRegion.WorldSize * 0.5f;
			Vector2 boundsMin = new Vector2(center.x - radii.x, Mathf.Max(center.y - radii.y, cutY));
			Vector2 boundsMax = new Vector2(center.x + radii.x, center.y + radii.y);
			Vector2 cropMin = Vector2.Max(cameraMin, boundsMin);
			Vector2 cropMax = Vector2.Min(cameraMax, boundsMax);
			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				return new ParticleFluidRenderRegion2D(fullRegion.WorldCenter, Vector2.one * cameraWorldUnitsPerPixel, Vector4.zero, 1, 1, true);
			}

			float uvMinX = Mathf.Clamp01((cropMin.x - cameraMin.x) / fullRegion.WorldSize.x);
			float uvMinY = Mathf.Clamp01((cropMin.y - cameraMin.y) / fullRegion.WorldSize.y);
			float uvMaxX = Mathf.Clamp01((cropMax.x - cameraMin.x) / fullRegion.WorldSize.x);
			float uvMaxY = Mathf.Clamp01((cropMax.y - cameraMin.y) / fullRegion.WorldSize.y);
			int x = Mathf.Clamp(Mathf.FloorToInt(uvMinX * fullWidth), 0, Mathf.Max(fullWidth - 1, 0));
			int y = Mathf.Clamp(Mathf.FloorToInt(uvMinY * fullHeight), 0, Mathf.Max(fullHeight - 1, 0));
			int xMax = Mathf.Clamp(Mathf.CeilToInt(uvMaxX * fullWidth), x + 1, fullWidth);
			int yMax = Mathf.Clamp(Mathf.CeilToInt(uvMaxY * fullHeight), y + 1, fullHeight);
			int pixelWidth = Mathf.Max(1, xMax - x);
			int pixelHeight = Mathf.Max(1, yMax - y);
			if (pixelWidth >= fullWidth - 1 && pixelHeight >= fullHeight - 1)
			{
				return fullRegion;
			}

			Vector4 sourceUvRect = new Vector4(
				x / (float)fullWidth,
				y / (float)fullHeight,
				pixelWidth / (float)fullWidth,
				pixelHeight / (float)fullHeight
			);
			Vector2 worldMin = cameraMin + new Vector2(sourceUvRect.x * fullRegion.WorldSize.x, sourceUvRect.y * fullRegion.WorldSize.y);
			Vector2 worldSize = new Vector2(sourceUvRect.z * fullRegion.WorldSize.x, sourceUvRect.w * fullRegion.WorldSize.y);
			Vector2 worldCenter = worldMin + worldSize * 0.5f;
			return new ParticleFluidRenderRegion2D(worldCenter, worldSize, sourceUvRect, pixelWidth, pixelHeight, true);
		}

		static ParticleFluidRenderRegion2D GetScaledRenderRegion(ParticleFluidRenderRegion2D source, float scale)
		{
			float clampedScale = Mathf.Max(scale, 0.0001f);
			return new ParticleFluidRenderRegion2D(
				source.WorldCenter,
				source.WorldSize,
				source.SourceUvRect,
				Mathf.Max(1, Mathf.RoundToInt(source.PixelWidth * clampedScale)),
				Mathf.Max(1, Mathf.RoundToInt(source.PixelHeight * clampedScale)),
				source.IsCropped
			);
		}

		static float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings)
		{
			return Mathf.Max(surface.renderTextureScale * settings.textureScale, 0.0001f);
		}

		static Vector4 GetCausticMultiplier(Color colour, float intensity)
		{
			float clampedIntensity = Mathf.Max(intensity, 0f);
			return new Vector4(
				colour.r * clampedIntensity,
				colour.g * clampedIntensity,
				colour.b * clampedIntensity,
				0f
			);
		}

		static Vector4 GetPointLightVector(ParticleFluidLighting2D.DirectionalLightSettings light)
		{
			if (light == null)
			{
				return new Vector4(0f, 0f, 0f, 0.0001f);
			}

			return new Vector4(light.pointPosition.x, light.pointPosition.y, Mathf.Max(light.pointHeight, 0.0001f), Mathf.Max(light.pointRange, 0.0001f));
		}

		static int GetCausticPointRayCount(ParticleFluidLighting2D.DirectionalLightSettings light, Vector2 worldSize, int width, int height)
		{
			if (light == null)
			{
				return 0;
			}

			float pixelsPerWorldUnit = Mathf.Max(
				width / Mathf.Max(worldSize.x, 0.0001f),
				height / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(light.pointRange, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(TwoPi * radiusPixels));
		}

		void GetCausticPointRaySpan(ParticleDisplay2D display, ParticleFluidLighting2D.DirectionalLightSettings light, bool useAnalyticBoundary, out float angleStart, out float angleRange)
		{
			angleStart = 0f;
			angleRange = TwoPi;
			if (display == null
			    || light == null
			    || light.type != ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point
			    || !useAnalyticBoundary
			    || !display.sim.useEllipticalBounds)
			{
				return;
			}

			Vector2 point = light.pointPosition;
			float expansion = GetAnalyticBoundaryExpansion(display);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			float cutY = display.sim.obstacleY - expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f || IsInsideAnalyticBoundary(point, center, radii, cutY))
			{
				return;
			}

			int angleCount = 0;
			for (int i = 0; i < PointLightBoundaryEllipseSamples; i++)
			{
				float t = i / (float)PointLightBoundaryEllipseSamples * TwoPi;
				Vector2 boundaryPoint = center + new Vector2(Mathf.Cos(t) * radii.x, Mathf.Sin(t) * radii.y);
				if (boundaryPoint.y >= cutY)
				{
					AddPointLightBoundaryAngle(point, boundaryPoint, ref angleCount);
				}
			}

			float cutRelY = cutY - center.y;
			if (Mathf.Abs(cutRelY) <= radii.y)
			{
				float cutHalfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY / (radii.y * radii.y)));
				for (int i = 0; i < PointLightBoundaryCutSamples; i++)
				{
					float t = PointLightBoundaryCutSamples > 1 ? i / (float)(PointLightBoundaryCutSamples - 1) : 0.5f;
					AddPointLightBoundaryAngle(point, new Vector2(center.x + Mathf.Lerp(-cutHalfWidth, cutHalfWidth, t), cutY), ref angleCount);
				}
			}

			if (angleCount < 2)
			{
				return;
			}

			System.Array.Sort(pointLightBoundaryAngles, 0, angleCount);
			float largestGap = -1f;
			int largestGapIndex = 0;
			for (int i = 0; i < angleCount; i++)
			{
				float current = pointLightBoundaryAngles[i];
				float next = i == angleCount - 1 ? pointLightBoundaryAngles[0] + TwoPi : pointLightBoundaryAngles[i + 1];
				float gap = next - current;
				if (gap > largestGap)
				{
					largestGap = gap;
					largestGapIndex = i;
				}
			}

			float padding = 2f * Mathf.Deg2Rad;
			angleStart = Mathf.Repeat(pointLightBoundaryAngles[(largestGapIndex + 1) % angleCount] - padding, TwoPi);
			angleRange = Mathf.Clamp(TwoPi - largestGap + padding * 2f, 0.0001f, TwoPi);
		}

		void AddPointLightBoundaryAngle(Vector2 lightPoint, Vector2 boundaryPoint, ref int angleCount)
		{
			if (angleCount >= pointLightBoundaryAngles.Length)
			{
				return;
			}

			Vector2 delta = boundaryPoint - lightPoint;
			if (delta.sqrMagnitude <= 0.000001f)
			{
				return;
			}

			pointLightBoundaryAngles[angleCount++] = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x), TwoPi);
		}

		static bool IsInsideAnalyticBoundary(Vector2 point, Vector2 center, Vector2 radii, float cutY)
		{
			if (point.y < cutY)
			{
				return false;
			}

			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			return ellipseValue <= 1f;
		}

		static float GetCausticDebugExposure(ParticleFluidLighting2D settings)
		{
			if (settings == null)
			{
				return 0.0001f;
			}

			float exposure = SupportsCausticRaymarch(settings.PrimaryLight) ? Luminance(settings.EffectiveLightColor) * Mathf.Max(settings.PrimaryLight.intensity, 0f) : 0f;
			if (SupportsCausticRaymarch(settings.SecondaryLight))
			{
				exposure += Luminance(settings.EffectiveSecondaryLightColor) * Mathf.Max(settings.SecondaryLight.intensity, 0f);
			}
			if (SupportsCausticRaymarch(settings.TertiaryLight))
			{
				exposure += Luminance(settings.EffectiveTertiaryLightColor) * Mathf.Max(settings.TertiaryLight.intensity, 0f);
			}

			return Mathf.Max(exposure, 0.0001f);
		}

		static float Luminance(Color colour)
		{
			return colour.r * 0.2126f + colour.g * 0.7152f + colour.b * 0.0722f;
		}

		static float GetSaturationDispersionScale(Color colour)
		{
			float maxChannel = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
			float minChannel = Mathf.Min(colour.r, Mathf.Min(colour.g, colour.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}

		static void GetLightRayShares(float primaryWeight, float secondaryWeight, float tertiaryWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare)
		{
			float totalWeight = primaryWeight + secondaryWeight + tertiaryWeight;
			if (totalWeight > 0.0001f)
			{
				primaryShare = primaryWeight / totalWeight;
				secondaryShare = secondaryWeight / totalWeight;
				tertiaryShare = tertiaryWeight / totalWeight;
				return;
			}

			primaryShare = 0f;
			secondaryShare = 0f;
			tertiaryShare = 0f;
		}

		static float LightSampleWeight(ParticleFluidLighting2D.DirectionalLightSettings light, Color effectiveColour)
		{
			if (!SupportsCausticRaymarch(light))
			{
				return 0f;
			}

			return Mathf.Max(0f, Luminance(effectiveColour) * light.intensity * light.sampleBias);
		}

		static bool SupportsCausticRaymarch(ParticleFluidLighting2D.DirectionalLightSettings light)
		{
			return light != null
			       && light.enabled
			       && light.intensity > 0f;
		}

		static bool AnyEnabledCausticPointLight(ParticleFluidLighting2D settings)
		{
			return IsWeightedPointLight(settings.PrimaryLight, settings.EffectiveLightColor)
			       || IsWeightedPointLight(settings.SecondaryLight, settings.EffectiveSecondaryLightColor)
			       || IsWeightedPointLight(settings.TertiaryLight, settings.EffectiveTertiaryLightColor);
		}

		static bool IsWeightedPointLight(ParticleFluidLighting2D.DirectionalLightSettings light, Color effectiveColour)
		{
			return light != null
			       && light.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point
			       && LightSampleWeight(light, effectiveColour) > 0f;
		}

		Vector3 GetDirectLightingDirection(ParticleDisplay2D display, ParticleFluidLighting2D settings, Vector3 lightDirection)
		{
			if (settings.directionalLightingMode == ParticleFluidLighting2D.DirectionalLightingMode.Direct || !display.sim.useEllipticalBounds)
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
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(settings.phase1Material.indexOfRefraction, 1.0001f));
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

		void GetCausticRayRange(ParticleDisplay2D display, ParticleFluidLighting2D settings, int width, int height, Vector3 lightDirection, Vector2 worldCenter, Vector2 worldSize, float analyticBoundaryExpansion, out float startOffset, out int rayCount)
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

		bool ShouldRenderCaustics(ParticleFluidLighting2D settings)
		{
			return settings != null && settings.causticsEnabled && settings.computeShader != null;
		}

		ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			ParticleFluidLighting2D lighting = display != null ? display.GetComponent<ParticleFluidLighting2D>() : null;
			return lighting != null && lighting.isActiveAndEnabled ? lighting : null;
		}

		bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
		{
			return display.debugMode == ParticleDisplay2D.DebugVisualization.None
			       && LightingDebugMode(GetActiveLighting(display)) == ParticleFluidLighting2D.LightingDebugVisualization.None;
		}

		bool ShouldUseCroppedRenderRegion(ParticleDisplay2D display)
		{
			if (ShouldUseMaterialPipeline(display))
			{
				return true;
			}

			ParticleFluidLighting2D.LightingDebugVisualization lightingDebug = LightingDebugMode(GetActiveLighting(display));
			return lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.Caustics
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion;
		}

		bool ShouldRenderCausticDebug(ParticleFluidLighting2D settings)
		{
			return LightingDebugMode(settings) != ParticleFluidLighting2D.LightingDebugVisualization.None;
		}

		static ParticleFluidLighting2D.LightingDebugVisualization LightingDebugMode(ParticleFluidLighting2D settings)
		{
			return settings != null ? settings.debugMode : ParticleFluidLighting2D.LightingDebugVisualization.None;
		}

		static int GetDebugShaderMode(ParticleDisplay2D display, ParticleFluidLighting2D settings)
		{
			if (display != null && display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return (int)display.debugMode;
			}

			return LightingDebugMode(settings) switch
			{
				ParticleFluidLighting2D.LightingDebugVisualization.Caustics => 7,
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => 8,
				ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField => 9,
				ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion => 10,
				_ => 0,
			};
		}

		bool ShouldRenderPhaseDiffuseLight(ParticleFluidLighting2D settings)
		{
			return ShouldRenderCaustics(settings)
			       && settings.phaseDiffuseLightEnabled
			       && settings.phaseDiffuseLightCompute != null
			       && (
				       settings.phase0Material.diffuseScatterStrength > 0f
				       || settings.phase1Material.diffuseScatterStrength > 0f
			       );
		}

		bool ShouldRenderRadianceCascadeLight(ParticleFluidLighting2D settings)
		{
			return ShouldRenderCaustics(settings)
			       && settings.radianceCascadeEnabled
			       && settings.radianceCascadeShader != null
			       && settings.phase0Material.diffuseScatterStrength > 0f;
		}

		bool ShouldRenderDirectionalLightField(ParticleFluidLighting2D settings)
		{
			return ShouldRenderCaustics(settings) && settings.directionalLightingMode == ParticleFluidLighting2D.DirectionalLightingMode.DirectionalLightField;
		}

		bool ShouldRenderCausticMotion(ParticleFluidLighting2D settings)
		{
			return ShouldRenderCaustics(settings)
			       && (settings.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion
			           || LightingDebugMode(settings) == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
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
