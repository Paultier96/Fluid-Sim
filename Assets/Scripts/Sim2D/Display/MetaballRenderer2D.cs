using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		const string CommandBufferName = "Sim2D Metaball Render";
		Material metaballMaterial;
		Material debugMaterial;
		Material blurMaterial;
		Material velocityBlurMaterial;
		readonly MetaballMaterialRenderer2D materialRenderer = new ();
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		RenderTexture velocityPhase0AccumulationTexture;
		RenderTexture velocityPhase0BlurTexture;
		RenderTexture velocityPhase1AccumulationTexture;
		RenderTexture velocityPhase1BlurTexture;
		
		CommandBuffer commandBuffer;
		
		bool commandBufferAttached;
		
		ParticleFluidRenderRegion2D currentMaterialRenderRegion;
		public RenderTexture CombinedAccumulationTexture => combinedAccumulationTexture;
		public RenderTexture CombinedBlurTexture => combinedBlurTexture;
		public RenderTexture NormalAccumulationTexture => normalAccumulationTexture;
		public RenderTexture NormalBlurTexture => normalBlurTexture;
		public RenderTexture VelocityPhase0AccumulationTexture => velocityPhase0AccumulationTexture;
		public RenderTexture VelocityPhase0BlurTexture => velocityPhase0BlurTexture;
		public RenderTexture VelocityPhase1AccumulationTexture => velocityPhase1AccumulationTexture;
		public RenderTexture VelocityPhase1BlurTexture => velocityPhase1BlurTexture;
		public Material BlurMaterial => blurMaterial;
		public Material VelocityBlurMaterial => velocityBlurMaterial;
		public ParticleFluidMaterialMapSet MaterialMaps => materialRenderer.MaterialMaps;
		public ParticleFluidRenderRegion2D CurrentMaterialRenderRegion => currentMaterialRenderRegion;
		public bool IsMaterialPipelineReady => materialRenderer.IsReady;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			if (!PrepareForRender(display, cam))
			{
				RemoveCommandBuffer();
				return;
			}

			EnsureCommandBuffer(cam);
			commandBuffer.Clear();
			BuildCommandBuffer(display, cam, commandBuffer, BuiltinRenderTextureType.CameraTarget);
		}

		public void Record(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (!PrepareForRender(display, cam) || targetCommandBuffer == null)
			{
				return;
			}

			BuildCommandBuffer(display, cam, targetCommandBuffer, finalTarget);
		}

		public bool PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || blurMaterial == null || velocityBlurMaterial == null || cam == null || display == null || display.mesh == null || display.argsBuffer == null)
			{
				return false;
			}

			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyDebugSettings(display, cam);
			ApplyMaterialSettings(display, cam);
			GetActiveLighting(display).ApplyFrameSettings(display, cam, currentMaterialRenderRegion, combinedAccumulationTexture, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture);
			return true;
		}

		public void RecordAccumulationAndBlur(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			RecordAccumulation(display, targetCommandBuffer);
			RecordBlur(display, cam, targetCommandBuffer);
		}

		public void RecordAccumulationTarget(ParticleDisplay2D display, IRasterCommandBuffer targetCommandBuffer, int shaderPass)
		{
			if (targetCommandBuffer == null || display == null)
			{
				return;
			}

			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, shaderPass, display.argsBuffer);
		}

		public void RecordComposite(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			RecordMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			RecordVectorField(display, targetCommandBuffer);
		}

		public void RecordMaterialMaps(CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			materialRenderer.Render(targetCommandBuffer);
		}

		public void RecordCompositeWithPreparedMaterialMaps(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			RecordVectorField(display, targetCommandBuffer);
		}

		public bool UsesMaterialPipeline(ParticleDisplay2D display)
		{
			return ShouldUseMaterialPipeline(display);
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			return new ParticleFluidLighting2D.FrameContext(
				display,
				cam,
				currentMaterialRenderRegion,
				lighting != null ? lighting.currentCausticRenderRegion : currentMaterialRenderRegion,
				display.GetZoomScale(cam),
				GetAnalyticBoundaryExpansion(display)
			);
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
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref velocityBlurMaterial, display.metaballs.blurShader);
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
			lighting.EnsureLightingResources(currentMaterialRenderRegion);
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
			debugMaterial.SetTexture("CausticMotionTex", lighting.causticMotionTexture != null ? lighting.causticMotionTexture : Texture2D.blackTexture);
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
			debugMaterial.SetTexture("CausticTex", lighting != null && lighting.denoisingEnabled ? lighting.causticTemporalTexture : lighting.causticResolvedTexture);
			bool renderDirectionalLightField = lighting.ShouldRenderDirectionalLightField();
			debugMaterial.SetInt("metaballDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			Texture lightDirectionTex = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTex = lighting.denoisingEnabled ? lighting.lightDirectionTemporalTexture : lighting.lightDirectionTexture;
			}
			debugMaterial.SetTexture("LightDirectionTex", lightDirectionTex);
			bool renderSoftLight = lighting.ShouldRenderPhaseDiffuseLight() || lighting.ShouldRenderRadianceCascadeLight();
			debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			debugMaterial.SetInt("metaballSoftLightPhase0Only", lighting.ShouldRenderRadianceCascadeLight() ? 1 : 0);
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
			debugMaterial.SetFloat("particleCausticDebugExposure", lighting.GetCausticDebugExposure());
			debugMaterial.SetInt("particleCausticTemporalDebugEnabled", lighting != null && lighting.denoisingEnabled ? 1 : 0);
			debugMaterial.SetInt("particleCausticRegionEnabled", lighting.currentCausticRenderRegion.IsCropped ? 1 : 0);
			debugMaterial.SetVector("particleCausticUvRect", lighting.currentCausticRenderRegion.SourceUvRect);
			debugMaterial.SetVector("causticCurrentWorldCenter", new Vector4(lighting.currentCausticRenderRegion.WorldCenter.x, lighting.currentCausticRenderRegion.WorldCenter.y, 0f, 0f));
			debugMaterial.SetVector("causticCurrentWorldSize", new Vector4(lighting.currentCausticRenderRegion.WorldSize.x, lighting.currentCausticRenderRegion.WorldSize.y, 0f, 0f));
			debugMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void ApplyMaterialSettings(ParticleDisplay2D display, Camera cam)
		{
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
			materialRenderer.ApplySettings(display, cam, combinedAccumulationTexture, normalAccumulationTexture, currentMaterialRenderRegion, GetAnalyticBoundaryExpansion(display), effectiveNormalStrength, GetActiveLighting(display));
		}

		void BuildCommandBuffer(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			RecordAccumulation(display, targetCommandBuffer);
			RecordBlur(display, cam, targetCommandBuffer);
			RecordCaustics(display, cam, targetCommandBuffer);
			RecordMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			RecordVectorField(display, targetCommandBuffer);
		}

		void RecordAccumulation(ParticleDisplay2D display, CommandBuffer targetCommandBuffer)
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
		}

		public void RecordBlur(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
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
		}

		public float GetEffectiveMotionBlurRadius(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			return display.GetEffectiveMotionBlurRadius(cam, lighting != null ? lighting.motionBlurRadius : 0f);
		}

		public void RecordCaustics(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.IsReady;
			if ((useLighting || lighting.ShouldRenderCausticDebug()) && lighting.ShouldRenderCaustics())
			{
				ParticleFluidLighting2D.FrameContext lightingContext = CreateLightingContext(display, cam);
				targetCommandBuffer.BeginSample("Metaballs/Caustics");
				int frameIndex = lighting.ReserveCausticsFrameIndex();
				RenderTexture lightDirectionTarget = lighting.GetCausticsLightDirectionTarget();
				lighting.TraceCaustics.RecordComputeClear(lightingContext, targetCommandBuffer, combinedAccumulationTexture, frameIndex, lighting.causticResolvedTexture, lighting.causticMotionTexture, lightDirectionTarget);
				lighting.TraceCaustics.RecordComputeTrace(lightingContext, targetCommandBuffer, combinedAccumulationTexture, frameIndex, combinedAccumulationTexture, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture, display.gradientTexture, display.gradientTexture2);
				lighting.TraceCaustics.RecordComputeResolve(lightingContext, targetCommandBuffer, combinedAccumulationTexture, frameIndex, lighting.causticResolvedTexture, lighting.causticMotionTexture, lightDirectionTarget);
				RenderTexture temporalMotionTexture = lighting.TemporalCaustics.RecordBlurAndMotion(lightingContext, targetCommandBuffer);
				lighting.TemporalCaustics.RecordTemporal(lightingContext, targetCommandBuffer, temporalMotionTexture);
				lighting.SoftLightCaustics.RecordSoftLight(lightingContext, targetCommandBuffer, lighting.GetSharpCausticsTexture(), combinedAccumulationTexture);
				targetCommandBuffer.EndSample("Metaballs/Caustics");
			}
		}

		void RecordMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.IsReady;
			if (useMaterialPipeline && materialRenderer.IsReady)
			{
				materialRenderer.Render(targetCommandBuffer);
				RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			}
			else if (debugMaterial != null)
			{
				targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
				targetCommandBuffer.Blit(null, finalTarget, debugMaterial, 0);
				targetCommandBuffer.EndSample("Metaballs/Debug Composite");
			}
		}

		void RecordPreparedMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.IsReady;
			if (!useMaterialPipeline || !materialRenderer.IsReady)
			{
				return;
			}

			targetCommandBuffer.BeginSample("Metaballs/Material Pipeline");
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

		void RecordVectorField(ParticleDisplay2D display, CommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.AppendVectorFieldDraw(targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}
		public static float GetAnalyticBoundaryExpansion(ParticleDisplay2D display)
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

		ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			ParticleFluidLighting2D lighting = display != null ? display.GetComponent<ParticleFluidLighting2D>() : null;
			return lighting != null && lighting.isActiveAndEnabled ? lighting : null;
		}

		bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
		{
			return display.debugMode == ParticleDisplay2D.DebugVisualization.None
			       && GetActiveLighting(display).debugMode== ParticleFluidLighting2D.LightingDebugVisualization.None;
		}

		bool ShouldUseCroppedRenderRegion(ParticleDisplay2D display)
		{
			if (ShouldUseMaterialPipeline(display))
			{
				return true;
			}

			ParticleFluidLighting2D.LightingDebugVisualization lightingDebug = GetActiveLighting(display).debugMode;
			return lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.Caustics
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.SoftLight
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp;
		}



		public static int GetDebugShaderMode(ParticleDisplay2D display, ParticleFluidLighting2D settings)
		{
			if (display != null && display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return (int)display.debugMode;
			}

			return settings.debugMode switch
			{
				ParticleFluidLighting2D.LightingDebugVisualization.Caustics => 7,
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => 8,
				ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField => 9,
				ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion => 10,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection => 12,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp => 13,
				_ => 0,
			};
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
