using System;
using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
		private static readonly int HashGridCellSize = Shader.PropertyToID("hashGridCellSize");
		private static readonly int HashGridWorldCenter = Shader.PropertyToID("hashGridWorldCenter");
		private static readonly int HashGridWorldSize = Shader.PropertyToID("hashGridWorldSize");
		private static readonly int ShowHashGridOverlay = Shader.PropertyToID("showHashGridOverlay");
		private static readonly int HashGridOverlayOnly = Shader.PropertyToID("hashGridOverlayOnly");

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
		[NonSerialized] internal RenderTexture normalAccumulationTexture;
		[NonSerialized] internal RenderTexture velocityTexture;

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

		public void RecordAccumulationRenderGraph(RenderGraph renderGraph, TextureHandle combinedHandle, TextureHandle normalHandle, TextureHandle velocityHandle, bool renderVelocityTextures)
		{
			RecordCombinedNormalAccumulationPass(renderGraph, combinedHandle, normalHandle);
			if (renderVelocityTextures)
			{
				RecordAccumulationPass(renderGraph, "Velocity Accumulation", velocityHandle, 1);
			}
		}

		private void RecordCombinedNormalAccumulationPass(RenderGraph renderGraph, TextureHandle combinedTarget, TextureHandle normalTarget)
		{
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Combined + Normal Accumulation", out MetaballAccumulationPassData passData);

			passData.shaderPass = 0;
			passData.renderRegion = _currentRenderRegion;
			passData.particleMesh = _currentDisplay.particleMesh;
			passData.argsBuffer = _currentDisplay.argsBuffer;
			passData.material = _metaballMaterial;
			builder.SetRenderAttachment(combinedTarget, 0);
			builder.SetRenderAttachment(normalTarget, 1);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (MetaballAccumulationPassData data, RasterGraphContext context) =>
			{
				RasterCommandBuffer cmd = context.cmd;
				cmd.SetGlobalVector(DomainWorldCenter, data.renderRegion.center);
				cmd.SetGlobalVector(DomainWorldSize, data.renderRegion.size);
				cmd.DrawMeshInstancedIndirect(data.particleMesh, 0, data.material, data.shaderPass, data.argsBuffer);
			});
		}

		private void RecordAccumulationPass(RenderGraph renderGraph, string passName, TextureHandle target, int shaderPass)
		{
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(passName, out MetaballAccumulationPassData passData);

			passData.shaderPass = shaderPass;
			passData.renderRegion = _currentRenderRegion;
			passData.particleMesh = _currentDisplay.particleMesh;
			passData.argsBuffer = _currentDisplay.argsBuffer;
			passData.material = _metaballMaterial;
			builder.SetRenderAttachment(target, 0);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (MetaballAccumulationPassData data, RasterGraphContext context) =>
			{
				RasterCommandBuffer cmd = context.cmd;
				cmd.SetGlobalVector(DomainWorldCenter, data.renderRegion.center);
				cmd.SetGlobalVector(DomainWorldSize, data.renderRegion.size);
				cmd.DrawMeshInstancedIndirect(data.particleMesh, 0, data.material, data.shaderPass, data.argsBuffer);
			});
		}

		public void RecordSurfaceBlurRenderGraph(RenderGraph renderGraph, TextureHandle combinedHandle, TextureHandle combinedBlurHandle, TextureHandle normalHandle, TextureHandle normalBlurHandle)
		{
			float surfaceBlurRadius = _currentDisplay.EffectiveConfiguredBlurRadius * _currentDisplay.GetZoomScale(_currentCamera) * renderTextureScale;
			ParticleFluidRenderUtils.RecordGaussianBlur(renderGraph, "Surface Blur Combined", surfaceBlurRadius, blurMaterial, combinedHandle, combinedBlurHandle, _currentSourceSize);
			ParticleFluidRenderUtils.RecordGaussianBlur(renderGraph, "Surface Blur Normal", surfaceBlurRadius, blurMaterial, normalHandle, normalBlurHandle, _currentSourceSize);
		}

		private static void UseIfValid(IBaseRenderGraphBuilder builder, TextureHandle texture, AccessFlags accessFlags)
		{
			if (texture.IsValid())
			{
				builder.UseTexture(texture, accessFlags);
			}
		}

		public void RecordVelocityBlurRenderGraph(RenderGraph renderGraph, TextureHandle velocityHandle, TextureHandle velocityBlurHandle, bool renderVelocityTextures)
		{
			if (!renderVelocityTextures)
			{
				return;
			}

			ParticleFluidRenderUtils.RecordGaussianBlur(renderGraph, "Velocity Gaussian Blur", EffectiveVelocityBlurRadius, blurMaterial, velocityHandle, velocityBlurHandle, _currentVelocitySize);
		}

		public void RecordMaterialMapsRenderGraph(
			RenderGraph renderGraph,
			TextureHandle combinedHandle,
			TextureHandle normalHandle,
			TextureHandle gradientAtlasHandle,
			TextureHandle materialAlbedoHandle,
			TextureHandle materialNormalHandle,
			TextureHandle materialTransportHandle)
		{
			MaterialRenderer.RecordRenderGraph(
				renderGraph,
				combinedHandle,
				normalHandle,
				gradientAtlasHandle,
				materialAlbedoHandle,
				materialNormalHandle,
				materialTransportHandle,
				CreateLightingContext(_currentDisplay, _currentCamera),
				_lighting,
				combinedAccumulationTexture,
				normalAccumulationTexture);
		}

		internal void RecordCompositeRenderGraph(
			RenderGraph renderGraph,
			TextureHandle colorHandle,
			bool useMaterialPipeline,
			TextureHandle combinedHandle,
			TextureHandle normalHandle,
			TextureHandle velocityHandle,
			TextureHandle materialAlbedoHandle,
			TextureHandle materialNormalHandle,
			TextureHandle materialTransportHandle,
			TextureHandle gradientAtlasHandle,
			LightingResourceHandles lightingResources)
		{
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Composite", out CompositePassData passData);
			passData.useMaterialPipeline = useMaterialPipeline;
			passData.renderer = this;
			passData.display = _currentDisplay;
			passData.camera = _currentCamera;
			passData.lighting = _lighting;
			passData.debugMaterial = _debugMaterial;
			passData.renderRegion = _currentRenderRegion;
			passData.materialRenderer = MaterialRenderer;
			passData.gradientAtlas = gradientAtlasHandle;
			builder.SetRenderAttachment(colorHandle, 0, AccessFlags.Write);
			UseIfValid(builder, combinedHandle, AccessFlags.Read);
			UseIfValid(builder, normalHandle, AccessFlags.Read);
			UseIfValid(builder, velocityHandle, AccessFlags.Read);
			UseIfValid(builder, materialAlbedoHandle, AccessFlags.Read);
			UseIfValid(builder, materialNormalHandle, AccessFlags.Read);
			UseIfValid(builder, materialTransportHandle, AccessFlags.Read);
			UseIfValid(builder, gradientAtlasHandle, AccessFlags.Read);
			UseIfValid(builder, lightingResources.selectedCaustics, AccessFlags.Read);
			UseIfValid(builder, lightingResources.gaussianSoftLight0, AccessFlags.Read);
			UseIfValid(builder, lightingResources.selectedRadianceCascade, AccessFlags.Read);
			builder.AllowGlobalStateModification(true);
			builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
			{
				RasterCommandBuffer cmd = context.cmd;
				if (data.useMaterialPipeline)
				{
					cmd.BeginSample("Metaballs/Material Pipeline");
					bool hasDebugView = data.display.debugMode != ParticleDisplay2D.DebugVisualization.None ||
						data.lighting != null && data.lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None;
					if (hasDebugView && data.debugMaterial != null)
					{
						data.renderer.ApplyDebugSettings();
						ParticleFluidRenderBindings.ApplyMetaballDebugGlobals(cmd, data.display, data.camera, data.lighting, data.gradientAtlas);
						cmd.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), data.renderRegion.CreateRegionMatrix(), data.debugMaterial, 0, 0);
					}
					else if (data.lighting != null)
					{
						data.lighting.Render(cmd, data.gradientAtlas);
					}
					else
					{
						data.materialRenderer.RenderUnlit(cmd, data.renderRegion);
					}
					if (!hasDebugView && data.display.showHashGridOverlay && data.debugMaterial != null)
					{
						data.renderer.ApplyDebugSettings();
						data.debugMaterial.SetInt(HashGridOverlayOnly, 1);
						ParticleFluidRenderBindings.ApplyMetaballDebugGlobals(cmd, data.display, data.camera, data.lighting, data.gradientAtlas);
						cmd.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), data.renderRegion.CreateRegionMatrix(), data.debugMaterial, 0, 0);
					}
					cmd.EndSample("Metaballs/Material Pipeline");
					data.renderer.RecordVectorField(data.display, cmd);
				}
				else
				{
					if (data.debugMaterial != null)
					{
						cmd.BeginSample("Metaballs/Debug Composite");
						ParticleFluidRenderBindings.ApplyMetaballDebugGlobals(cmd, data.display, data.camera, data.lighting, data.gradientAtlas);
						cmd.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), data.renderRegion.CreateRegionMatrix(), data.debugMaterial, 0, 0);
						cmd.EndSample("Metaballs/Debug Composite");
					}

					data.renderer.RecordVectorField(data.display, cmd);
				}
			});
		}

		public ParticleFluidLighting2D.FrameContext CreateLightingContext(ParticleDisplay2D display, Camera cam)
		{
			return new ParticleFluidLighting2D.FrameContext(display, cam, _currentRenderRegion);
		}

		public void Release()
		{
			ComputeHelper.Release(combinedAccumulationTexture);
			ComputeHelper.Release(normalAccumulationTexture, velocityTexture);
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
			float materialScale = _lighting != null ? _lighting.materialMapTextureScale : 1f;
			float causticScale = _lighting != null ? _lighting.directLight.textureScale : 1f;
			_currentRenderRegion = cropRegion;
			_currentSourceSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(renderTextureScale, 0.0001f));
			_currentVelocitySize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, Mathf.Max(velocityTextureScale, 0.0001f));
			_currentMaterialSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, materialScale);
			_currentCausticSize = ParticleFluidRenderBounds2D.ScaleSize(baseResolution, causticScale);

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, _currentSourceSize);
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, _currentSourceSize);
			if (_lighting != null && _lighting.directLight.temporalCaustics.ShouldRenderVelocityTextures(_currentDisplay))
			{
				ComputeHelper.CreateRenderTexture(ref velocityTexture, _currentVelocitySize);
			}
			else
			{
				ComputeHelper.Release(velocityTexture);
			}
			if (ShouldUseMaterialPipeline(_currentDisplay))
			{
				MaterialRenderer.materialMaps.EnsureRenderTextures(_currentMaterialSize, "Particle2D");
			}
			else
			{
				MaterialRenderer.materialMaps.Release();
			}
			_lighting?.EnsureLightingResources(_currentRenderRegion, _currentCausticSize);
		}

		private void ApplyDebugSettings()
		{
			int debugShaderMode;
			if (_currentDisplay.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				debugShaderMode = (int)_currentDisplay.debugMode;
			}
			else
			{
				if (_lighting == null)
				{
					debugShaderMode = 0;
				}
				else
				{
					debugShaderMode = _lighting.debugMode switch
					{
						ParticleFluidLighting2D.LightingDebugVisualization.Caustics => DebugModeCaustics,
						ParticleFluidLighting2D.LightingDebugVisualization.SoftLight => DebugModeSoftLight,
						ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw => DebugModeRadianceCascadeRaw,
						_ => 0,
					};
				}
			}

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
			_debugMaterial.SetFloat(HashGridCellSize, _currentDisplay.sim.EffectiveSmoothingRadius);
			_debugMaterial.SetVector(HashGridWorldCenter, _currentRenderRegion.center);
			_debugMaterial.SetVector(HashGridWorldSize, _currentRenderRegion.size);
			_debugMaterial.SetInt(ShowHashGridOverlay, _currentDisplay.showHashGridOverlay ? 1 : 0);
			_debugMaterial.SetInt(HashGridOverlayOnly, 0);
		}

		public float EffectiveVelocityBlurRadius => _lighting != null ? _currentDisplay.GetEffectiveMotionBlurRadius(_currentCamera, _lighting.directLight.temporalCaustics.motionBlurRadius) : 0f;

		private void RecordVectorField(ParticleDisplay2D display, RasterCommandBuffer targetCommandBuffer)
		{
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.vectorField.AppendDraw(display, targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		public bool ShouldUseMaterialPipeline(ParticleDisplay2D display)
		{
			return _lighting != null && display != null && display.debugMode <= ParticleDisplay2D.DebugVisualization.Temperature;
		}

		private class MetaballAccumulationPassData
		{
			public int shaderPass;
			public Bounds renderRegion;
			public Mesh particleMesh;
			public ComputeBuffer argsBuffer;
			public Material material;
		}

		private class CompositePassData
		{
			public bool useMaterialPipeline;
			public MetaballRenderer2D renderer;
			public ParticleDisplay2D display;
			public Camera camera;
			public ParticleFluidLighting2D lighting;
			public Material debugMaterial;
			public Bounds renderRegion;
			public MetaballMaterialRenderer2D materialRenderer;
			public TextureHandle gradientAtlas;
		}
	}
}
