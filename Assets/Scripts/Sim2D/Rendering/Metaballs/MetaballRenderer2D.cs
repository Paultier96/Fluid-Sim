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
		static readonly int[] MotionPhase0MipIds = { MotionPhase0Mip0Id, MotionPhase0Mip1Id, MotionPhase0Mip2Id, MotionPhase0Mip3Id, MotionPhase0Mip4Id, MotionPhase0Mip5Id };
		private Material _metaballMaterial;
		private Material _debugMaterial;
		internal Material blurMaterial;
		private Material _velocityBlurMaterial;
		internal readonly MetaballMaterialRenderer2D materialRenderer = new ();
		internal RenderTexture combinedAccumulationTexture;
		internal RenderTexture combinedBlurTexture;
		internal RenderTexture normalAccumulationTexture;
		internal RenderTexture normalBlurTexture;
		internal RenderTexture velocityTexture;
		internal RenderTexture velocityBlurTexture;

		private Bounds _currentRenderRegion;
		private Vector2Int _currentSourceSize;
		private Vector2Int _currentMaterialSize;
		private Vector2Int _currentCausticSize;
		private ParticleDisplay2D _currentDisplay;
		private Camera _currentCamera;
		private ParticleFluidLighting2D _lighting;

		public bool PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			_currentDisplay = display;
			_currentCamera = cam;
			_lighting = ResolveActiveLighting(display);
			EnsureMaterials(display);
			if (_metaballMaterial == null || blurMaterial == null || cam == null || display == null || display.mesh == null || display.argsBuffer == null)
			{
				return false;
			}

			EnsureRenderTextures(display);
			ApplyMetaballMaterialSettings(display);
			ApplyDebugSettings(display, cam);
			materialRenderer.ApplySharedSettings(display.sim.analyticBoundary.analyticBoundaryExpansion);
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

		public void RecordMaterialMaps(CommandBuffer targetCommandBuffer, bool renderVelocityMap = true)
		{
			if (targetCommandBuffer == null || _currentDisplay == null)
			{
				return;
			}

			float effectiveNormalStrength = _currentDisplay.GetEffectiveNormalStrength(_currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidPassBindings.ApplyMetaballMaterialGlobals(targetCommandBuffer, _currentDisplay, _currentCamera, _lighting, _currentRenderRegion, effectiveNormalStrength);
			materialRenderer.RenderTransportMap(targetCommandBuffer, _currentRenderRegion, _currentCamera);
			if (_lighting != null)
			{
				_lighting.ApplyLightingInputs(materialRenderer.MaterialMaps.CreateLightingInputs(_currentRenderRegion, _currentSourceSize, velocityTexture));
			}
		}

		public void RecordSurfaceMaterialMaps(CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null || _currentDisplay == null)
			{
				return;
			}
			float effectiveNormalStrength = _currentDisplay.GetEffectiveNormalStrength(_currentDisplay.EffectiveConfiguredBlurRadius);
			materialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidPassBindings.ApplyMetaballMaterialGlobals(targetCommandBuffer, _currentDisplay, _currentCamera, _lighting, _currentRenderRegion, effectiveNormalStrength);
			materialRenderer.RenderSurfaceMaps(targetCommandBuffer, _currentRenderRegion, _currentCamera);
			if (_lighting != null)
			{
				_lighting.ApplyLightingInputs(materialRenderer.MaterialMaps.CreateLightingInputs(_currentRenderRegion, _currentSourceSize, velocityTexture));
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

			if (_lighting == null)
			{
				return false;
			}

			if (_lighting.directLight.temporalSettings.denoisingEnabled && _lighting.directLight.temporalSettings.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.ParticleMotion)
			{
				return true;
			}

			return _lighting.directLight.lightingMode == ParticleFluidDirectLight.LightingMode.Caustics
			       && (_lighting.directLight.temporalSettings.temporalMotionSource == ParticleFluidCausticsTemporal.TemporalMotionSource.CausticMotion
			           || _lighting.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
			return new ParticleFluidLighting2D.FrameContext(display, cam, _currentRenderRegion, _currentSourceSize);
		}

		public void Release()
		{
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture, velocityTexture, velocityBlurTexture);
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
			if (_lighting != null)
			{
				_lighting.EnsureMaterials(_lighting.lightingShader);
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
			_metaballMaterial.SetVector("metaballRenderWorldCenter", _currentRenderRegion.center);
			_metaballMaterial.SetVector("metaballRenderWorldSize", _currentRenderRegion.size);
			_metaballMaterial.SetInt("useEllipticalBounds", display.sim.analyticBoundary.useEllipticalBounds ? 1 : 0);
			_metaballMaterial.SetVector("ellipseBoundsCenter", display.sim.analyticBoundary.BoundsCenter);
			_metaballMaterial.SetVector("ellipseBoundsSize", display.sim.analyticBoundary.boundsSize);
			_metaballMaterial.SetFloat("obstacleY", display.sim.analyticBoundary.obstacleY);
			_metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
		}

		void EnsureRenderTextures(ParticleDisplay2D display)
		{
			Bounds cropRegion = GetRenderRegion(display, out Vector2Int baseResolution);
			float materialScale = _lighting.materialMapTextureScale;
			float causticScale = _lighting.directLight.textureScale;
			_currentRenderRegion = cropRegion;
			_currentSourceSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(display.metaballs.renderTextureScale, 0.0001f));
			_currentMaterialSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, materialScale);
			_currentCausticSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, causticScale);

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, _currentSourceSize);
			if (ShouldRenderVelocityTextures(display))
			{
				ComputeHelper.CreateRenderTexture(ref velocityTexture, _currentSourceSize);
				ComputeHelper.CreateRenderTexture(ref velocityBlurTexture, _currentSourceSize);
			}
			else
			{
				ComputeHelper.Release(velocityTexture, velocityBlurTexture);
			}
			if (ShouldUseMaterialPipeline(display))
			{
				materialRenderer.EnsureRenderTextures(_currentMaterialSize, _currentSourceSize);
			}
			else
			{
				materialRenderer.MaterialMaps.Release();
			}
			_lighting.EnsureLightingResources(_currentRenderRegion, _currentCausticSize);
		}

		void ApplyDebugSettings(ParticleDisplay2D display, Camera cam)
		{
			if (_debugMaterial == null)
			{
				return;
			}

			_debugMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			_debugMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			_debugMaterial.SetTexture("VelocityTex", velocityTexture != null ? velocityTexture : Texture2D.blackTexture);
			int debugShaderMode = GetDebugShaderMode(display, _lighting);
			Texture causticMotionDebugTexture = Texture2D.blackTexture;
			if (_lighting != null)
			{
				Texture processedMotionTexture = _lighting.directLight.temporalCaustics.GetTemporalMotionTextureAfterBlur();
				Texture rawMotionTexture = _lighting.directLight.causticMotionTexture;
				causticMotionDebugTexture = processedMotionTexture ?? rawMotionTexture ?? Texture2D.blackTexture;
			}
			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			_debugMaterial.SetVector("domainWorldCenter", _currentRenderRegion.center);
			_debugMaterial.SetVector("domainWorldSize", _currentRenderRegion.size);
			Texture causticDebugTexture =
				_lighting == null ? Texture2D.blackTexture :
				_lighting.directLight.temporalSettings.denoisingEnabled ? _lighting.directLight.temporalCaustics.causticTemporalTexture : _lighting.directLight.causticResolvedTexture;
			bool renderSoftLight = _lighting != null && (_lighting.gaussianSss.ShouldRender() || _lighting.radianceCascadeGi.isActiveAndEnabled);
			_debugMaterial.SetInt("metaballPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			Texture softLightPhase1Tex = _lighting != null && _lighting.currentSoftLightPhase1Texture != null
				? _lighting.currentSoftLightPhase1Texture
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
					debugTex0 = _lighting != null && _lighting.currentGaussianBlurTexture != null
						? _lighting.currentGaussianBlurTexture
						: Texture2D.blackTexture;
					debugTex1 = softLightPhase1Tex;
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
			_debugMaterial.SetColor("metaballPhase0DiffuseLightTint", _lighting != null ? _lighting.phase0Material.diffuseLightTint : Color.white);
			_debugMaterial.SetColor("metaballPhase1DiffuseLightTint", _lighting != null ? _lighting.phase1Material.diffuseLightTint : Color.white);
			_debugMaterial.SetFloat("metaballPhase0DiffuseAdditiveBlend", _lighting != null ? _lighting.phase0Material.diffuseAdditiveBlend : 0f);
			_debugMaterial.SetFloat("metaballPhase1DiffuseAdditiveBlend", _lighting != null ? _lighting.phase1Material.diffuseAdditiveBlend : 0f);
			_debugMaterial.SetInt("debugMode", debugShaderMode);
			_debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			_debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(_debugMaterial);
			_debugMaterial.SetFloat("particleCausticDebugExposure", _lighting != null ? _lighting.lightManager.GetCausticDebugExposure() : 1f);
			_debugMaterial.SetInt("particleCausticTemporalDebugEnabled", _lighting != null && _lighting.directLight.temporalSettings.denoisingEnabled ? 1 : 0);
			_debugMaterial.SetVector("domainWorldCenter", _lighting != null ? _lighting.currentCausticWorldCenter : _currentRenderRegion.center);
			_debugMaterial.SetVector("domainWorldSize", _lighting != null ? _lighting.currentCausticWorldSize : _currentRenderRegion.size);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		public void RecordMotionPyramid(CommandBuffer targetCommandBuffer, float effectiveMotionBlurRadius)
		{
			if (_velocityBlurMaterial == null || velocityTexture == null || velocityBlurTexture == null)
			{
				return;
			}

			GetMotionPyramidParams(effectiveMotionBlurRadius, out int levels, out float blendStrength);
			targetCommandBuffer.BeginSample("Metaballs/Pyramid Particle Motion");
			_velocityBlurMaterial.SetFloat(MotionPyramidBlendStrengthId, blendStrength);
			RecordSinglePhaseMotionPyramid(targetCommandBuffer, velocityTexture, velocityBlurTexture, MotionPhase0MipIds, levels);
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
			return display.GetEffectiveMotionBlurRadius(cam, _lighting != null ? _lighting.directLight.temporalSettings.motionBlurRadius : 0f);
		}

		void RecordMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			if (useMaterialPipeline && materialRenderer.IsReady)
			{
				float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
				ParticleFluidPassBindings.ApplyMetaballMaterialGlobals(targetCommandBuffer, display, cam, _lighting, _currentRenderRegion, effectiveNormalStrength);
				materialRenderer.RenderSurfaceMaps(targetCommandBuffer, _currentRenderRegion, cam);
				RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			}
			else if (_debugMaterial != null)
			{
				targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
				float effectiveNormalStrength = display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius);
				ParticleFluidPassBindings.ApplyMetaballDebugGlobals(targetCommandBuffer, display, cam, _lighting, effectiveNormalStrength);
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), _debugMaterial, 0, 0);
				targetCommandBuffer.EndSample("Metaballs/Debug Composite");
			}
		}

		void RecordPreparedMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			bool useMaterialPipeline = ShouldUseMaterialPipeline(display);
			if (!useMaterialPipeline || !materialRenderer.IsReady)
			{
				return;
			}

			targetCommandBuffer.BeginSample("Metaballs/Material Pipeline");
			ParticleFluidLayoutBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim.analyticBoundary);
			if (_lighting != null && _lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None && _debugMaterial != null)
			{
				ApplyDebugSettings(display, cam);
				ParticleFluidPassBindings.ApplyMetaballDebugGlobals(targetCommandBuffer, display, cam, _lighting, display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius));
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), _debugMaterial, 0, 0);
			}
			else if (_lighting != null && _lighting.lightingMaterial != null)
			{
				ParticleFluidLightingInputSet lightingInputs = materialRenderer.MaterialMaps.CreateLightingInputs(_currentRenderRegion, _currentSourceSize, velocityTexture);
				ParticleFluidLighting2D.FrameContext lightingContext = _lighting.PrepareLighting(cam, lightingInputs);
				_lighting.RenderLit(targetCommandBuffer, finalTarget, cam, lightingContext, lightingInputs.transportTexture);
			}
			else
			{
				materialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, _currentRenderRegion);
			}
			targetCommandBuffer.EndSample("Metaballs/Material Pipeline");
		}

		void RecordVectorField(ParticleDisplay2D display, CommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.AppendVectorFieldDraw(targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		Bounds GetRenderRegion(ParticleDisplay2D display, out Vector2Int resolution)
		{
			bool crop = display.sim.analyticBoundary.useEllipticalBounds && ShouldUseCroppedRenderRegion(display);
			Bounds? cropBounds = crop ? display.sim.analyticBoundary.CropBounds : null;
			return ParticleFluidRenderBounds2D.GetCameraRenderRegion(_currentCamera, cropBounds, crop, out resolution);
		}

		static ParticleFluidLighting2D ResolveActiveLighting(ParticleDisplay2D display)
		{
			return display != null ? display.ActiveLighting : null;
		}

		public bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
		{
			return display != null
			       && display.debugMode == ParticleDisplay2D.DebugVisualization.None
			       && _lighting != null;
		}

		bool ShouldUseCroppedRenderRegion(ParticleDisplay2D display)
		{
			if (ShouldUseMaterialPipeline(display))
			{
				return true;
			}

			if (_lighting == null)
			{
				return false;
			}

			ParticleFluidLighting2D.LightingDebugVisualization lightingDebug = _lighting.debugMode;
			return lightingDebug is ParticleFluidLighting2D.LightingDebugVisualization.Caustics or ParticleFluidLighting2D.LightingDebugVisualization.SoftLight or ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw or ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion or ParticleFluidLighting2D.LightingDebugVisualization.TemporalRejection or ParticleFluidLighting2D.LightingDebugVisualization.TemporalClamp or ParticleFluidLighting2D.LightingDebugVisualization.ProjectedShadow;
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

