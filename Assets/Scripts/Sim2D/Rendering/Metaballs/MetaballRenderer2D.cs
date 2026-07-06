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
		private Material _metaballMaterial;
		private Material _debugMaterial;
		internal Material blurMaterial;
		private Material _velocityBlurMaterial;
		internal readonly MetaballMaterialRenderer2D materialRenderer = new ();
		internal RenderTexture combinedAccumulationTexture;
		internal RenderTexture combinedBlurTexture;
		internal RenderTexture normalAccumulationTexture;
		internal RenderTexture normalBlurTexture;
		internal RenderTexture velocityPhase0AccumulationTexture;
		internal RenderTexture velocityPhase0BlurTexture;
		internal RenderTexture velocityPhase1AccumulationTexture;
		internal RenderTexture velocityPhase1BlurTexture;

		private ParticleFluidRenderLayout2D _currentRenderLayout;
		private ParticleDisplay2D _currentDisplay;
		private Camera _currentCamera;

		public bool PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			_currentDisplay = display;
			_currentCamera = cam;
			EnsureMaterials(display);
			if (_metaballMaterial == null || blurMaterial == null || cam == null || display == null || display.mesh == null || display.argsBuffer == null)
			{
				return false;
			}

			EnsureRenderTextures(display);
			ApplyMetaballMaterialSettings(display);
			ApplyDebugSettings(display, cam);
			ApplyMaterialSettings(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				lighting.ApplyFrameSettings(cam, _currentRenderLayout, ShouldRenderVelocityTextures(display) ? velocityPhase0AccumulationTexture : null, ShouldRenderVelocityTextures(display) ? velocityPhase1AccumulationTexture : null);
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
			targetCommandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, _metaballMaterial, shaderPass, display.argsBuffer);
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
			if (targetCommandBuffer == null || _currentDisplay == null)
			{
				return;
			}
			ParticleFluidLighting2D lighting = GetActiveLighting(_currentDisplay);
			float effectiveNormalStrength = _currentDisplay.GetEffectiveNormalStrength(_currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, _currentDisplay.sim.analyticBoundary);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, _currentDisplay.metaballs);
			ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, _currentRenderLayout.domainRegion);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, _currentDisplay);
			ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, _currentDisplay, _currentCamera, lighting, effectiveNormalStrength);
			materialRenderer.RenderTransportMap(targetCommandBuffer, _currentRenderLayout.SourceRegion, _currentCamera);
			if (lighting != null)
			{
				materialRenderer.MaterialMaps.BindTo(lighting);
			}
		}

		public void RecordSurfaceMaterialMaps(CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null || _currentDisplay == null)
			{
				return;
			}
			ParticleFluidLighting2D lighting = GetActiveLighting(_currentDisplay);
			float effectiveNormalStrength = _currentDisplay.GetEffectiveNormalStrength(_currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, _currentDisplay.sim.analyticBoundary);
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, _currentDisplay.metaballs);
			ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, _currentRenderLayout.domainRegion);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, _currentDisplay);
			ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, _currentDisplay, _currentCamera, lighting, effectiveNormalStrength);
			materialRenderer.RenderSurfaceMaps(targetCommandBuffer, _currentRenderLayout.MaterialRegion, _currentCamera);
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

		public bool ShouldRenderVelocityTextures(ParticleDisplay2D display)
		{
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
				_currentRenderLayout,
				display.GetZoomScale(cam)
			);
		}

		public void Release()
		{
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture, velocityPhase0AccumulationTexture, velocityPhase0BlurTexture, velocityPhase1AccumulationTexture, velocityPhase1BlurTexture);
			materialRenderer.Release();

			if (_metaballMaterial != null)
			{
				Object.DestroyImmediate(_metaballMaterial);
				_metaballMaterial = null;
			}

			if (_debugMaterial != null)
			{
				Object.DestroyImmediate(_debugMaterial);
				_debugMaterial = null;
			}


			if (blurMaterial != null)
			{
				Object.DestroyImmediate(blurMaterial);
				blurMaterial = null;
			}

			if (_velocityBlurMaterial != null)
			{
				Object.DestroyImmediate(_velocityBlurMaterial);
				_velocityBlurMaterial = null;
			}
		}

		void EnsureMaterials(ParticleDisplay2D display)
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _metaballMaterial, display.metaballShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _debugMaterial, display.metaballs.debugShader);
			materialRenderer.EnsureMaterial(display.metaballs.materialShader);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				lighting.EnsureMaterials(lighting.lightingShader);
			}
			ParticleFluidRenderUtils.EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			Shader motionPyramidShader = Shader.Find("Hidden/Particle2DPhaseMotionPyramid");
			ParticleFluidRenderUtils.EnsureMaterial(ref _velocityBlurMaterial, motionPyramidShader != null ? motionPyramidShader : display.metaballs.blurShader);
		}



		void ApplyMetaballMaterialSettings(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			display.BindSimulationBuffers(_metaballMaterial);
			display.ApplyCommonParticleSettings(_metaballMaterial);
			_metaballMaterial.SetFloat("metaballSharpness", settings.sharpness);
			_metaballMaterial.SetFloat("metaballIntensity", settings.intensity);
			_metaballMaterial.SetVector("metaballRenderWorldCenter", _currentRenderLayout.domainRegion.WorldCenter);
			_metaballMaterial.SetVector("metaballRenderWorldSize", _currentRenderLayout.domainRegion.WorldSize);
			_metaballMaterial.SetInt("useEllipticalBounds", display.sim.analyticBoundary.useEllipticalBounds ? 1 : 0);
			_metaballMaterial.SetVector("ellipseBoundsCenter", display.sim.analyticBoundary.ellipseBoundsCenter);
			_metaballMaterial.SetVector("ellipseBoundsSize", display.sim.analyticBoundary.ellipseBoundsSize);
			_metaballMaterial.SetFloat("obstacleY", display.sim.analyticBoundary.obstacleY);
			_metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
		}

		void EnsureRenderTextures(ParticleDisplay2D display)
		{
			_currentRenderLayout = GetRenderLayout(display);
			int width = _currentRenderLayout.sourceSize.x;
			int height = _currentRenderLayout.sourceSize.y;

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
				materialRenderer.EnsureRenderTextures(_currentRenderLayout.materialSize, _currentRenderLayout.sourceSize);
			}
			else
			{
				materialRenderer.MaterialMaps.Release();
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (lighting != null)
			{
				lighting.EnsureLightingResources(_currentRenderLayout);
			}
		}

		void ApplyDebugSettings(ParticleDisplay2D display, Camera cam)
		{
			if (_debugMaterial == null)
			{
				return;
			}

			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			_debugMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			_debugMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			_debugMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture != null ? velocityPhase0AccumulationTexture : Texture2D.blackTexture);
			_debugMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture != null ? velocityPhase1AccumulationTexture : Texture2D.blackTexture);
			int debugShaderMode = GetDebugShaderMode(display, lighting);
			Texture causticMotionDebugTexture = Texture2D.blackTexture;
			if (lighting != null)
			{
				Texture processedMotionTexture = lighting.directLight.traceCaustics.temporalCaustics.GetTemporalMotionTextureAfterBlur();
				Texture rawMotionTexture = lighting.directLight.traceCaustics.causticMotionTexture;
				causticMotionDebugTexture = processedMotionTexture ?? rawMotionTexture ?? Texture2D.blackTexture;
			}
			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			_debugMaterial.SetVector("metaballWorldCenter", _currentRenderLayout.domainRegion.WorldCenter);
			_debugMaterial.SetVector("metaballWorldSize", _currentRenderLayout.domainRegion.WorldSize);
			Texture causticDebugTexture =
				lighting == null ? Texture2D.blackTexture :
				lighting.directLight.denoisingEnabled ? lighting.directLight.traceCaustics.temporalCaustics.causticTemporalTexture : lighting.directLight.traceCaustics.causticResolvedTexture;
			bool renderSoftLight = lighting != null && (lighting.gaussianSss.ShouldRender() || lighting.radianceCascadeGi.radianceCascadeEnabled);
			_debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			Texture softLightPhase1Tex = lighting != null && lighting.currentSoftLightPhase1Texture != null
				? lighting.currentSoftLightPhase1Texture
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
					debugTex0 = lighting != null && lighting.currentGaussianBlurTexture != null
						? lighting.currentGaussianBlurTexture
						: Texture2D.blackTexture;
					debugTex1 = softLightPhase1Tex;
					break;
				case 18:
					debugTex0 = lighting != null && lighting.currentGaussianInitTexture != null
						? lighting.currentGaussianInitTexture
						: Texture2D.blackTexture;
					break;
				case 17:
					debugTex0 = softLightPhase1Tex;
					break;
				case 10:
					debugTex0 = causticMotionDebugTexture;
					break;
			}
			_debugMaterial.SetTexture("DebugTex0", debugTex0);
			_debugMaterial.SetTexture("DebugTex1", debugTex1);
			_debugMaterial.SetColor("metaballPhase0DiffuseLightTint", lighting != null ? lighting.phase0Material.diffuseLightTint : Color.white);
			_debugMaterial.SetColor("metaballPhase1DiffuseLightTint", lighting != null ? lighting.phase1Material.diffuseLightTint : Color.white);
			_debugMaterial.SetFloat("metaballPhase0DiffuseAdditiveBlend", lighting != null ? lighting.phase0Material.diffuseAdditiveBlend : 0f);
			_debugMaterial.SetFloat("metaballPhase1DiffuseAdditiveBlend", lighting != null ? lighting.phase1Material.diffuseAdditiveBlend : 0f);
			_debugMaterial.SetInt("debugMode", debugShaderMode);
			_debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			_debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(_debugMaterial);
			_debugMaterial.SetFloat("particleCausticDebugExposure", lighting != null ? lighting.lightManager.GetCausticDebugExposure() : 1f);
			_debugMaterial.SetInt("particleCausticTemporalDebugEnabled", lighting != null && lighting.directLight.denoisingEnabled ? 1 : 0);
			_debugMaterial.SetVector("causticCurrentWorldCenter", lighting != null ? lighting.currentCausticWorldCenter : _currentRenderLayout.domainRegion.WorldCenter);
			_debugMaterial.SetVector("causticCurrentWorldSize", lighting != null ? lighting.currentCausticWorldSize : _currentRenderLayout.domainRegion.WorldSize);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void ApplyMaterialSettings(ParticleDisplay2D display)
		{
			materialRenderer.ApplySharedSettings(display.sim.analyticBoundary.analyticBoundaryExpansion);
		}

		public void RecordMotionPyramid(CommandBuffer targetCommandBuffer, float effectiveMotionBlurRadius)
		{
			if (_velocityBlurMaterial == null || velocityPhase0AccumulationTexture == null || velocityPhase0BlurTexture == null || velocityPhase1AccumulationTexture == null || velocityPhase1BlurTexture == null)
			{
				return;
			}

			GetMotionPyramidParams(effectiveMotionBlurRadius, out int levels, out float blendStrength);
			targetCommandBuffer.BeginSample("Metaballs/Pyramid Particle Motion");
			_velocityBlurMaterial.SetFloat(MotionPyramidBlendStrengthId, blendStrength);
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
				targetCommandBuffer.Blit(previousSource, tempMipIds[i], _velocityBlurMaterial, 0);
				previousSource = tempMipIds[i];
				previousWidth = width;
				previousHeight = height;
			}

			RenderTargetIdentifier currentLow = previousSource;
			for (int i = mipCount - 1; i >= 0; i--)
			{
				RenderTargetIdentifier currentHigh = i == 0 ? sourceTexture : tempMipIds[i - 1];
				targetCommandBuffer.SetGlobalTexture(MotionPyramidLowMipTexId, currentLow);
				targetCommandBuffer.Blit(currentHigh, scratchTexture, _velocityBlurMaterial, 1);
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
				ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
				ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
				ParticleFluidRasterLayoutBindings.ApplyMetaballGlobals(targetCommandBuffer, _currentRenderLayout.domainRegion);
				ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, display);
				ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, display, cam, lighting, effectiveNormalStrength);
				materialRenderer.RenderSurfaceMaps(targetCommandBuffer, _currentRenderLayout.MaterialRegion, cam);
				RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			}
			else if (_debugMaterial != null)
			{
				targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
				float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
				ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
				ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
				ParticleFluidRasterTextureBindings.ApplyGradientGlobals(targetCommandBuffer, display);
				ParticleFluidRasterTextureBindings.ApplyDebugGradientGlobals(targetCommandBuffer, display);
				ParticleFluidMetaballScalarBindings.ApplyGlobals(targetCommandBuffer, display, cam, lighting, effectiveNormalStrength);
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), ParticleFluidRenderUtils.CreateRegionMatrix(_currentRenderLayout.domainRegion), _debugMaterial, 0, 0);
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
			ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
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
				materialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, _currentRenderLayout.domainRegion);
			}
			targetCommandBuffer.EndSample("Metaballs/Material Pipeline");
		}

		void RecordVectorField(ParticleDisplay2D display, CommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.AppendVectorFieldDraw(targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		ParticleFluidRenderLayout2D GetRenderLayout(ParticleDisplay2D display)
		{
			ParticleFluidRenderRegion2D cropRegion = GetMaterialRenderRegion(display);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			float causticScale = lighting != null ? lighting.directLight.textureScale : sourceScale;
			return new ParticleFluidRenderLayout2D(
				cropRegion,
				cropRegion.ScaledSize(sourceScale),
				cropRegion.ScaledSize(materialScale),
				cropRegion.ScaledSize(causticScale));
		}

		ParticleFluidRenderRegion2D GetMaterialRenderRegion(ParticleDisplay2D display)
		{
			ParticleFluidRenderRegion2D fullRegion = ParticleFluidRenderRegion2D.Full(_currentCamera);
			if (!display.sim.analyticBoundary.useEllipticalBounds || !ShouldUseCroppedRenderRegion(display))
			{
				return fullRegion;
			}
			return fullRegion.CropToWorldBounds(display.sim.analyticBoundary.GetBounds());
		}

		ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			ParticleFluidLighting2D lighting = display != null ? display.Lighting : null;
			return lighting != null && lighting.isActiveAndEnabled ? lighting : null;
		}

		public bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
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
