using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		const int MaxMotionPyramidLevels = 6;
		static readonly int MotionPyramidBlendStrengthId = Shader.PropertyToID("pyramidBlendStrength");
		static readonly int MotionPyramidLowMipTexId = Shader.PropertyToID("_LowMipTex");
		static readonly int MotionPhase0Mip0Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip0");
		static readonly int MotionPhase0Mip1Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip1");
		static readonly int MotionPhase0Mip2Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip2");
		static readonly int MotionPhase0Mip3Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip3");
		static readonly int MotionPhase0Mip4Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip4");
		static readonly int MotionPhase0Mip5Id = Shader.PropertyToID("_ParticleFluidMotionPhase0Mip5");
		static readonly int MotionPhase1Mip0Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip0");
		static readonly int MotionPhase1Mip1Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip1");
		static readonly int MotionPhase1Mip2Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip2");
		static readonly int MotionPhase1Mip3Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip3");
		static readonly int MotionPhase1Mip4Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip4");
		static readonly int MotionPhase1Mip5Id = Shader.PropertyToID("_ParticleFluidMotionPhase1Mip5");
		static readonly int[] MotionPhase0MipIds = { MotionPhase0Mip0Id, MotionPhase0Mip1Id, MotionPhase0Mip2Id, MotionPhase0Mip3Id, MotionPhase0Mip4Id, MotionPhase0Mip5Id };
		static readonly int[] MotionPhase1MipIds = { MotionPhase1Mip0Id, MotionPhase1Mip1Id, MotionPhase1Mip2Id, MotionPhase1Mip3Id, MotionPhase1Mip4Id, MotionPhase1Mip5Id };
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
		
		ParticleFluidRenderLayout2D currentRenderLayout;
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
		public ParticleFluidRenderRegion2D CurrentMaterialRenderRegion => currentRenderLayout.Source;
		public bool IsMaterialPipelineReady => materialRenderer.IsReady;

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
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				lighting.ApplyFrameSettings(
					display,
					cam,
					currentRenderLayout,
					combinedAccumulationTexture,
					velocityPhase0AccumulationTexture,
					velocityPhase1AccumulationTexture);
			}
			return true;
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
				currentRenderLayout,
				display.GetZoomScale(cam),
				GetAnalyticBoundaryExpansion(display)
			);
		}

		public void Release()
		{
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture, velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityPhase1AccumulationTexture, velocityPhase1BlurTexture);
			materialRenderer.Release();

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
			Shader motionPyramidShader = Shader.Find("Hidden/Particle2DPhaseMotionPyramid");
			EnsureMaterial(ref velocityBlurMaterial, motionPyramidShader != null ? motionPyramidShader : display.metaballs.blurShader);
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
			metaballMaterial.SetVector("metaballRenderWorldCenter", new Vector4(currentRenderLayout.Source.WorldCenter.x, currentRenderLayout.Source.WorldCenter.y, 0f, 0f));
			metaballMaterial.SetVector("metaballRenderWorldSize", new Vector4(currentRenderLayout.Source.WorldSize.x, currentRenderLayout.Source.WorldSize.y, 0f, 0f));
			metaballMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			metaballMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			metaballMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			metaballMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
		}

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			currentRenderLayout = GetRenderLayout(display, cam);
			int width = currentRenderLayout.Source.PixelWidth;
			int height = currentRenderLayout.Source.PixelHeight;

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
				materialRenderer.EnsureRenderTextures(currentRenderLayout.Material);
			}
			else
			{
				materialRenderer.ReleaseMaterialTextures();
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				lighting.EnsureLightingResources(currentRenderLayout);
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
			Texture causticMotionDebugTexture = Texture2D.blackTexture;
			if (lighting != null)
			{
				Texture processedMotionTexture = lighting.TemporalCaustics.GetTemporalMotionTextureAfterBlur();
				Texture rawMotionTexture = lighting.causticMotionTexture;
				causticMotionDebugTexture = processedMotionTexture ?? rawMotionTexture ?? Texture2D.blackTexture;
			}
			debugMaterial.SetTexture("CausticMotionTex", causticMotionDebugTexture);
			debugMaterial.SetTexture("CausticReactiveShadowMapTex", lighting != null && lighting.reactiveShadowMapTexture != null ? lighting.reactiveShadowMapTexture : Texture2D.blackTexture);
			debugMaterial.SetTexture("CausticReactiveShadowHistoryTex", lighting != null && lighting.reactiveShadowMapHistoryTexture != null ? lighting.reactiveShadowMapHistoryTexture : Texture2D.blackTexture);
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
			debugMaterial.SetTexture("CausticTex",
				lighting == null ? Texture2D.blackTexture :
				lighting.denoisingEnabled ? lighting.causticTemporalTexture : lighting.causticResolvedTexture);
			bool renderDirectionalLightField = lighting != null && lighting.ShouldRenderDirectionalLightField();
			debugMaterial.SetInt("metaballDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			Texture lightDirectionTex = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTex = lighting.denoisingEnabled ? lighting.lightDirectionTemporalTexture : lighting.lightDirectionTexture;
			}
			debugMaterial.SetTexture("LightDirectionTex", lightDirectionTex);
			bool renderSoftLight = lighting != null && (lighting.ShouldRenderPhaseDiffuseLight() || lighting.ShouldRenderRadianceCascadeLight());
			debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			debugMaterial.SetTexture("SoftLightTex",
				lighting != null && lighting.currentSoftLightPhase0Texture != null ? lighting.currentSoftLightPhase0Texture :
				Texture2D.blackTexture);
			debugMaterial.SetTexture("SoftLightTexPhase1",
				lighting != null && lighting.currentSoftLightPhase1Texture != null ? lighting.currentSoftLightPhase1Texture :
				Texture2D.blackTexture);
			debugMaterial.SetColor("metaballPhase0DiffuseLightTint", lighting != null ? lighting.phase0Material.diffuseLightTint : Color.white);
			debugMaterial.SetColor("metaballPhase1DiffuseLightTint", lighting != null ? lighting.phase1Material.diffuseLightTint : Color.white);
			debugMaterial.SetFloat("metaballPhase0DiffuseAlbedoTintBlend", lighting != null ? lighting.phase0Material.diffuseAlbedoTintBlend : 0f);
			debugMaterial.SetFloat("metaballPhase1DiffuseAlbedoTintBlend", lighting != null ? lighting.phase1Material.diffuseAlbedoTintBlend : 0f);
			debugMaterial.SetFloat("metaballRadianceCascadePhase0Visibility", lighting != null ? lighting.radianceCascadePhase0Visibility : 0f);
			debugMaterial.SetFloat("metaballRadianceCascadePhase1Visibility", lighting != null ? lighting.radianceCascadePhase1Visibility : 1f);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			debugMaterial.SetInt("debugMode", GetDebugShaderMode(display, lighting));
			debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			debugMaterial.SetFloat("motionVelocityThreshold", lighting != null ? lighting.motionVelocityThreshold : 0f);
			display.ApplyDebugClipSettings(debugMaterial);
			debugMaterial.SetFloat("particleCausticDebugExposure", lighting != null ? lighting.GetCausticDebugExposure() : 1f);
			debugMaterial.SetInt("particleCausticTemporalDebugEnabled", lighting != null && lighting.denoisingEnabled ? 1 : 0);
			ParticleFluidRenderRegion2D causticRegion = lighting != null ? lighting.currentCausticRenderRegion : currentRenderLayout.Source;
			debugMaterial.SetInt("particleCausticRegionEnabled", causticRegion.IsCropped ? 1 : 0);
			debugMaterial.SetVector("particleCausticUvRect", causticRegion.SourceUvRect);
			debugMaterial.SetVector("causticCurrentWorldCenter", new Vector4(causticRegion.WorldCenter.x, causticRegion.WorldCenter.y, 0f, 0f));
			debugMaterial.SetVector("causticCurrentWorldSize", new Vector4(causticRegion.WorldSize.x, causticRegion.WorldSize.y, 0f, 0f));
			debugMaterial.SetVector("causticHistoryWorldCenter", lighting != null ? new Vector4(lighting.previousCausticWorldCenter.x, lighting.previousCausticWorldCenter.y, 0f, 0f) : new Vector4(causticRegion.WorldCenter.x, causticRegion.WorldCenter.y, 0f, 0f));
			debugMaterial.SetVector("causticHistoryWorldSize", lighting != null ? new Vector4(lighting.previousCausticWorldSize.x, lighting.previousCausticWorldSize.y, 0f, 0f) : new Vector4(causticRegion.WorldSize.x, causticRegion.WorldSize.y, 0f, 0f));
			Vector3 effectiveProjectedShadowDirection = lighting != null ? lighting.GetDirectLightingDirection(display, lighting.primaryLight.Direction) : Vector3.zero;
			debugMaterial.SetVector("causticReactiveShadowDirection", new Vector4(effectiveProjectedShadowDirection.x, effectiveProjectedShadowDirection.y, 0f, 0f));
			debugMaterial.SetVector("causticReactiveShadowHistoryDirection", lighting != null ? new Vector4(lighting.previousReactiveShadowDirection.x, lighting.previousReactiveShadowDirection.y, 0f, 0f) : Vector4.zero);
			debugMaterial.SetFloat("causticReactiveShadowOffset", lighting != null ? lighting.projectedShadowOffset : 0f);
			debugMaterial.SetFloat("causticReactiveShadowExpansion", lighting != null ? lighting.projectedShadowExpansion : 0f);
			debugMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void ApplyMaterialSettings(ParticleDisplay2D display, Camera cam)
		{
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
			materialRenderer.ApplySettings(display, cam, combinedAccumulationTexture, normalAccumulationTexture, currentRenderLayout.Material, GetAnalyticBoundaryExpansion(display), effectiveNormalStrength, GetActiveLighting(display));
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
				RecordMotionPyramid(targetCommandBuffer, effectiveMotionBlurRadius);
			}
		}

		public void RecordMotionPyramid(CommandBuffer targetCommandBuffer, float effectiveMotionBlurRadius)
		{
			if (velocityBlurMaterial == null)
			{
				return;
			}

			GetMotionPyramidParams(effectiveMotionBlurRadius, out int levels, out float blendStrength);
			targetCommandBuffer.BeginSample("Metaballs/Pyramid Particle Motion");
			velocityBlurMaterial.SetFloat(MotionPyramidBlendStrengthId, blendStrength);
			RecordSinglePhaseMotionPyramid(targetCommandBuffer, velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, MotionPhase0MipIds, levels);
			RecordSinglePhaseMotionPyramid(targetCommandBuffer, velocityPhase1AccumulationTexture, velocityPhase1BlurTexture, MotionPhase1MipIds, levels);
			targetCommandBuffer.EndSample("Metaballs/Pyramid Particle Motion");
		}

		void RecordSinglePhaseMotionPyramid(CommandBuffer targetCommandBuffer, RenderTexture sourceTexture, RenderTexture scratchTexture, int[] tempMipIds, int levels)
		{
			int mipCount = Mathf.Clamp(levels - 1, 0, MaxMotionPyramidLevels);
			RenderTargetIdentifier previousSource = sourceTexture;
			int previousWidth = sourceTexture.width;
			int previousHeight = sourceTexture.height;
			for (int i = 0; i < mipCount; i++)
			{
				int width = Mathf.Max(1, previousWidth / 2);
				int height = Mathf.Max(1, previousHeight / 2);
				targetCommandBuffer.GetTemporaryRT(tempMipIds[i], width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
				targetCommandBuffer.Blit(previousSource, tempMipIds[i], velocityBlurMaterial, 0);
				previousSource = tempMipIds[i];
				previousWidth = width;
				previousHeight = height;
			}

			RenderTargetIdentifier currentLow = previousSource;
			for (int i = mipCount - 1; i >= 0; i--)
			{
				RenderTargetIdentifier currentHigh = i == 0 ? sourceTexture : tempMipIds[i - 1];
				targetCommandBuffer.SetGlobalTexture(MotionPyramidLowMipTexId, currentLow);
				targetCommandBuffer.Blit(currentHigh, scratchTexture, velocityBlurMaterial, 1);
				targetCommandBuffer.Blit(scratchTexture, currentHigh);
				currentLow = currentHigh;
			}

			for (int i = 0; i < mipCount; i++)
			{
				targetCommandBuffer.ReleaseTemporaryRT(tempMipIds[i]);
			}
		}

		internal static void GetMotionPyramidParams(float effectiveMotionBlurRadius, out int levels, out float blendStrength)
		{
			float clampedRadius = Mathf.Max(effectiveMotionBlurRadius, 0f);
			levels = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Max(clampedRadius, 1f), 2f)) + 1, 1, MaxMotionPyramidLevels + 1);
			blendStrength = Mathf.Clamp01(0.2f + clampedRadius / 48f);
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
			if (lighting == null)
			{
				return;
			}

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
				ParticleFluidLighting2D.FrameContext lightingContext = CreateLightingContext(display, cam);
				Vector2 projectedShadowDirection = Vector2.zero;
				bool useProjectedShadow = lighting.ShouldRenderProjectedShadows()
				                          && lighting.ProjectedShadow.RecordCurrentShadowMap(lightingContext, targetCommandBuffer, out projectedShadowDirection);
				materialRenderer.MaterialMaps.BindTo(lighting, currentRenderLayout.Material);
				lighting.ApplyProjectedShadowSettings(useProjectedShadow, projectedShadowDirection);
				lighting.Render(targetCommandBuffer, finalTarget, cam);
			}
			else
			{
				materialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, currentRenderLayout.Material);
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

		ParticleFluidRenderLayout2D GetRenderLayout(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidRenderRegion2D cropRegion = GetMaterialRenderRegion(display, cam, Mathf.Max(cam.pixelWidth, 1), Mathf.Max(cam.pixelHeight, 1));
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			float causticScale = lighting != null ? lighting.textureScale : sourceScale;
			ParticleFluidRenderRegion2D sourceRegion = ParticleFluidLighting2D.GetCameraScaledRenderRegion(cam, cropRegion, sourceScale);
			ParticleFluidRenderRegion2D materialRegion = ParticleFluidLighting2D.GetCameraScaledRenderRegion(cam, cropRegion, materialScale);
			ParticleFluidRenderRegion2D causticRegion = ParticleFluidLighting2D.GetCameraScaledRenderRegion(cam, cropRegion, causticScale);
			return new ParticleFluidRenderLayout2D(cropRegion, sourceRegion, materialRegion, causticRegion);
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
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			return display != null
			       && display.debugMode == ParticleDisplay2D.DebugVisualization.None
			       && lighting != null
			       && lighting.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.None;
		}

		bool ShouldUseCroppedRenderRegion(ParticleDisplay2D display)
		{
			if (ShouldUseMaterialPipeline(display))
			{
				return true;
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting == null)
			{
				return false;
			}

			ParticleFluidLighting2D.LightingDebugVisualization lightingDebug = lighting.debugMode;
			return lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.Caustics
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.SoftLight
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascade
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadow
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadowMotion;
		}



		public static int GetDebugShaderMode(ParticleDisplay2D display, ParticleFluidLighting2D settings)
		{
			if (display != null && display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return (int)display.debugMode;
			}

			if (settings == null)
			{
				return 0;
			}

			return settings.debugMode switch
			{
				ParticleFluidLighting2D.LightingDebugVisualization.Caustics => 7,
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => 8,
				ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascade => 16,
				ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField => 9,
				ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion => 10,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection => 12,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp => 13,
				ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadow => 14,
				ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadowMotion => 15,
				_ => 0,
			};
		}
	}
}
