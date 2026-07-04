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
		internal Material blurMaterial;
		Material velocityBlurMaterial;
		internal readonly MetaballMaterialRenderer2D materialRenderer = new ();
		internal RenderTexture combinedAccumulationTexture;
		internal RenderTexture combinedBlurTexture;
		internal RenderTexture normalAccumulationTexture;
		internal RenderTexture normalBlurTexture;
		internal RenderTexture velocityPhase0AccumulationTexture;
		internal RenderTexture velocityPhase0BlurTexture;
		internal RenderTexture velocityPhase1AccumulationTexture;
		internal RenderTexture velocityPhase1BlurTexture;
		
		ParticleFluidRenderLayout2D currentRenderLayout;
		ParticleDisplay2D currentDisplay;
		Camera currentCamera;

		public bool PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			currentDisplay = display;
			currentCamera = cam;
			EnsureMaterials(display);
			if (metaballMaterial == null || blurMaterial == null || cam == null || display == null || display.mesh == null || display.argsBuffer == null)
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
					ShouldRenderVelocityTextures(display) ? velocityPhase0AccumulationTexture : null,
					ShouldRenderVelocityTextures(display) ? velocityPhase1AccumulationTexture : null);
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
			if (targetCommandBuffer == null || currentDisplay == null)
			{
				return;
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(currentDisplay);
			float effectiveNormalStrength = currentDisplay.GetEffectiveNormalStrength(currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, currentDisplay.sim != null ? currentDisplay.sim.analyticBoundary : null);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, currentDisplay.metaballs);
			ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, currentRenderLayout.DomainRegion.WorldCenter, currentRenderLayout.DomainRegion.WorldSize);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, currentDisplay);
			ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, currentDisplay, currentCamera, lighting, effectiveNormalStrength);
			materialRenderer.RenderTransportMap(targetCommandBuffer, currentRenderLayout.SourceRegion, currentCamera);
			if (lighting != null)
			{
				materialRenderer.MaterialMaps.BindTo(lighting);
			}
		}

		public void RecordSurfaceMaterialMaps(CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null || currentDisplay == null)
			{
				return;
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(currentDisplay);
			float effectiveNormalStrength = currentDisplay.GetEffectiveNormalStrength(currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, currentDisplay.sim != null ? currentDisplay.sim.analyticBoundary : null);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, currentDisplay.metaballs);
			ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, currentRenderLayout.DomainRegion.WorldCenter, currentRenderLayout.DomainRegion.WorldSize);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, currentDisplay);
			ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, currentDisplay, currentCamera, lighting, effectiveNormalStrength);
			materialRenderer.RenderSurfaceMaps(targetCommandBuffer, currentRenderLayout.MaterialRegion, currentCamera);
			if (lighting != null)
			{
				materialRenderer.MaterialMaps.BindTo(lighting);
			}
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

		public bool ShouldRenderVelocityTextures(ParticleDisplay2D display)
		{
			if (display == null)
			{
				return false;
			}

			if (display.debugMode == ParticleDisplay2D.DebugVisualization.ParticleMotion)
			{
				return true;
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting == null)
			{
				return false;
			}

			if (lighting.directLight.denoisingEnabled && lighting.directLight.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.ParticleMotion)
			{
				return true;
			}

			return lighting.directLight.lightingMode == ParticleFluidLighting2D.LightingMode.Caustics
			       && (lighting.directLight.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion
			           || lighting.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
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
			ParticleFluidRenderUtils.EnsureMaterial(ref metaballMaterial, display.metaballShader);
			Shader debugShader = display.metaballs.debugShader != null ? display.metaballs.debugShader : Shader.Find("Hidden/Particle2DMetaballDebug");
			ParticleFluidRenderUtils.EnsureMaterial(ref debugMaterial, debugShader);
			Shader materialShader = display.metaballs.materialShader != null ? display.metaballs.materialShader : Shader.Find("Hidden/Particle2DMetaballMaterial");
			materialRenderer.EnsureMaterial(materialShader);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				Shader lightingShader = lighting.lightingShader != null ? lighting.lightingShader : Shader.Find("Hidden/Particle2DParticleFluidLighting");
				lighting.EnsureMaterials(lightingShader);
			}
			ParticleFluidRenderUtils.EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			Shader motionPyramidShader = Shader.Find("Hidden/Particle2DPhaseMotionPyramid");
			ParticleFluidRenderUtils.EnsureMaterial(ref velocityBlurMaterial, motionPyramidShader != null ? motionPyramidShader : display.metaballs.blurShader);
		}



		void ApplyMetaballMaterialSettings(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			display.BindSimulationBuffers(metaballMaterial);
			display.ApplyCommonParticleSettings(metaballMaterial);
			metaballMaterial.SetFloat("metaballSharpness", settings.sharpness);
			metaballMaterial.SetFloat("metaballIntensity", settings.intensity);
			metaballMaterial.SetVector("metaballRenderWorldCenter", currentRenderLayout.DomainRegion.WorldCenter);
			metaballMaterial.SetVector("metaballRenderWorldSize", currentRenderLayout.DomainRegion.WorldSize);
			metaballMaterial.SetInt("useEllipticalBounds", display.sim.analyticBoundary.useEllipticalBounds ? 1 : 0);
			metaballMaterial.SetVector("ellipseBoundsCenter", display.sim.analyticBoundary.ellipseBoundsCenter);
			metaballMaterial.SetVector("ellipseBoundsSize", display.sim.analyticBoundary.ellipseBoundsSize);
			metaballMaterial.SetFloat("obstacleY", display.sim.analyticBoundary.obstacleY);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
		}

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			currentRenderLayout = GetRenderLayout(display, cam);
			int width = currentRenderLayout.SourceSize.x;
			int height = currentRenderLayout.SourceSize.y;

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Accumulation");
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Blur");
			if (ShouldRenderVelocityTextures(display))
			{
				ComputeHelper.CreateRenderTexture(ref velocityPhase0AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Accumulation");
				ComputeHelper.CreateRenderTexture(ref velocityPhase0BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 0 Velocity Blur");
				ComputeHelper.CreateRenderTexture(ref velocityPhase1AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Accumulation");
				ComputeHelper.CreateRenderTexture(ref velocityPhase1BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase 1 Velocity Blur");
			}
			else
			{
				ComputeHelper.Release(velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityPhase1AccumulationTexture, velocityPhase1BlurTexture);
			}
			if (GetActiveLighting(display) != null || ShouldUseMaterialPipeline(display))
			{
				materialRenderer.EnsureRenderTextures(currentRenderLayout.MaterialSize, currentRenderLayout.SourceSize);
			}
			else
			{
				materialRenderer.MaterialMaps.Release();
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

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			debugMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			debugMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			debugMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture != null ? velocityPhase0AccumulationTexture : Texture2D.blackTexture);
			debugMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture != null ? velocityPhase1AccumulationTexture : Texture2D.blackTexture);
			int debugShaderMode = GetDebugShaderMode(display, lighting);
			Texture causticMotionDebugTexture = Texture2D.blackTexture;
			if (lighting != null)
			{
				Texture processedMotionTexture = lighting.directLight.traceCaustics.temporalCaustics.GetTemporalMotionTextureAfterBlur();
				Texture rawMotionTexture = lighting.directLight.traceCaustics.causticMotionTexture;
				causticMotionDebugTexture = processedMotionTexture ?? rawMotionTexture ?? Texture2D.blackTexture;
			}
			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			debugMaterial.SetVector("metaballWorldCenter", new Vector4(currentRenderLayout.DomainRegion.WorldCenter.x, currentRenderLayout.DomainRegion.WorldCenter.y, 0f, 0f));
			debugMaterial.SetVector("metaballWorldSize", new Vector4(currentRenderLayout.DomainRegion.WorldSize.x, currentRenderLayout.DomainRegion.WorldSize.y, 0f, 0f));
			Texture causticDebugTexture =
				lighting == null ? Texture2D.blackTexture :
				lighting.directLight.denoisingEnabled ? lighting.directLight.traceCaustics.temporalCaustics.causticTemporalTexture : lighting.directLight.traceCaustics.causticResolvedTexture;
			bool renderSoftLight = lighting != null && (lighting.gaussianSss.ShouldRender() || lighting.radianceCascadeGi.radianceCascadeEnabled);
			debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			Texture softLightPhase0Tex = lighting != null && lighting.CurrentSoftLightPhase0Texture != null
				? lighting.CurrentSoftLightPhase0Texture
				: Texture2D.blackTexture;
			Texture softLightPhase1Tex = lighting != null && lighting.CurrentSoftLightPhase1Texture != null
				? lighting.CurrentSoftLightPhase1Texture
				: Texture2D.blackTexture;
			Texture debugTex0 = Texture2D.blackTexture;
			Texture debugTex1 = Texture2D.blackTexture;
			switch (debugShaderMode)
			{
				case 7:
				case 12:
				case 13:
				case 14:
					debugTex0 = causticDebugTexture != null ? causticDebugTexture : Texture2D.blackTexture;
					break;
				case 8:
					debugTex0 = lighting != null && lighting.CurrentGaussianBlurTexture != null
						? lighting.CurrentGaussianBlurTexture
						: Texture2D.blackTexture;
					debugTex1 = softLightPhase1Tex;
					break;
				case 18:
					debugTex0 = lighting != null && lighting.CurrentGaussianInitTexture != null
						? lighting.CurrentGaussianInitTexture
						: Texture2D.blackTexture;
					break;
				case 17:
					debugTex0 = softLightPhase1Tex;
					break;
				case 10:
					debugTex0 = causticMotionDebugTexture;
					break;
			}
			debugMaterial.SetTexture("DebugTex0", debugTex0);
			debugMaterial.SetTexture("DebugTex1", debugTex1);
			debugMaterial.SetColor("metaballPhase0DiffuseLightTint", lighting != null ? lighting.phase0Material.diffuseLightTint : Color.white);
			debugMaterial.SetColor("metaballPhase1DiffuseLightTint", lighting != null ? lighting.phase1Material.diffuseLightTint : Color.white);
			debugMaterial.SetFloat("metaballPhase0DiffuseAdditiveBlend", lighting != null ? lighting.phase0Material.diffuseAdditiveBlend : 0f);
			debugMaterial.SetFloat("metaballPhase1DiffuseAdditiveBlend", lighting != null ? lighting.phase1Material.diffuseAdditiveBlend : 0f);
			debugMaterial.SetInt("debugMode", debugShaderMode);
			debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(debugMaterial);
			debugMaterial.SetFloat("particleCausticDebugExposure", lighting != null ? lighting.lightManager.GetCausticDebugExposure() : 1f);
			debugMaterial.SetInt("particleCausticTemporalDebugEnabled", lighting != null && lighting.directLight.denoisingEnabled ? 1 : 0);
			Vector2 causticWorldCenter = lighting != null ? lighting.currentCausticWorldCenter : currentRenderLayout.DomainRegion.WorldCenter;
			Vector2 causticWorldSize = lighting != null ? lighting.currentCausticWorldSize : currentRenderLayout.DomainRegion.WorldSize;
			debugMaterial.SetVector("causticCurrentWorldCenter", new Vector4(causticWorldCenter.x, causticWorldCenter.y, 0f, 0f));
			debugMaterial.SetVector("causticCurrentWorldSize", new Vector4(causticWorldSize.x, causticWorldSize.y, 0f, 0f));
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void ApplyMaterialSettings(ParticleDisplay2D display, Camera cam)
		{
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
			materialRenderer.ApplySharedSettings(display, cam, GetAnalyticBoundaryExpansion(display), effectiveNormalStrength, GetActiveLighting(display));
		}

		public void RecordMotionPyramid(CommandBuffer targetCommandBuffer, float effectiveMotionBlurRadius)
		{
			if (velocityBlurMaterial == null || velocityPhase0AccumulationTexture == null || velocityPhase0BlurTexture == null || velocityPhase1AccumulationTexture == null || velocityPhase1BlurTexture == null)
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
			return display.GetEffectiveMotionBlurRadius(cam, lighting != null ? lighting.directLight.motionBlurRadius : 0f);
		}

		void RecordMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.lightingMaterial != null;
			if (useMaterialPipeline && materialRenderer.IsReady)
			{
				float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
				ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
				ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
				ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, currentRenderLayout.DomainRegion.WorldCenter, currentRenderLayout.DomainRegion.WorldSize);
				ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, display);
				ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, display, cam, lighting, effectiveNormalStrength);
				materialRenderer.RenderSurfaceMaps(targetCommandBuffer, currentRenderLayout.MaterialRegion, cam);
				RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			}
			else if (debugMaterial != null)
			{
				targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
				float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
				ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
				ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
				ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, display);
				ParticleFluidRasterTextureBindings.ApplyDebugGradientGlobals(targetCommandBuffer, display);
				ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, display, cam, lighting, effectiveNormalStrength);
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), ParticleFluidRenderUtils.CreateRegionMatrix(currentRenderLayout.DomainRegion), debugMaterial, 0, 0);
				targetCommandBuffer.EndSample("Metaballs/Debug Composite");
			}
		}

		void RecordPreparedMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			bool useLighting = useMaterialPipeline && lighting != null && lighting.lightingMaterial != null;
			if (!useMaterialPipeline || !materialRenderer.IsReady)
			{
				return;
			}

			targetCommandBuffer.BeginSample("Metaballs/Material Pipeline");
			ParticleFluidAnalyticBoundaryBindings.ApplyGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
			if (useLighting)
			{
				ParticleFluidLighting2D.FrameContext lightingContext = CreateLightingContext(display, cam);
				Vector2 projectedShadowDirection = Vector2.zero;
				ParticleFluidProjectedShadow.RecordParams projectedShadowParams = default;
				if (lighting.directLight.projectedShadow.ShouldRender(lighting.directLight)
				    && lighting.lightManager.GetMainDirectionalLight() is ParticleFluidDirectionalLight2D directionalLight)
				{
					Vector3 effectiveLightDirection = directionalLight.GetDirectLightingDirection(lighting, display.sim.analyticBoundary);
					projectedShadowParams = new ParticleFluidProjectedShadow.RecordParams(
						lighting.directLight.projectedShadowCompute,
						lighting.directLight.projectedShadow.projectedShadowMapBuffer,
						lighting.directLight.projectedShadow.projectedShadowMapTexture,
						lighting.materialTransportTexture,
						lighting.directLight.projectedShadowMapBins,
						effectiveLightDirection,
						lightingContext.renderLayout.SourceRegion,
						lightingContext.renderLayout.CausticRegion);
				}
				bool useProjectedShadow = lighting.directLight.projectedShadow.ShouldRender(lighting.directLight)
				                          && lighting.directLight.projectedShadow.RecordCurrentShadowMap(targetCommandBuffer, projectedShadowParams, out projectedShadowDirection);
				materialRenderer.MaterialMaps.BindTo(lighting);
				lighting.directLight.projectedShadow.ApplyToMaterial(lighting.lightingMaterial, useProjectedShadow, projectedShadowDirection, lighting.directLight.projectedShadowOffset, lighting.directLight.projectedShadowExpansion);
				lighting.Render(targetCommandBuffer, finalTarget, cam);
			}
			else
			{
				materialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, currentRenderLayout.DomainRegion);
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
			return display.sim != null ? display.sim.analyticBoundary.analyticBoundaryExpansion : 0f;
		}

		ParticleFluidRenderLayout2D GetRenderLayout(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidRenderRegion2D cropRegion = GetMaterialRenderRegion(display, cam, Mathf.Max(cam.pixelWidth, 1), Mathf.Max(cam.pixelHeight, 1));
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			float causticScale = lighting != null ? lighting.directLight.textureScale : sourceScale;
			ParticleFluidRenderRegion2D domainRegion = new ParticleFluidRenderRegion2D(
				cropRegion.WorldCenter,
				cropRegion.WorldSize,
				cropRegion.PixelSize.x,
				cropRegion.PixelSize.y);
			return new ParticleFluidRenderLayout2D(
				cropRegion,
				domainRegion,
				ParticleFluidRenderLayout2D.ScaledSize(cropRegion, sourceScale),
				ParticleFluidRenderLayout2D.ScaledSize(cropRegion, materialScale),
				ParticleFluidRenderLayout2D.ScaledSize(cropRegion, causticScale));
		}

		ParticleFluidRenderRegion2D GetMaterialRenderRegion(ParticleDisplay2D display, Camera cam, int fullWidth, int fullHeight)
		{
			ParticleFluidRenderRegion2D fullRegion = ParticleFluidRenderRegion2D.Full(cam, fullWidth, fullHeight);
			if (display == null || cam == null || !display.sim.analyticBoundary.useEllipticalBounds || !ShouldUseCroppedRenderRegion(display))
			{
				return fullRegion;
			}

			float expansion = GetAnalyticBoundaryExpansion(display);
			float cameraWorldUnitsPerPixel = fullRegion.WorldSize.y / Mathf.Max(fullHeight, 1);
			Vector2 center = display.sim.analyticBoundary.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.analyticBoundary.ellipseBoundsSize.x), Mathf.Abs(display.sim.analyticBoundary.ellipseBoundsSize.y)) + Vector2.one * expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return fullRegion;
			}

			float cutY = display.sim.analyticBoundary.obstacleY - expansion;
			Vector2 cameraMin = fullRegion.WorldBounds.min;
			Vector2 cameraMax = fullRegion.WorldBounds.max;
			Vector2 boundsMin = new Vector2(center.x - radii.x, Mathf.Max(center.y - radii.y, cutY));
			Vector2 boundsMax = new Vector2(center.x + radii.x, center.y + radii.y);
			Vector2 cropMin = Vector2.Max(cameraMin, boundsMin);
			Vector2 cropMax = Vector2.Min(cameraMax, boundsMax);
			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				return new ParticleFluidRenderRegion2D(new Bounds(fullRegion.WorldBounds.center, new Vector3(cameraWorldUnitsPerPixel, cameraWorldUnitsPerPixel, 0f)), 1, 1);
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

			Vector4 compositeUvRect = new Vector4(
				x / (float)fullWidth,
				y / (float)fullHeight,
				pixelWidth / (float)fullWidth,
				pixelHeight / (float)fullHeight
			);
			Vector2 worldMin = cameraMin + Vector2.Scale(new Vector2(compositeUvRect.x, compositeUvRect.y), fullRegion.WorldSize);
			Vector2 worldSize = Vector2.Scale(new Vector2(compositeUvRect.z, compositeUvRect.w), fullRegion.WorldSize);
			return new ParticleFluidRenderRegion2D(new Bounds(worldMin + worldSize * 0.5f, worldSize), pixelWidth, pixelHeight);
		}

		ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			ParticleFluidLighting2D lighting = display != null ? display.Lighting : null;
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
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp
			       || lightingDebug == ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadow;
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
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLightInit => 18,
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => 8,
				ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw => 17,
				ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion => 10,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection => 12,
				ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp => 13,
				ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadow => 14,
				_ => 0,
			};
		}
	}
}
