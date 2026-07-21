using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		private Material _metaballMaterial;
		private Material _debugMaterial;
		internal Material blurMaterial;
		internal readonly MetaballMaterialRenderer2D materialRenderer = new ();
		internal RenderTexture combinedAccumulationTexture;
		internal RenderTexture combinedBlurTexture;
		internal RenderTexture normalAccumulationTexture;
		internal RenderTexture normalBlurTexture;
		internal RenderTexture velocityTexture;
		internal RenderTexture velocityBlurTexture;

		private Bounds _currentRenderRegion;
		private Vector2Int _currentSourceSize;
		private Vector2Int _currentVelocitySize;
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
			materialRenderer.RenderMaterialMaps(targetCommandBuffer, _currentRenderRegion, _currentCamera);
			if (_lighting != null)
			{
				_lighting.ApplyLightingInputs(materialRenderer.MaterialMaps.CreateLightingInputs(_currentRenderRegion, _currentMaterialSize, velocityTexture));
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

			return false;
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
			return new ParticleFluidLighting2D.FrameContext(display, cam, _currentRenderRegion, _currentMaterialSize);
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
			_currentVelocitySize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(display.metaballs.velocityTextureScale, 0.0001f));
			_currentMaterialSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, materialScale);
			_currentCausticSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, causticScale);

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, _currentSourceSize);
			if (ShouldRenderVelocityTextures(display))
			{
				ComputeHelper.CreateRenderTexture(ref velocityTexture, _currentVelocitySize);
				ComputeHelper.CreateRenderTexture(ref velocityBlurTexture, _currentVelocitySize);
			}
			else
			{
				ComputeHelper.Release(velocityTexture, velocityBlurTexture);
			}
			if (ShouldUseMaterialPipeline(display))
			{
				materialRenderer.EnsureRenderTextures(_currentMaterialSize);
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

			int debugShaderMode = GetDebugShaderMode(display, _lighting);
			_debugMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			Texture normalDebugTexture = debugShaderMode == (int)ParticleDisplay2D.DebugVisualization.Gradient && materialRenderer.MaterialMaps.normalTexture != null
				? materialRenderer.MaterialMaps.normalTexture
				: Texture2D.blackTexture;
			_debugMaterial.SetTexture("MaterialNormalTex", normalDebugTexture);
			_debugMaterial.SetTexture("MaterialTransportTex", materialRenderer.MaterialMaps.transportTexture != null ? materialRenderer.MaterialMaps.transportTexture : Texture2D.blackTexture);
			_debugMaterial.SetTexture("VelocityTex", velocityTexture != null ? velocityTexture : Texture2D.blackTexture);
			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			_debugMaterial.SetVector("domainWorldCenter", _currentRenderRegion.center);
			_debugMaterial.SetVector("domainWorldSize", _currentRenderRegion.size);
			Texture causticDebugTexture =
				_lighting == null ? Texture2D.blackTexture :
				_lighting.directLight.temporalSettings.denoisingEnabled ? _lighting.directLight.temporalCaustics.causticTemporalTexture : _lighting.directLight.causticResolvedTexture;
			Texture softLightPhase1Tex = _lighting != null && _lighting.currentSoftLightPhase1Texture != null
				? _lighting.currentSoftLightPhase1Texture
				: Texture2D.blackTexture;
			Texture debugTex0 = Texture2D.blackTexture;
			switch (debugShaderMode)
			{
				case 7:
				case 11:
					debugTex0 = causticDebugTexture != null ? causticDebugTexture : Texture2D.blackTexture;
					break;
				case 8:
					debugTex0 = _lighting != null && _lighting.currentGaussianBlurTexture != null
						? _lighting.currentGaussianBlurTexture
						: Texture2D.blackTexture;
					break;
				case 9:
					debugTex0 = softLightPhase1Tex;
					break;
			}
			_debugMaterial.SetTexture("DebugTex0", debugTex0);
			_debugMaterial.SetInt("debugMode", debugShaderMode);
			_debugMaterial.SetFloat("debugGradientMax", display.debugGradientMax);
			_debugMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			display.ApplyDebugClipSettings(_debugMaterial);
			_debugMaterial.SetFloat("particleCausticDebugExposure", _lighting != null ? _lighting.lightManager.GetCausticDebugExposure() : 1f);
			_debugMaterial.SetVector("domainWorldCenter", _lighting != null ? _lighting.currentCausticWorldCenter : _currentRenderRegion.center);
			_debugMaterial.SetVector("domainWorldSize", _lighting != null ? _lighting.currentCausticWorldSize : _currentRenderRegion.size);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		public void RecordVelocityGaussianBlur(CommandBuffer targetCommandBuffer, float effectiveMotionBlurRadius)
		{
			if (blurMaterial == null || velocityTexture == null || velocityBlurTexture == null)
			{
				return;
			}
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, effectiveMotionBlurRadius, blurMaterial, velocityTexture, velocityBlurTexture, "Metaballs/Blur Particle Motion");
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
				materialRenderer.RenderMaterialMaps(targetCommandBuffer, _currentRenderRegion, cam);
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
			if ((display.debugMode != ParticleDisplay2D.DebugVisualization.None || _lighting != null && _lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None) && _debugMaterial != null)
			{
				ApplyDebugSettings(display, cam);
				ParticleFluidPassBindings.ApplyMetaballDebugGlobals(targetCommandBuffer, display, cam, _lighting, display.GetEffectiveNormalStrength(display.EffectiveConfiguredBlurRadius));
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), _debugMaterial, 0, 0);
			}
			else if (_lighting != null && _lighting.lightingMaterial != null)
			{
				ParticleFluidLightingInputSet lightingInputs = materialRenderer.MaterialMaps.CreateLightingInputs(_currentRenderRegion, _currentMaterialSize, velocityTexture);
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
			       && (display.debugMode == ParticleDisplay2D.DebugVisualization.None || UsesMaterialMapDebugMode(display.debugMode))
			       && _lighting != null;
		}

		static bool UsesMaterialMapDebugMode(ParticleDisplay2D.DebugVisualization debugMode)
		{
			return debugMode is ParticleDisplay2D.DebugVisualization.Gradient
			       or ParticleDisplay2D.DebugVisualization.Curvature
			       or ParticleDisplay2D.DebugVisualization.Viscosity
			       or ParticleDisplay2D.DebugVisualization.Density
			       or ParticleDisplay2D.DebugVisualization.Temperature;
		}

		bool ShouldUseCroppedRenderRegion(ParticleDisplay2D display)
		{
			if (display != null && display.debugMode == ParticleDisplay2D.DebugVisualization.ParticleMotion)
			{
				return true;
			}

			if (ShouldUseMaterialPipeline(display))
			{
				return true;
			}

			if (_lighting == null)
			{
				return false;
			}

			ParticleFluidLighting2D.LightingDebugVisualization lightingDebug = _lighting.debugMode;
			return lightingDebug is ParticleFluidLighting2D.LightingDebugVisualization.Caustics or ParticleFluidLighting2D.LightingDebugVisualization.SoftLight or ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw;
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
				ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw => 9,
				_ => 0,
			};
		}
	}
}

