using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace Seb.Fluid2D.Rendering
{
	[Serializable]
	public sealed class MetaballRenderer2D
	{
		private static readonly int MetaballSharpness = Shader.PropertyToID("metaballSharpness");
		private static readonly int MetaballIntensity = Shader.PropertyToID("metaballIntensity");
		private static readonly int DomainWorldCenter = Shader.PropertyToID("domainWorldCenter");
		private static readonly int DomainWorldSize = Shader.PropertyToID("domainWorldSize");
		private static readonly int CombinedTex = Shader.PropertyToID("CombinedTex");
		private static readonly int MaterialNormalTex = Shader.PropertyToID("MaterialNormalTex");
		private static readonly int MaterialTransportTex = Shader.PropertyToID("MaterialTransportTex");
		private static readonly int VelocityTex = Shader.PropertyToID("VelocityTex");
		private static readonly int DebugTex0 = Shader.PropertyToID("DebugTex0");
		private static readonly int DebugMode = Shader.PropertyToID("debugMode");
		private static readonly int DebugGradientMax = Shader.PropertyToID("debugGradientMax");
		private static readonly int MotionDebugDeltaTime = Shader.PropertyToID("motionDebugDeltaTime");
		private static readonly int DebugShowClipping = Shader.PropertyToID("debugShowClipping");
		private static readonly int ParticleCausticDebugExposure = Shader.PropertyToID("particleCausticDebugExposure");

		private const int DebugModeCaustics = 7;
		private const int DebugModeSoftLight = 8;
		private const int DebugModeRadianceCascadeRaw = 9;

		[Header("Shaders")]
		public Shader debugShader;
		public Shader materialShader;
		public Shader blurShader;

		[Header("Shape - Surface")]
		[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
		[Range(0.125f, 1f)] public float velocityTextureScale = 0.5f;
		[Min(0)] public float blurRadius = 6;
		[Min(0)] public float densityThreshold = 0.18f;
		[Min(0.0001f)] public float edgeSoftness = 0.06f;

		[Header("Shape - Phase Boundary")]
		[Tooltip("Screen-space width in pixels for anti-aliased blending between fluid phases.")]
		[Min(0.0001f)] public float phaseBlendWidth = 1f;
		[Tooltip("for the transport map phase blending used by ray marched lighting")]
		[Min(0.0001f)] public float transportPhaseBlendWidth = 1f;
		[FormerlySerializedAs("phase0RenderBias")] [Range(-0.99f, 0.99f)] public float renderBias;
		[Tooltip("How strongly phase boundary bias redistributes normal strength.")]
		[Range(0f, 10f)] public float phaseBiasNormalStrength = 0.5f;

		[Header("Shape - Particle Kernel")]
		[Min(0.01f)] public float sharpness = 3.5f;
		[Min(0)] public float intensity = 1.0f;

		[Header("Lighting - Normals")]
		[Min(0f)] public float normalStrength = 1f;
		[Tooltip("Curves the reconstructed normal magnitude before rebuilding Z. Values above 1 keep the surface flatter for longer and push the steep falloff closer to the silhouette.")]
		[Min(0.0001f)] public float normalProfileCurve = 1f;
		[Min(0f)] public float normalBlurCompensation = 0.5f;

		[Header("Ghost Boundary Normals")]
		[Tooltip("Strength of the analytic ellipse/cut-boundary normals in the metaball composite. Values above 1 make the boundary normal ramp steeper; negative values flip the direction.")]
		[Range(-4f, 4f)] public float ghostBoundaryNormalStrength = 1f;

		private Material _metaballMaterial;
		private Material _debugMaterial;
		[NonSerialized] internal Material blurMaterial;
		 private MetaballMaterialRenderer2D _materialRenderer;
		internal MetaballMaterialRenderer2D MaterialRenderer => _materialRenderer ??= new MetaballMaterialRenderer2D();
		[NonSerialized] internal RenderTexture combinedAccumulationTexture;
		[NonSerialized] internal RenderTexture combinedBlurTexture;
		[NonSerialized] internal RenderTexture normalAccumulationTexture;
		[NonSerialized] internal RenderTexture normalBlurTexture;
		[NonSerialized] internal RenderTexture velocityTexture;
		[NonSerialized] internal RenderTexture velocityBlurTexture;

		private Bounds _currentRenderRegion;
		private Vector2Int _currentSourceSize;
		private Vector2Int _currentVelocitySize;
		private Vector2Int _currentMaterialSize;
		private Vector2Int _currentCausticSize;
		private ParticleDisplay2D _currentDisplay;
		private Camera _currentCamera;
		private ParticleFluidLighting2D _lighting;

		public void PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			_currentDisplay = display;
			_currentCamera = cam;
			_lighting = display.ActiveLighting;
			ParticleFluidRenderUtils.EnsureMaterial(ref _metaballMaterial, display.metaballShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _debugMaterial, debugShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref blurMaterial, blurShader);
			MaterialRenderer.EnsureMaterial(materialShader);
			EnsureRenderTextures();
			ApplyMetaballMaterialSettings();
			ApplyDebugSettings();
		}

		public void RecordAccumulationTarget(ParticleDisplay2D display, IRasterCommandBuffer targetCommandBuffer, int shaderPass)
		{
			targetCommandBuffer.SetGlobalVector(DomainWorldCenter, _currentRenderRegion.center);
			targetCommandBuffer.SetGlobalVector(DomainWorldSize, _currentRenderRegion.size);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			targetCommandBuffer.DrawMeshInstancedIndirect(display.particleMesh, 0, _metaballMaterial, shaderPass, display.argsBuffer);
		}

		public void RecordFallbackComposite(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (_debugMaterial != null)
			{
				targetCommandBuffer.BeginSample("Metaballs/Debug Composite");
				ParticleFluidRenderBindings.ApplyMetaballDebugGlobals(targetCommandBuffer, display, cam, _lighting);
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), _debugMaterial, 0, 0);
				targetCommandBuffer.EndSample("Metaballs/Debug Composite");
			}

			RecordVectorField(display, targetCommandBuffer);
		}

		public void RecordMaterialMaps(CommandBuffer targetCommandBuffer)
		{
			MaterialRenderer.SetSourceTextures(combinedAccumulationTexture, normalAccumulationTexture);
			ParticleFluidRenderBindings.ApplyMetaballMaterialGlobals(targetCommandBuffer, _currentDisplay, _currentCamera, _lighting, _currentRenderRegion);
			MaterialRenderer.RenderMaterialMaps(targetCommandBuffer, _currentRenderRegion, _currentCamera);
			_lighting.ApplyMaterialMaps(MaterialRenderer.materialMaps);
		}

		public void RecordCompositeWithPreparedMaterialMaps(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			RecordPreparedMaterialAndLighting(display, cam, targetCommandBuffer, finalTarget);
			RecordVectorField(display, targetCommandBuffer);
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
			return new ParticleFluidLighting2D.FrameContext(display, cam, _currentRenderRegion, _currentMaterialSize);
		}

		public void Release()
		{
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture, velocityTexture, velocityBlurTexture);
			MaterialRenderer.Release();
			Object.DestroyImmediate(_metaballMaterial);
			_metaballMaterial = null;
			Object.DestroyImmediate(_debugMaterial);
			_debugMaterial = null;
			Object.DestroyImmediate(blurMaterial);
			blurMaterial = null;
		}


		private void ApplyMetaballMaterialSettings()
		{
			_currentDisplay.BindSimulationBuffers(_metaballMaterial);
			_currentDisplay.ApplyCommonParticleSettings(_metaballMaterial);
			_metaballMaterial.SetFloat(MetaballSharpness, sharpness);
			_metaballMaterial.SetFloat(MetaballIntensity, intensity);
		}

		private void EnsureRenderTextures()
		{
			bool crop = _currentDisplay.sim.analyticBoundary.useEllipticalBounds && ((_currentDisplay != null && _currentDisplay.debugMode == ParticleDisplay2D.DebugVisualization.ParticleMotion)
				|| ShouldUseMaterialPipeline(_currentDisplay) || (_lighting != null && _lighting.debugMode is
					ParticleFluidLighting2D.LightingDebugVisualization.Caustics
					or ParticleFluidLighting2D.LightingDebugVisualization.SoftLight
					or ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw));
			Bounds? cropBounds = crop ? _currentDisplay.sim.analyticBoundary.CropBounds : null;
			Bounds cropRegion = ParticleFluidRenderBounds2D.GetCameraRenderRegion(_currentCamera, cropBounds, crop, out Vector2Int baseResolution);
			float materialScale = _lighting.materialMapTextureScale;
			float causticScale = _lighting.directLight.textureScale;
			_currentRenderRegion = cropRegion;
			_currentSourceSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(renderTextureScale, 0.0001f));
			_currentVelocitySize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(velocityTextureScale, 0.0001f));
			_currentMaterialSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, materialScale);
			_currentCausticSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, causticScale);

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, _currentSourceSize);
			if (_lighting.directLight.temporalCaustics.ShouldRenderVelocityTextures(_currentDisplay))
			{
				ComputeHelper.CreateRenderTexture(ref velocityTexture, _currentVelocitySize);
				ComputeHelper.CreateRenderTexture(ref velocityBlurTexture, _currentVelocitySize);
			}
			else
			{
				ComputeHelper.Release(velocityTexture, velocityBlurTexture);
			}
			if (ShouldUseMaterialPipeline(_currentDisplay))
			{
				MaterialRenderer.materialMaps.EnsureRenderTextures(_currentMaterialSize, "Particle2D");
			}
			else
			{
				MaterialRenderer.materialMaps.Release();
			}
			_lighting.EnsureLightingResources(_currentRenderRegion, _currentCausticSize);
		}

		private void ApplyDebugSettings()
		{
			int debugShaderMode = GetDebugShaderMode(_currentDisplay, _lighting);
			_debugMaterial.SetTexture(CombinedTex, combinedAccumulationTexture);
			Texture normalDebugTexture = debugShaderMode == (int)ParticleDisplay2D.DebugVisualization.Gradient && MaterialRenderer.materialMaps.normalTexture != null
				? MaterialRenderer.materialMaps.normalTexture
				: Texture2D.blackTexture;
			_debugMaterial.SetTexture(MaterialNormalTex, normalDebugTexture);
			_debugMaterial.SetTexture(MaterialTransportTex, MaterialRenderer.materialMaps.transportTexture != null ? MaterialRenderer.materialMaps.transportTexture : Texture2D.blackTexture);
			_debugMaterial.SetTexture(VelocityTex, velocityTexture != null ? velocityTexture : Texture2D.blackTexture);
			_debugMaterial.SetVector(DomainWorldCenter, _currentRenderRegion.center);
			_debugMaterial.SetVector(DomainWorldSize, _currentRenderRegion.size);
			Texture causticDebugTexture =
				_lighting == null ? Texture2D.blackTexture :
				_lighting.directLight.temporalCaustics.denoisingEnabled ? _lighting.directLight.temporalCaustics.causticTemporalTexture : _lighting.directLight.causticResolvedTexture;
			Texture softLightPhase1Tex = _lighting != null && _lighting.currentSoftLightPhase1Texture != null
				? _lighting.currentSoftLightPhase1Texture
				: Texture2D.blackTexture;
			Texture debugTex = Texture2D.blackTexture;
			switch (debugShaderMode)
			{
				case DebugModeCaustics: debugTex = causticDebugTexture != null ? causticDebugTexture : Texture2D.blackTexture;
					break;
				case DebugModeSoftLight: debugTex = _lighting != null && _lighting.currentGaussianBlurTexture != null ? _lighting.currentGaussianBlurTexture : Texture2D.blackTexture;
					break;
				case DebugModeRadianceCascadeRaw: debugTex = softLightPhase1Tex;
					break;
			}
			_debugMaterial.SetTexture(DebugTex0, debugTex);
			_debugMaterial.SetInt(DebugMode, debugShaderMode);
			_debugMaterial.SetFloat(DebugGradientMax, _currentDisplay.debugGradientMax);
			_debugMaterial.SetFloat(MotionDebugDeltaTime, _currentDisplay.sim.CurrentSimulationDeltaTime);
			_debugMaterial.SetInt(DebugShowClipping, _currentDisplay.debugShowClipping ? 1 : 0);
			_debugMaterial.SetFloat(ParticleCausticDebugExposure, _lighting != null ? _lighting.lightManager.GetCausticDebugExposure() : 1f);
			_debugMaterial.SetVector(DomainWorldCenter, _lighting != null ? _lighting.currentCausticWorldCenter : _currentRenderRegion.center);
			_debugMaterial.SetVector(DomainWorldSize, _lighting != null ? _lighting.currentCausticWorldSize : _currentRenderRegion.size);
		}

		public void RecordVelocityGaussianBlur(CommandBuffer targetCommandBuffer)
		{
			if (blurMaterial == null || velocityTexture == null || velocityBlurTexture == null)
			{
				return;
			}
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, EffectiveVelocityBlurRadius, blurMaterial, velocityTexture, velocityBlurTexture, "Metaballs/Blur Particle Motion");
		}

		public float EffectiveVelocityBlurRadius => _lighting != null ? _currentDisplay.GetEffectiveMotionBlurRadius(_currentCamera, _lighting.directLight.temporalCaustics.motionBlurRadius) : 0f;

		private void RecordPreparedMaterialAndLighting(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			targetCommandBuffer.BeginSample("Metaballs/Material Pipeline");
			if ((display.debugMode != ParticleDisplay2D.DebugVisualization.None || _lighting != null && _lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None) && _debugMaterial != null)
			{
				ApplyDebugSettings();
				ParticleFluidRenderBindings.ApplyMetaballDebugGlobals(targetCommandBuffer, display, cam, _lighting);
				targetCommandBuffer.SetRenderTarget(finalTarget);
				targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), _debugMaterial, 0, 0);
			}
			else if (_lighting != null)
			{
				_lighting.Render(targetCommandBuffer, finalTarget);
			}
			else
			{
				MaterialRenderer.RenderUnlit(targetCommandBuffer, finalTarget, _currentRenderRegion);
			}
			targetCommandBuffer.EndSample("Metaballs/Material Pipeline");
		}

		private void RecordVectorField(ParticleDisplay2D display, CommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.vectorField.AppendDraw(display, targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		public bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
		{
			return _lighting != null && display != null && display.debugMode <= ParticleDisplay2D.DebugVisualization.Temperature;
		}

		public static int GetDebugShaderMode(ParticleDisplay2D display, ParticleFluidLighting2D settings)
		{
			if (display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return (int)display.debugMode;
			}

			if (settings == null)
			{
				return 0;
			}

			return settings.debugMode switch
			{
				ParticleFluidLighting2D.LightingDebugVisualization.Caustics => DebugModeCaustics,
				ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => DebugModeSoftLight,
				ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw => DebugModeRadianceCascadeRaw,
				_ => 0,
			};
		}
	}
}
