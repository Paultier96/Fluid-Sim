using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
#endif

	namespace Seb.Fluid2D.Rendering
	{
		[DisallowMultipleComponent]
		[RequireComponent(typeof(ParticleFluidGaussianSss))]
		[RequireComponent(typeof(ParticleFluidRadianceCascadeGi))]
		[RequireComponent(typeof(ParticleFluidDirectLight))]
	public class ParticleFluidLighting2D : MonoBehaviour
	{
		[Serializable]
		public class PhaseMaterialSettings
		{
			[Min(1.0001f)] public float indexOfRefraction = 1.333f;
			[Min(0f)] public float absorption = 0f;
			[Tooltip("Blends ray absorption colour from the phase albedo gradient toward Diffuse Light Tint. 0 uses the current albedo-based absorption; 1 uses Diffuse Light Tint.")]
			[Range(0f, 1f)] public float absorptionDiffuseTintBlend = 0f;
			[Tooltip("Blends volumetric radiance-cascade absorption colour from the phase albedo gradient toward Diffuse Light Tint. This does not affect direct caustic absorption.")]
			[Range(0f, 1f)] public float radianceCascadeAbsorptionDiffuseTintBlend = 0f;
			[Range(0f, 1f)] public float reflectance = 0f;
			[Range(0.02f, 1f)] public float roughness = 0.35f;
			[Range(0f, 1f)] public float metallic = 0f;
			[Range(0f, 1f)] public float screenSpaceReflectionStrength = 0f;
			[ColorUsage(false, true)] public Color diffuseLightTint = Color.white;
			[Tooltip("Blends sharp caustic diffuse/transmission lighting from physical albedo-filtered light toward additive/emissive light.")]
			[Range(0f, 1f)] public float causticAdditiveBlend = 0f;
			[Tooltip("Blends soft diffuse lighting from physical albedo-filtered light toward additive/emissive light. Use higher values for a stronger SSS glow.")]
			[Range(0f, 1f)] public float diffuseAdditiveBlend = 0f;
			[Tooltip("How much soft diffuse lighting is modulated by the phase normal and primary light direction. 0 is fully ambient, 1 is Lambert diffuse.")]
			[Range(0f, 1f)] public float diffuseNormalInfluence = 0f;
			
			public PhaseMaterialSettings(float indexOfRefraction)
			{
				this.indexOfRefraction = indexOfRefraction;
			}
		}

		public enum TemporalMotionSource
		{
			Static,
			ParticleMotion,
			CausticMotion,
			ProjectedShadow
		}

		public enum LightingMode
		{
			Off,
			Shadows,
			Caustics
		}

		public enum LightingDebugVisualization
		{
			None,
			Caustics,
			SoftLightInit,
			SoftLight,
			RadianceCascadeRaw,
			CausticMotion,
			TemporalRejection,
			TemporalClamp,
			ProjectedShadow
		}

		[Header("Shaders")]
		public Shader lightingShader;

		[Header("Debug")]
		public LightingDebugVisualization debugMode = LightingDebugVisualization.None;

		[Range(0f, 1f)] public float ambientLight = 0.65f;

		[Header("Fresnel")]
		[ColorUsage(false, true)] public Color fresnelColor = new Color(0.75f, 0.9f, 1f, 1f);
		[Min(0f)] public float fresnelIntensity = 0.25f;
		[Min(0.1f)] public float fresnelPower = 3f;

		[Header("Screen-Space Refraction And Reflections")]
		[Min(0)] public float refractionStrength = 0.01f;
		[Min(0)] public float refractionEdgeFade = 0.05f;
		public bool screenSpaceRefractionCanCrossPhases = false;
		[Space]
		[Min(0f)] public float screenSpaceReflectionDistance = 24f;
		[Min(0.1f)] public float screenSpaceReflectionEdgePower = 1.5f;
		[Tooltip("Base virtual-depth scale in material-map pixels for sampling caustic light colour outside the blob for rasterized specular highlights.")]
		[Min(0f)] public float specularCausticSampleOffset = 6f;
		[Min(0f)] public float specularAntiAliasingStrength = 1f;

		[Header("Transmission")]
		[Min(0f)] public float transmissionIntensity = 0.2f;
		[Min(0.1f)] public float transmissionPower = 2f;

		[Header("Ambient Occlusion")]
		[Range(0f, 1f)] public float ambientOcclusion = 0.2f;
		[Min(0.1f)] public float ambientOcclusionPower = 2f;

		[Header("Iridescence")]
		[Min(0f)] public float iridescenceIntensity = 0f;
		[Min(0f)] public float iridescenceScale = 2.0f;

		[Header("MaterialMaps")]
		[Range(0.25f, 2f)] public float materialMapTextureScale = 1f;

		[Header("Phase Materials")]
		public ParticleFluidPhaseLookPreset phaseLookPreset;
		public bool applyPhaseLookPresetOnEnable = true;
		public bool applyPhaseLookPresetOnValidate = true;
		public PhaseMaterialSettings phase0Material = new (1.442f);
		public PhaseMaterialSettings phase1Material = new (1.333f);
		public PhaseMaterialSettings boundaryMaterial = new (1.516f);

		[Tooltip("How much albedo brightness affects ray absorption when using albedo-based absorption. 0 mostly uses hue only; 1 uses the brightened albedo value directly.")]
		[Range(0f, 1f)] public float absorptionAlbedoBrightnessInfluence = 0.7f;
		[Tooltip("How saturated albedo-based ray absorption is allowed to be. Lower values reduce pure RGB caustic tinting while keeping brightness control separate.")]
		[Range(0f, 1f)] public float absorptionAlbedoSaturationInfluence = 0.7f;

		private const int LightingPass = 0;

		internal Material lightingMaterial;
		internal const int MaxCausticTraceThreads = 65535;
		internal const int CausticTraceThreadGroupSize = 64;
		private const int MaterialSlotCount = 3;
		public const int LitPhaseCount = 2;
		private readonly PhaseMaterialSettings[] _materialSlots = new PhaseMaterialSettings[MaterialSlotCount];
		private readonly Vector4[] _phaseDiffuseLightTints = new Vector4[LitPhaseCount];
		private readonly Vector4[] _phaseSurfaceData = new Vector4[LitPhaseCount];
		private readonly Vector4[] _phaseSoftLightData = new Vector4[LitPhaseCount];

		[SerializeField] private ParticleDisplay2D display;
		[SerializeField] internal ParticleFluidLightManager lightManager;
		[SerializeField] internal ParticleFluidGaussianSss gaussianSss;
		[SerializeField] internal ParticleFluidRadianceCascadeGi radianceCascadeGi;
		[SerializeField] internal ParticleFluidDirectLight directLight;
		internal Texture materialAlbedoTexture;
		internal Texture materialNormalTexture;
		internal Texture materialTransportTexture;
		private Bounds _domainRenderRegion;
		internal float currentZoomScale = 1f;
		
		internal Vector2 currentCausticWorldCenter;
		internal Vector2 currentCausticWorldSize;
		internal Texture currentSoftLightPhase0Texture;
		internal Texture currentSoftLightPhase1Texture;
		internal Texture currentGaussianInitTexture;
		internal Texture currentGaussianBlurTexture;

		void Awake()
		{
			SyncMaterialSlots();
			ResolveReferences();
			ResetSoftLightDebugOutputs();
		}
		
		void OnEnable()
		{
			SyncMaterialSlots();
			ResolveReferences();
			ParticleFluidPhaseLookPreset.Changed += OnPhaseLookPresetChanged;
			if (applyPhaseLookPresetOnEnable)
			{
				ApplyPhaseLookPreset();
			}
		}

		void OnDisable()
		{
			ParticleFluidPhaseLookPreset.Changed -= OnPhaseLookPresetChanged;
		}

		void OnValidate()
		{
			SyncMaterialSlots();
			ResolveReferences();
			if (applyPhaseLookPresetOnValidate)
			{
				ApplyPhaseLookPreset();
			}
		}

		internal PhaseMaterialSettings[] PhaseMaterials
		{
			get
			{
				SyncMaterialSlots();
				return _materialSlots;
			}
		}

		void SyncMaterialSlots()
		{
			phase0Material ??= new PhaseMaterialSettings(1.442f);
			phase1Material ??= new PhaseMaterialSettings(1.333f);
			boundaryMaterial ??= new PhaseMaterialSettings(1.516f);
			_materialSlots[0] = phase0Material;
			_materialSlots[1] = phase1Material;
			_materialSlots[2] = boundaryMaterial;
		}
		
		public void ApplyPhaseLookPreset()
		{
			if (phaseLookPreset == null)
			{
				return;
			}

			CopyPhaseMaterialSettings(phaseLookPreset.phase0Material, phase0Material);
			CopyPhaseMaterialSettings(phaseLookPreset.phase1Material, phase1Material);
			CopyPhaseMaterialSettings(phaseLookPreset.boundaryMaterial, boundaryMaterial);
			gaussianSss?.ApplyPhaseLookPreset(phaseLookPreset);
			SyncMaterialSlots();

			ParticleDisplay2D particleDisplay = Display;
			if (particleDisplay != null)
			{
				particleDisplay.SetPhaseColourMaps(phaseLookPreset.phase0ColourMap, phaseLookPreset.phase1ColourMap);
			}
		}

		void OnPhaseLookPresetChanged(ParticleFluidPhaseLookPreset changedPreset)
		{
			if (!applyPhaseLookPresetOnValidate || changedPreset != phaseLookPreset)
			{
				return;
			}

			ApplyPhaseLookPreset();
		}

		static void CopyPhaseMaterialSettings(PhaseMaterialSettings source, PhaseMaterialSettings destination)
		{
			if (source == null || destination == null)
			{
				return;
			}

			destination.indexOfRefraction = source.indexOfRefraction;
			destination.absorption = source.absorption;
			destination.absorptionDiffuseTintBlend = source.absorptionDiffuseTintBlend;
			destination.radianceCascadeAbsorptionDiffuseTintBlend = source.radianceCascadeAbsorptionDiffuseTintBlend;
			destination.reflectance = source.reflectance;
			destination.roughness = source.roughness;
			destination.metallic = source.metallic;
			destination.screenSpaceReflectionStrength = source.screenSpaceReflectionStrength;
			destination.diffuseLightTint = source.diffuseLightTint;
			destination.causticAdditiveBlend = source.causticAdditiveBlend;
			destination.diffuseAdditiveBlend = source.diffuseAdditiveBlend;
			destination.diffuseNormalInfluence = source.diffuseNormalInfluence;
		}

		public void EnsureMaterials(Shader shader)
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref lightingMaterial, shader);
			gaussianSss.EnsureMaterials();
			radianceCascadeGi.EnsureMaterials();
			directLight.EnsureMaterials();
		}

		internal void ResetSoftLightDebugOutputs()
		{
			currentSoftLightPhase0Texture = Texture2D.blackTexture;
			currentSoftLightPhase1Texture = Texture2D.blackTexture;
			currentGaussianInitTexture = Texture2D.blackTexture;
			currentGaussianBlurTexture = Texture2D.blackTexture;
			ParticleFluidGaussianSss tempQualifier = gaussianSss;
			if (tempQualifier != null)
			{
				tempQualifier.currentInitTexture = Texture2D.blackTexture;
			}

			radianceCascadeGi?.ResetDebugOutputs();
		}


		public void BindMaterialTextures()
		{
			Texture albedoTexture = materialAlbedoTexture != null ? materialAlbedoTexture : Texture2D.blackTexture;
			Texture normalTexture = materialNormalTexture != null ? materialNormalTexture : Texture2D.blackTexture;
			Texture transportTexture = materialTransportTexture != null ? materialTransportTexture : Texture2D.blackTexture;
			lightingMaterial.SetTexture("MaterialAlbedoTex", albedoTexture);
			lightingMaterial.SetTexture("MaterialNormalTex", normalTexture);
			lightingMaterial.SetTexture("MaterialTransportTex", transportTexture);
			lightingMaterial.SetVector("MaterialAlbedoTex_TexelSize", new Vector4(1f / albedoTexture.width, 1f / albedoTexture.height, albedoTexture.width, albedoTexture.height));
		}

		internal void ApplyLightingInputs(ParticleFluidLightingInputSet inputs)
		{
			materialAlbedoTexture = inputs.albedoTexture;
			materialNormalTexture = inputs.normalTexture;
			materialTransportTexture = inputs.transportTexture;
			if (lightingMaterial != null)
			{
				BindMaterialTextures();
			}
		}


		public readonly struct FrameContext
		{
			public readonly ParticleDisplay2D display;
			public readonly Camera cam;
			public readonly Bounds renderRegion;
			public readonly Vector2Int sourceSize;

			public FrameContext(ParticleDisplay2D display, Camera cam, Bounds renderRegion, Vector2Int sourceSize)
			{
				this.display = display;
				this.cam = cam;
				this.renderRegion = renderRegion;
				this.sourceSize = sourceSize;
			}
		}
		
		internal FrameContext PrepareLighting(Camera cam, ParticleFluidLightingInputSet inputs)
		{
			ApplyLightingInputs(inputs);
			FrameContext context = new FrameContext(display, cam, inputs.renderRegion, inputs.sourceSize);
			directLight.ApplyTemporalSettings(context, inputs.velocityPhase0Texture, inputs.velocityPhase1Texture);
			return context;
		}

		public void ApplySettings(FrameContext context, bool renderCaustics, bool renderSoftLight, Texture causticTexture)
		{
			ParticleDisplay2D display = context.display;
			PhaseMaterialSettings[] materials = PhaseMaterials;

			_domainRenderRegion = context.renderRegion;
			currentZoomScale = context.display.GetZoomScale(context.cam);
			
			BindMaterialTextures();
			lightingMaterial.SetInt("particleFluidCausticsEnabled", renderCaustics ? 1 : 0);
			lightingMaterial.SetTexture("CausticTex", causticTexture);
			lightingMaterial.SetInt("particleFluidPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			lightingMaterial.SetInt("particleGaussianPhase0Only", gaussianSss != null && gaussianSss.gaussianDiffuseEnabled ? 1 : 0);
			lightingMaterial.SetFloat("particleFluidRadianceCascadeDirectCausticStrength", radianceCascadeGi != null ? radianceCascadeGi.radianceCascadeDirectCausticStrength : 0f);
			lightingMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			lightingMaterial.SetTexture("SoftLightTexPhase1", Texture2D.blackTexture);
			ResetSoftLightDebugOutputs();
			for (int phaseIndex = 0; phaseIndex < LitPhaseCount; phaseIndex++)
			{
				PhaseMaterialSettings material = materials[phaseIndex];
				_phaseDiffuseLightTints[phaseIndex] = material.diffuseLightTint;
				_phaseSurfaceData[phaseIndex] = new Vector4(material.reflectance, material.roughness, material.metallic, material.screenSpaceReflectionStrength);
				_phaseSoftLightData[phaseIndex] = new Vector4(material.causticAdditiveBlend, material.diffuseAdditiveBlend, material.diffuseNormalInfluence, 0f);
			}
			lightingMaterial.SetVectorArray("particleFluidPhaseDiffuseLightTint", _phaseDiffuseLightTints);
			lightingMaterial.SetVectorArray("particlePhaseSurface", _phaseSurfaceData);
			lightingMaterial.SetVectorArray("particlePhaseSoftLight", _phaseSoftLightData);
			lightingMaterial.SetFloat("particleFluidIridescenceIntensity", iridescenceIntensity);
			lightingMaterial.SetFloat("particleFluidIridescenceScale", iridescenceScale);
			lightingMaterial.SetFloat("particleAmbientLight", ambientLight);
			lightManager.ApplyLightingMaterialParams(lightingMaterial, display.sim.analyticBoundary, PhaseMaterials[1].indexOfRefraction);
			lightingMaterial.SetColor("particleFresnelColor", fresnelColor);
			lightingMaterial.SetFloat("particleFresnelIntensity", fresnelIntensity);
			lightingMaterial.SetFloat("particleFresnelPower", fresnelPower);
			lightingMaterial.SetFloat("screenSpaceReflectionDistance", screenSpaceReflectionDistance * currentZoomScale);
			lightingMaterial.SetFloat("screenSpaceReflectionEdgePower", screenSpaceReflectionEdgePower);
			lightingMaterial.SetFloat("particleSpecularCausticSampleOffset", specularCausticSampleOffset * currentZoomScale);
			lightingMaterial.SetFloat("particleSpecularAntiAliasingStrength", specularAntiAliasingStrength);
			float phaseBoundary = Mathf.Clamp01(0.5f + Mathf.Clamp(display.metaballs.phase0RenderBias, -1f, 1f) * 0.5f);
			float phase0Scale = Mathf.Sqrt(Mathf.Max(phaseBoundary * 2f, 0.0001f));
			float phase1Scale = Mathf.Sqrt(Mathf.Max((1f - phaseBoundary) * 2f, 0.0001f));
			Vector4 phaseRadiusScale = new Vector4(phase0Scale, phase1Scale, 0f, 0f);
			lightingMaterial.SetVector("particlePhaseScale", phaseRadiusScale);
			lightingMaterial.SetFloat("particleTransmissionIntensity", transmissionIntensity);
			lightingMaterial.SetFloat("particleTransmissionPower", transmissionPower);
			lightingMaterial.SetFloat("particleAmbientOcclusion", ambientOcclusion);
			lightingMaterial.SetFloat("particleAmbientOcclusionPower", ambientOcclusionPower);
			directLight.ApplyInactiveProjectedShadow(lightingMaterial);
		}

		internal void Render(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam)
		{
			BindMaterialTextures();
			commandBuffer.BeginSample("Particle Fluid/Final Lighting");
			ParticleDisplay2D display = Display;
			ParticleFluidPassBindings.ApplyFinalLightingGlobals(commandBuffer, display, _domainRenderRegion);
			commandBuffer.SetRenderTarget(finalTarget);
			commandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _domainRenderRegion.CreateRegionMatrix(), lightingMaterial, 0, LightingPass);
			commandBuffer.EndSample("Particle Fluid/Final Lighting");
		}

		internal void RecordProjectedShadowAndApply(CommandBuffer commandBuffer, FrameContext context, Texture transportTexture)
		{
			if (lightingMaterial == null || commandBuffer == null)
			{
				return;
			}

			Vector2 projectedShadowDirection = Vector2.zero;
			ParticleFluidProjectedShadow.RecordParams projectedShadowParams = default;
			bool shouldRender = directLight.projectedShadow.ShouldRender(directLight);
			if (shouldRender)
			{
				ParticleFluidDirectLight.ProjectedShadowSettings projectedShadowSettings = directLight.projectedShadowSettings;
				projectedShadowParams = new ParticleFluidProjectedShadow.RecordParams(
					directLight.projectedShadowCompute,
					directLight.projectedShadow.projectedShadowMapBuffer,
					directLight.projectedShadow.projectedShadowMapTexture,
					transportTexture,
					projectedShadowSettings.mapBins,
					lightManager.GetMainDirectionalLight().GetBoundaryRefractedDirection(PhaseMaterials[1].indexOfRefraction, context.display.sim.analyticBoundary),
					context.renderRegion,
					context.sourceSize);
			}

			bool useProjectedShadow = shouldRender && directLight.projectedShadow.RecordCurrentShadowMap(commandBuffer, projectedShadowParams, out projectedShadowDirection);
			directLight.projectedShadow.ApplyToMaterial(lightingMaterial, useProjectedShadow, projectedShadowDirection, directLight.projectedShadowSettings);
		}

		internal void RenderLit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam, FrameContext context, Texture transportTexture)
		{
			RecordProjectedShadowAndApply(commandBuffer, context, transportTexture);
			Render(commandBuffer, finalTarget, cam);
		}

		internal void RenderConfiguredLit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam, FrameContext context, Texture transportTexture, bool renderCaustics, bool renderSoftLight, Texture causticTexture, Texture sharpCausticsTexture = null)
		{
			ApplySettings(context, renderCaustics, renderSoftLight, causticTexture);
			if (renderSoftLight)
			{
				RecordSoftLight(context, commandBuffer, sharpCausticsTexture != null ? sharpCausticsTexture : Texture2D.blackTexture);
			}
			RenderLit(commandBuffer, finalTarget, cam, context, transportTexture);
		}

		public void EnsureLightingResources(Bounds renderRegion, Vector2Int causticSize)
		{
			currentCausticWorldCenter = renderRegion.center;
			currentCausticWorldSize = renderRegion.size;
			bool renderPhaseDiffuseLight = gaussianSss.ShouldRender();
			bool renderRadianceCascadeLight = radianceCascadeGi.radianceCascadeEnabled;

			gaussianSss.EnsureResources(causticSize, renderPhaseDiffuseLight);
			radianceCascadeGi.EnsureResources(causticSize, renderRadianceCascadeLight);

			directLight.EnsureResources(causticSize, renderRegion);
		}

		public void Release()
		{
			if (lightingMaterial != null)
			{
				DestroyImmediate(lightingMaterial);
				lightingMaterial = null;
			}

			directLight.Release();
			gaussianSss.Release();
			radianceCascadeGi.Release();
		}

		internal ParticleDisplay2D Display => ResolveDisplay();

		internal void RecordSoftLight(FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics)
		{
			bool renderGaussian = gaussianSss.ShouldRender();
			bool renderRadianceCascade = radianceCascadeGi.radianceCascadeEnabled;
			Texture phase0SoftLightTexture = Texture2D.blackTexture;
			Texture phase1GITexture = Texture2D.blackTexture;
			Texture gaussianBlurredTexture = Texture2D.blackTexture;
			ResetSoftLightDebugOutputs();

			if (renderGaussian)
			{
				phase0SoftLightTexture = gaussianSss.Render(context, targetCommandBuffer, sharpCaustics, materialTransportTexture, directLight.textureScale);
				gaussianBlurredTexture = phase0SoftLightTexture;
				if (renderRadianceCascade && gaussianSss.gaussianDiffuseEnabled && radianceCascadeGi.softLightPhase0Texture != null)
				{
					targetCommandBuffer.Blit(phase0SoftLightTexture, radianceCascadeGi.softLightPhase0Texture);
					phase0SoftLightTexture = radianceCascadeGi.softLightPhase0Texture;
				}
			}

			if (renderRadianceCascade)
			{
				phase1GITexture = radianceCascadeGi.Render(context, targetCommandBuffer, sharpCaustics, renderGaussian && gaussianSss.gaussianDiffuseEnabled);
			}

			currentGaussianInitTexture = gaussianSss != null ? gaussianSss.currentInitTexture : Texture2D.blackTexture;
			currentGaussianBlurTexture = gaussianBlurredTexture;
			currentSoftLightPhase0Texture = phase0SoftLightTexture;
			currentSoftLightPhase1Texture = phase1GITexture;
			lightingMaterial.SetTexture("SoftLightTex", phase0SoftLightTexture);
			lightingMaterial.SetTexture("SoftLightTexPhase1", phase1GITexture);
		}

		void ResolveReferences()
		{
			ResolveDisplay();
			lightManager ??= GetComponent<ParticleFluidLightManager>();
			gaussianSss ??= GetComponent<ParticleFluidGaussianSss>();
			radianceCascadeGi ??= GetComponent<ParticleFluidRadianceCascadeGi>();
			directLight ??= GetComponent<ParticleFluidDirectLight>();
			if (lightManager == null && transform.parent != null)
			{
				lightManager = transform.parent.GetComponentInChildren<ParticleFluidLightManager>(true);
			}
			lightManager ??= FindAnyObjectByType<ParticleFluidLightManager>();
		}

		ParticleDisplay2D ResolveDisplay()
		{
			if (display != null)
			{
				return display;
			}

			display = GetComponent<ParticleDisplay2D>();
			if (display != null)
			{
				return display;
			}

			if (transform.parent != null)
			{
				display = transform.parent.GetComponentInChildren<ParticleDisplay2D>(true);
			}

			display ??= FindAnyObjectByType<ParticleDisplay2D>();

			return display;
		}
	}
}
