using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	public partial class ParticleFluidLighting2D : MonoBehaviour
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
			[Min(0f)] public float diffuseScatterStrength = 0f;
			[Min(0f)] public float diffuseGaussianRadius = 24f;
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

		[Serializable]
		public class FluidLightSettings
		{
			public enum LightType
			{
				Directional,
				Point
			}

			[Serializable]
			public class DirectionalParams
			{
				public float azimuthDegrees = 122.5f;
				[Range(-89, 89f)] public float elevationDegrees = 50.3f;
				[Min(0f)] public float angularRadiusDegrees = 0f;
			}

			[Serializable]
			public class PointParams
			{
				public Vector3 position;
				public bool followsMouse = false;
				[Tooltip("World-space radius over which point-light brightness fades to zero.")]
				[Min(0.0001f)] public float range = 20f;
				[Tooltip("Point-light attenuation exponent")]
				[Min(0.1f)] public float falloff = 2f;
			}

			public bool enabled = true;
			public LightType type = LightType.Directional;
			[Tooltip("6500 -> neutral daylight; 2700 -> incandescent light")]
			[Range(1000f, 20000f)] public float temperatureKelvin = 6500f;
			[ColorUsage(false, true)] public Color color = Color.white;
			[Min(0f)] public float intensity = 0.45f;
			[Min(0f)] public float sampleBias = 1f;
			public DirectionalParams directional = new();
			public PointParams point = new();

			public Vector3 Direction
			{
				get
				{
					float azimuth = directional.azimuthDegrees * Mathf.Deg2Rad;
					float elevation = directional.elevationDegrees * Mathf.Deg2Rad;
					float planarLength = Mathf.Cos(elevation);
					return new Vector3(
						Mathf.Cos(azimuth) * planarLength,
						Mathf.Sin(azimuth) * planarLength,
						Mathf.Sin(elevation)
					).normalized;
				}
			}

			public Color EffectiveColor =>  Mathf.CorrelatedColorTemperatureToRGB(temperatureKelvin) * color;
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
			FullCaustics
		}

		public enum SoftLightMode
		{
			Off,
			GaussianDiffuse,
			RadianceCascade,
			Hybrid
		}

		public enum RadianceCascadeTraceMode
		{
			Volumetric = 2,
			PhaseBoundarySdf = 1
		}

		public enum RadianceCascadeSdfBoundarySource
		{
			AlbedoEmission,
			SkyLitAlbedo,
			CausticAlbedo,
			GaussianPhase0
		}

		public enum LightingDebugVisualization
		{
			None,
			Caustics,
			SoftLight,
			RadianceCascadeRaw,
			CausticMotion,
			TemporalRejection,
			TemporalClamp,
			ProjectedShadow
		}

		[Header("Shaders")]
		public Shader lightingShader;
		public Shader blurShader;
		public Shader temporalShader;
		public ComputeShader computeShader;
		public ComputeShader projectedShadowCompute;
		public ComputeShader phaseDiffuseLightCompute;
		public ComputeShader radianceCascadeSdfCompute;
		public Shader radianceCascadeShader;
		public Shader radianceCascadeSdfShader;

		[Header("Debug")]
		public LightingDebugVisualization debugMode = LightingDebugVisualization.None;

		[Header("Lights")]
		public FluidLightSettings[] lights;
		[Tooltip("Unlit colour multiplier. Increase if shadowed particles are too dark.")]
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
		[Tooltip("Widens sharp specular lobes based on screen-space normal derivatives to reduce highlight aliasing. 0 disables it.")]
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

		[Header("Lighting")]
		public LightingMode lightingMode = LightingMode.FullCaustics;
		[Header("Raymarched Lighting - Setup")]
		[Range(0.125f, 1f)] public float textureScale = 0.5f;
		[Tooltip("Resolution scale for the material albedo/normal maps used by the shaded composite. This is independent from the metaball accumulation scale.")]
		[Range(0.25f, 2f)] public float materialMapTextureScale = 1f;

		[Header("Phase Materials")]
		public ParticleFluidPhaseLookPreset phaseLookPreset;
		public bool applyPhaseLookPresetOnEnable = true;
		public bool applyPhaseLookPresetOnValidate = true;
		public PhaseMaterialSettings phase0Material = new PhaseMaterialSettings(1.442f);
		public PhaseMaterialSettings phase1Material = new PhaseMaterialSettings(1.333f);
		public PhaseMaterialSettings boundaryMaterial = new PhaseMaterialSettings(1.516f);

		[Header("Raymarched Lighting - Refraction")]
		public bool stochasticReflection = true;
		[Min(0f)] public float dispersionStrength = 0f;
		[Tooltip("Randomly rotates the stratified spectral band assignment per ray and frame. 0 keeps fixed bands; 1 fully randomizes the band rotation to reduce stripes.")]
		[Range(0f, 1f)] public float dispersionRotation = 1f;
		[Tooltip("How much albedo brightness affects ray absorption when using albedo-based absorption. 0 mostly uses hue only; 1 uses the brightened albedo value directly.")]
		[Range(0f, 1f)] public float absorptionAlbedoBrightnessInfluence = 1f;
		[Tooltip("How saturated albedo-based ray absorption is allowed to be. Lower values reduce pure RGB caustic tinting while keeping brightness control separate.")]
		[Range(0f, 1f)] public float absorptionAlbedoSaturationInfluence = 1f;
		[FormerlySerializedAs("raySteps")]
		[Header("Raymarched Lighting - Rays")]
		[Tooltip("Number of raymarch steps used per lighting ray. Higher values find exits more reliably but cost more.")]
		[Range(8, 192)] public int extraRayTravelSteps = 64;
		[Min(0f)] public float rayBrightness = 1f; //delete?
		[Min(1)] public int rayStride = 1;
		[Range(1, 128)] public int raysPerPixel = 1;
		[Tooltip("Samples the medium colour every N ray steps while absorption is active.")]
		[Min(1)] public int colourSampleStride = 8;
		[Min(0f)] public float blur = 1.5f;

		[Header("Soft Subsurface Lighting")]
		public SoftLightMode softLightMode = SoftLightMode.Off;
		[Range(0.25f, 1f)] public float phaseDiffuseLightTextureScale = 0.5f;
		[Tooltip("How strongly phase boundaries block phase-aware diffuse lighting. Higher values keep light inside each phase.")]
		[Min(0f)] public float phaseDiffuseLightBoundarySharpness = 5f; //delete?
		[Tooltip("Number of cascade levels. Higher values spread soft light farther but cost one fullscreen pass per level.")]
		[Range(1, 6)] public int radianceCascadeCount = 4;
		public RadianceCascadeTraceMode radianceCascadeTraceMode = RadianceCascadeTraceMode.Volumetric;
		[Tooltip("Maximum ray range in normalized lighting-texture UV space at the reference zoom.")]
		[Min(0.0001f)] public float radianceCascadeRayRange = 1.25f;
		[Tooltip("Raymarch samples per cascade ray segment.")]
		[Range(1, 64)] public int radianceCascadeRaySteps = 16;
		[Tooltip("Multiplier applied to the radiance cascade soft light before compositing.")]
		[Min(0f)] public float radianceCascadeIntensity = 1f;
		[Tooltip("Scales how strongly emissive phase-0 blobs inject light into the radiance cascade transport.")]
		[Min(0f)] public float radianceCascadeBlobEmissionStrength = 1f;
		[Tooltip("Adds the primary directional light as an external radiance source, similar to a sky light, when cascade rays escape the fluid.")]
		public bool radianceCascadeDirectionalLightEnabled = true;
		[Tooltip("Scales the primary directional light contribution in the radiance cascades relative to blob emission.")]
		[Min(0f)] public float radianceCascadeDirectionalLightStrength = 1f;
		[Tooltip("Normalized cascade level where directional light starts contributing. 0 lets it affect all cascades; 1 restricts it to the highest cascade only.")]
		[Range(0f, 1f)] public float radianceCascadeDirectionalLightCascadeStart = 1f;
		[Tooltip("How far the phase-0 source region is inset inward in phase-boundary SDF mode, in soft-light texture pixels. Higher values shrink the effective phase-0 source region and also widen the directional-light visibility shell.")]
		[Min(0f)] public float radianceCascadeSdfPhase0InsetPixels = 1.5f;
		[Tooltip("How phase-boundary SDF hits create radiance: direct albedo emission, directional sky-lit albedo, caustic texture tinted by albedo, or the preserved gaussian phase-0 soft light.")]
		public RadianceCascadeSdfBoundarySource radianceCascadeSdfBoundarySource = RadianceCascadeSdfBoundarySource.AlbedoEmission;
		[Tooltip("Applies a cheap absorption approximation in phase-boundary SDF mode based on signed-distance depth inside phase 0. Useful for comparing SDF RC against volumetric RC without full marching cost.")]
		public bool radianceCascadeSdfApproximateAbsorption = false;
		[Tooltip("How much radiance-cascade lighting is visible on phase 0 in the final composite.")]
		[Min(0f)] public float radianceCascadePhase0Visibility = 0f;
		[Tooltip("How much radiance-cascade lighting is visible on phase 1 in the final composite.")]
		[Min(0f)] public float radianceCascadePhase1Visibility = 1f;
		[Tooltip("Amount of sharp direct caustic lighting kept while a soft SSS pass is enabled. 0 replaces sharp caustics with soft SSS, 1 keeps the previous sharp+soft result.")]
		[Range(0f, 1f)] public float radianceCascadeDirectCausticStrength = 1f;

		[Header("Raymarched Lighting - Temporal Denoising")]
		public bool denoisingEnabled = true;
		[Range(0f, 0.99f)] public float temporalHistoryWeight = 0.95f;
		[Tooltip("Clamps reprojected history into the current 3x3 neighbourhood before blending. Reduces stale bright/dark trails in newly revealed areas.")]
		[Range(0f, 1f)] public float temporalHistoryClampStrength = 0.6f;
		[Tooltip("How aggressively clamped history is rejected. Higher values reduce disocclusion trails more but keep less accumulated history around sharp caustics.")]
		[Min(0f)] public float temporalClampRejection = 0.5f;
		[Tooltip("Reduces disocclusion fizzle without blurring stable temporally accumulated regions.")]
		[Range(0f, 1f)] public float temporalRejectedSpatialFilter = 1f;
		public TemporalMotionSource temporalMotionSource = TemporalMotionSource.Static;
		[Tooltip("Radius in pixels at resolution factor 1 of the phase-separated velocity blur used by particle and caustic motion. Higher values smooth unstable boundary motion without mixing phase velocities.")]
		[Min(0)] public float motionBlurRadius = 6;
		[Tooltip("Ignores smoothed phase velocity below this world-space speed before using it for temporal reprojection or caustic motion. Helps tiny residual velocities stop adding shimmer.")]
		[Min(0f)] public float motionVelocityThreshold = 0f;
		[Tooltip("Full-resolution pixel blur radius applied to the caustic motion texture before dilation and temporal reprojection. Set to 0 to keep raw per-ray motion.")]
		[Min(0f)] public float temporalMotionBlur = 1.5f;
		[Tooltip("Spreads caustic motion vectors into nearby low-confidence pixels before temporal reprojection. Helps newly uncovered light regions move with nearby blobs instead of leaving trails.")]
		[Min(0f)] public float temporalMotionDilationRadius = 12f;
		[Tooltip("Number of dilation passes used to propagate caustic motion outward. Higher values cover larger newly revealed regions but cost one fullscreen blit per pass.")]
		[Range(1, 8)] public int temporalMotionDilationIterations = 3;
		[Tooltip("Frame-to-frame 2D jitter in raymarched lighting texture pixels. One component shifts ray positions, the other shifts the ray marching sample phase.")]
		[Range(0f, 1f)] public float temporalJitterPixels = 0f;
		[Tooltip("Subpixel normal resampling radius used for reflected and refracted lighting rays at phase boundaries. Helps multiple rays per pixel see different boundary normals.")]
		[Min(0f)] public float surfaceNormalJitterPixels = 0f;
		[Tooltip("Manual world-space offset applied along the projected shadow direction before using the 1D reactive shadow map for temporal rejection.")]
		public float projectedShadowOffset = 0f;
		[Min(0f)] public float projectedShadowExpansion = 0f;
		public bool projectedShadowHistoryRejection = true;

		const int LightingPass = 0;

		internal Material lightingMaterial;
		internal Material radianceCascadeMaterial;
		internal Material radianceCascadeSdfMaterial;
		internal Material causticBlurMaterial;
		internal Material temporalMaterial;
		internal Material causticMotionBlurMaterial;
		internal const int MaxCausticTraceThreads = 65535;
		internal const int CausticTraceThreadGroupSize = 64;
		public int projectedShadowMapBins = 2048;
		const float TwoPi = 2 * Mathf.PI;
		const int PointLightBoundaryEllipseSamples = 128;
		const int PointLightBoundaryCutSamples = 31;
		const int LightSlotCount = 3;
		const int MaterialSlotCount = 3;
		const int LitPhaseCount = 2;
		readonly float[] pointLightBoundaryAngles = new float[PointLightBoundaryEllipseSamples + PointLightBoundaryCutSamples + 2];
		readonly PhaseMaterialSettings[] materialSlots = new PhaseMaterialSettings[MaterialSlotCount];
		readonly Vector4[] phaseDiffuseLightTints = new Vector4[LitPhaseCount];
		readonly Vector4[] phaseSurfaceData = new Vector4[LitPhaseCount];
		readonly Vector4[] phaseSoftLightData = new Vector4[LitPhaseCount];

		internal ParticleFluidCausticsTrace traceCaustics;
		internal ParticleFluidCausticsTemporal temporalCaustics;
		internal ParticleFluidDiffuseLight softLightCaustics;
		internal ParticleFluidProjectedShadow projectedShadow;
		internal Texture materialAlbedoTexture;
		internal Texture materialNormalTexture;
		internal RenderTexture combinedSourceTexture;
		internal ParticleFluidRenderRegion2D materialRenderRegion;
		ParticleFluidRenderRegion2D causticRenderRegion;
		internal float currentZoomScale = 1f;
		
		internal ComputeBuffer causticAccumulationBuffer;
		internal ComputeBuffer causticMotionAccumulationBuffer;
		internal ComputeBuffer causticLightParamsBuffer;
		internal ComputeBuffer causticMaterialParamsBuffer;
		internal RenderTexture causticResolvedTexture;
		internal RenderTexture causticBlurTexture;
		internal RenderTexture causticMotionTexture;
		internal RenderTexture causticMotionDilatedTexture;
		internal RenderTexture causticMotionDilationScratchTexture;
		internal RenderTexture softLightTexture0;
		internal RenderTexture softLightTexture1;
		internal RenderTexture softLightPhase0Texture;
		internal RenderTexture radianceCascadeSdfSeedA;
		internal RenderTexture radianceCascadeSdfSeedB;
		internal RenderTexture radianceCascadeSdfPayloadA;
		internal RenderTexture radianceCascadeSdfPayloadB;
		internal RenderTexture radianceCascadeSdfNormalA;
		internal RenderTexture radianceCascadeSdfNormalB;
		internal Texture currentSoftLightPhase0Texture;
		internal Texture currentSoftLightPhase1Texture;
		internal RenderTexture causticHistoryTexture;
		internal RenderTexture causticTemporalTexture;
		internal ComputeBuffer projectedShadowMapBuffer;
		internal RenderTexture projectedShadowMapTexture;
		internal RenderTexture projectedShadowMapHistoryTexture;
		
		internal bool clearCausticHistory;
		internal bool hasPreviousCausticCamera;
		internal int causticFrameIndex;
		internal int causticTemporalFrameCount;
		internal Vector2 previousCausticWorldCenter;
		internal Vector2 previousCausticWorldSize;
		internal Vector2 previousProjectedShadowDirection;
		internal ParticleFluidRenderRegion2D currentCausticRenderRegion;
		
		public bool IsReady => lightingMaterial != null;
		
		void Awake()
		{
			EnsureLightsArray();
			SyncMaterialSlots();
			traceCaustics = new ParticleFluidCausticsTrace(this);
			temporalCaustics = new ParticleFluidCausticsTemporal(this);
			softLightCaustics = new ParticleFluidDiffuseLight(this);
			projectedShadow = new ParticleFluidProjectedShadow();
		}
		
		void OnEnable()
		{
			EnsureLightsArray();
			SyncMaterialSlots();
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
			EnsureLightsArray();
			SyncMaterialSlots();
			if (applyPhaseLookPresetOnValidate)
			{
				ApplyPhaseLookPreset();
			}
		}

		void EnsureLightsArray()
		{
			if (lights.Length != LightSlotCount)
			{
				FluidLightSettings[] resizedLights = new FluidLightSettings[LightSlotCount];
				if (lights != null)
				{
					Array.Copy(lights, resizedLights, Mathf.Min(lights.Length, LightSlotCount));
				}
				lights = resizedLights;
			}
		}

		internal PhaseMaterialSettings[] PhaseMaterials
		{
			get
			{
				SyncMaterialSlots();
				return materialSlots;
			}
		}

		void SyncMaterialSlots()
		{
			phase0Material ??= new PhaseMaterialSettings(1.442f);
			phase1Material ??= new PhaseMaterialSettings(1.333f);
			boundaryMaterial ??= new PhaseMaterialSettings(1.516f);
			materialSlots[0] = phase0Material;
			materialSlots[1] = phase1Material;
			materialSlots[2] = boundaryMaterial;
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
			SyncMaterialSlots();

			ParticleDisplay2D display = GetComponent<ParticleDisplay2D>();
			if (display != null)
			{
				display.SetPhaseColourMaps(phaseLookPreset.phase0ColourMap, phaseLookPreset.phase1ColourMap);
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
			destination.diffuseScatterStrength = source.diffuseScatterStrength;
			destination.diffuseGaussianRadius = source.diffuseGaussianRadius;
			destination.diffuseLightTint = source.diffuseLightTint;
			destination.causticAdditiveBlend = source.causticAdditiveBlend;
			destination.diffuseAdditiveBlend = source.diffuseAdditiveBlend;
			destination.diffuseNormalInfluence = source.diffuseNormalInfluence;
		}

		public void EnsureMaterials(Shader shader)
		{
#if UNITY_EDITOR
			EnsureEditorComputeReferences();
#endif
			Shader temporalShader = this.temporalShader != null ? this.temporalShader : Shader.Find("Hidden/Particle2DMetaballTemporal");
			EnsureMaterial(ref lightingMaterial, shader);
			EnsureMaterial(ref radianceCascadeMaterial, radianceCascadeShader);
			EnsureMaterial(ref radianceCascadeSdfMaterial, radianceCascadeSdfShader != null ? radianceCascadeSdfShader : Shader.Find("Hidden/Particle2DMetaballRadianceCascadesSdf"));
			EnsureMaterial(ref temporalMaterial, temporalShader);
			EnsureMaterial(ref causticBlurMaterial, blurShader);
			EnsureMaterial(ref causticMotionBlurMaterial, blurShader);
		}

#if UNITY_EDITOR
		void EnsureEditorComputeReferences()
		{
			if (projectedShadowCompute == null)
			{
				projectedShadowCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Scripts/Sim2D/Rendering/Lighting/ProjectedShadow/ParticleFluidProjectedShadow.compute");
			}
		}
#endif


		static void EnsureMaterial(ref Material material, Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			if (material != null)
			{
				DestroyImmediate(material);
			}

			material = new Material(shader);
		}

		public void SetMaterialTextures(Texture albedo, Texture normal, ParticleFluidRenderRegion2D renderRegion)
		{
			materialAlbedoTexture = albedo;
			materialNormalTexture = normal;
			materialRenderRegion = renderRegion;
			BindMaterialTextures();
		}

		public void BindMaterialTextures()
		{
			ParticleDisplay2D display = GetComponent<ParticleDisplay2D>();
			Texture albedoTexture = materialAlbedoTexture != null ? materialAlbedoTexture : Texture2D.blackTexture;
			Texture normalTexture = materialNormalTexture != null ? materialNormalTexture : Texture2D.blackTexture;
			lightingMaterial.SetTexture("MaterialAlbedoTex", albedoTexture);
			lightingMaterial.SetTexture("MaterialNormalTex", normalTexture);
			lightingMaterial.SetTexture("CombinedTex", combinedSourceTexture != null ? combinedSourceTexture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("ColourMap", display != null && display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("ColourMap2", display != null && display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", materialRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetVector("particleFluidCompositeUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetVector("particleFluidCameraUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
			int width = Mathf.Max(albedoTexture.width, 1);
			int height = Mathf.Max(albedoTexture.height, 1);
			lightingMaterial.SetVector("MaterialAlbedoTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
		}
		
		public Vector3 GetDirectLightingDirection(ParticleDisplay2D display, Vector3 lightDirection)
		{
			PhaseMaterialSettings[] materials = PhaseMaterials;
			if (!display.sim.useEllipticalBounds)
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
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(materials[1].indexOfRefraction, 1.0001f));
			Vector2 refractedLightXY = -refractedRayDirection * planarLength;
			return new Vector3(refractedLightXY.x, refractedLightXY.y, lightDirection.z).normalized;
		}
		

		bool TryGetAnalyticBoundaryHitFromCenter(ParticleDisplay2D display, Vector2 directionToLight, out Vector2 hitPoint, out Vector2 outwardNormal)
		{
			float expansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);
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

		public readonly struct FrameContext
		{
			public readonly ParticleDisplay2D display;
			public readonly Camera cam;
			public readonly ParticleFluidRenderLayout2D renderLayout;
			public readonly float zoomScale;
			public readonly float analyticBoundaryExpansion;

			public FrameContext(
				ParticleDisplay2D display,
				Camera cam,
				ParticleFluidRenderLayout2D renderLayout,
				float zoomScale,
				float analyticBoundaryExpansion)
			{
				this.display = display;
				this.cam = cam;
				this.renderLayout = renderLayout;
				this.zoomScale = zoomScale;
				this.analyticBoundaryExpansion = analyticBoundaryExpansion;
			}
		}
		
		public void ApplyFrameSettings(
			ParticleDisplay2D display,
			Camera cam,
			ParticleFluidRenderLayout2D renderLayout,
			RenderTexture combinedAccumulationTexture,
			Texture velocityPhase0AccumulationTexture,
			Texture velocityPhase1AccumulationTexture)
		{
			combinedSourceTexture = combinedAccumulationTexture;
			FrameContext context = new FrameContext(
				display,
				cam,
				renderLayout,
				display.GetZoomScale(cam),
				MetaballRenderer2D.GetAnalyticBoundaryExpansion(display)
			);

			ApplyTemporalSettings(context, combinedAccumulationTexture, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture);
			bool renderCaustics = lightingMode == LightingMode.FullCaustics;
			bool renderSoftLight = ShouldRenderPhaseDiffuseLight() || ShouldRenderRadianceCascadeLight();
			Texture causticTexture = renderCaustics
				? (denoisingEnabled ? causticTemporalTexture : causticResolvedTexture)
				: Texture2D.blackTexture;

			ApplySettings(
				context,
				renderCaustics,
				renderSoftLight,
				causticTexture
			);
		}
		

		public void ApplySettings(
			FrameContext context,
			bool renderCaustics,
			bool renderSoftLight,
			Texture causticTexture)
		{
			ParticleDisplay2D display = context.display;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			PhaseMaterialSettings[] materials = PhaseMaterials;

			materialRenderRegion = context.renderLayout.Material;
			causticRenderRegion = context.renderLayout.Caustic;
			currentZoomScale = context.zoomScale;
			
			Vector4[] lightBaseDirections = new Vector4[LightSlotCount];
			Vector4[] lightDirections = new Vector4[LightSlotCount];
			Vector4[] lightPoints = new Vector4[LightSlotCount];
			Vector4[] lightColors = new Vector4[LightSlotCount];
			Vector4[] lightData = new Vector4[LightSlotCount];

			BindMaterialTextures();
			lightingMaterial.SetVector("particleFluidWorldCenter", new Vector4(materialRenderRegion.WorldCenter.x, materialRenderRegion.WorldCenter.y, 0f, 0f));
			lightingMaterial.SetVector("particleFluidWorldSize", new Vector4(materialRenderRegion.WorldSize.x, materialRenderRegion.WorldSize.y, 0f, 0f));
			lightingMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			lightingMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			lightingMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			lightingMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			lightingMaterial.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
			lightingMaterial.SetInt("particleFluidCausticsEnabled", renderCaustics ? 1 : 0);
			lightingMaterial.SetTexture("CausticTex", causticTexture);
			lightingMaterial.SetInt("particleFluidCausticRegionEnabled", causticRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetVector("particleFluidCausticUvRect", causticRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			lightingMaterial.SetFloat("particleFluidRadianceCascadeDirectCausticStrength", radianceCascadeDirectCausticStrength);
			lightingMaterial.SetFloat("particleFluidRadianceCascadePhase0Visibility", radianceCascadePhase0Visibility);
			lightingMaterial.SetFloat("particleFluidRadianceCascadePhase1Visibility", radianceCascadePhase1Visibility);
			lightingMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			lightingMaterial.SetTexture("SoftLightTexPhase1", Texture2D.blackTexture);
			currentSoftLightPhase0Texture = Texture2D.blackTexture;
			currentSoftLightPhase1Texture = Texture2D.blackTexture;
			for (int phaseIndex = 0; phaseIndex < LitPhaseCount; phaseIndex++)
			{
				PhaseMaterialSettings material = materials[phaseIndex];
				phaseDiffuseLightTints[phaseIndex] = material.diffuseLightTint;
				phaseSurfaceData[phaseIndex] = new Vector4(material.reflectance, material.roughness, material.metallic, material.screenSpaceReflectionStrength);
				phaseSoftLightData[phaseIndex] = new Vector4(material.causticAdditiveBlend, material.diffuseAdditiveBlend, material.diffuseNormalInfluence, 0f);
			}
			lightingMaterial.SetVectorArray("particleFluidPhaseDiffuseLightTint", phaseDiffuseLightTints);
			lightingMaterial.SetVectorArray("particlePhaseSurface", phaseSurfaceData);
			lightingMaterial.SetVectorArray("particlePhaseSoftLight", phaseSoftLightData);
			lightingMaterial.SetFloat("densityThreshold", display.metaballs.densityThreshold);
			lightingMaterial.SetFloat("particleFluidIridescenceIntensity", iridescenceIntensity);
			lightingMaterial.SetFloat("particleFluidIridescenceScale", iridescenceScale);
			lightingMaterial.SetFloat("particleAmbientLight", ambientLight);
			for (int i = 0; i < LightSlotCount; i++)
			{
				FluidLightSettings light = lights[i];
				bool lightEnabled = light.enabled && light.intensity > 0f;
				Vector3 baseDirection = light.Direction;
				Vector3 refractedDirection = GetDirectLightingDirection(display, baseDirection);
				lightBaseDirections[i] = new Vector4(baseDirection.x, baseDirection.y, baseDirection.z, 0f);
				lightDirections[i] = new Vector4(refractedDirection.x, refractedDirection.y, refractedDirection.z, 0f);
				lightPoints[i] = GetPointLightVector(light);
				lightColors[i] = light.EffectiveColor;
				lightData[i] = new Vector4(
					lightEnabled ? 1f : 0f,
					(float)light.type,
					light.point.falloff,
					i == 0 ? (lightEnabled ? light.intensity : 0f) : light.intensity
				);
			}
			lightingMaterial.SetVectorArray("particleLightBaseDirections", lightBaseDirections);
			lightingMaterial.SetVectorArray("particleLightDirections", lightDirections);
			lightingMaterial.SetVectorArray("particleLightPoints", lightPoints);
			lightingMaterial.SetVectorArray("particleLightColors", lightColors);
			lightingMaterial.SetVectorArray("particleLightData", lightData);
			lightingMaterial.SetColor("particleFresnelColor", fresnelColor);
			lightingMaterial.SetFloat("particleFresnelIntensity", fresnelIntensity);
			lightingMaterial.SetFloat("particleFresnelPower", fresnelPower);
			lightingMaterial.SetFloat("screenSpaceReflectionDistance", screenSpaceReflectionDistance * currentZoomScale);
			lightingMaterial.SetFloat("screenSpaceReflectionEdgePower", screenSpaceReflectionEdgePower);
			lightingMaterial.SetFloat("particleSpecularCausticSampleOffset", specularCausticSampleOffset * currentZoomScale);
			lightingMaterial.SetFloat("particleSpecularAntiAliasingStrength", specularAntiAliasingStrength);
			Vector4 phaseRadiusScale = GetPhaseRadiusScale(display.metaballs.phase0RenderBias);
			lightingMaterial.SetVector("particleSpecularCausticPhaseScale", phaseRadiusScale);
			lightingMaterial.SetVector("particleAmbientOcclusionPhaseScale", phaseRadiusScale);
			lightingMaterial.SetFloat("particleTransmissionIntensity", transmissionIntensity);
			lightingMaterial.SetFloat("particleTransmissionPower", transmissionPower);
			lightingMaterial.SetFloat("particleAmbientOcclusion", ambientOcclusion);
			lightingMaterial.SetFloat("particleAmbientOcclusionPower", ambientOcclusionPower);
			ApplyProjectedShadowSettings(false, Vector2.zero);
		}

		internal void ApplyProjectedShadowSettings(bool enabled, Vector2 direction)
		{
			lightingMaterial.SetInt("particleFluidProjectedShadowEnabled", enabled ? 1 : 0);
			lightingMaterial.SetTexture("ProjectedShadowTex", enabled && projectedShadowMapTexture != null ? projectedShadowMapTexture : Texture2D.blackTexture);
			lightingMaterial.SetVector("particleFluidProjectedShadowDirection", new Vector4(direction.x, direction.y, 0f, 0f));
			lightingMaterial.SetFloat("particleFluidProjectedShadowOffset", projectedShadowOffset);
			lightingMaterial.SetFloat("particleFluidProjectedShadowExpansion", projectedShadowExpansion);
		}

		static Vector4 GetPhaseRadiusScale(float phase0RenderBias)
		{
			float phaseBoundary = Mathf.Clamp01(0.5f + Mathf.Clamp(phase0RenderBias, -1f, 1f) * 0.5f);
			float phase0Scale = Mathf.Sqrt(Mathf.Max(phaseBoundary * 2f, 0.0001f));
			float phase1Scale = Mathf.Sqrt(Mathf.Max((1f - phaseBoundary) * 2f, 0.0001f));
			return new Vector4(phase0Scale, phase1Scale, 0f, 0f);
		}

		public static Vector4 GetPointLightVector(FluidLightSettings light)
		{
			return new Vector4(light.point.position.x, light.point.position.y, Mathf.Max(light.point.position.z, 0.0001f), Mathf.Max(light.point.range, 0.0001f));
		}
		
		internal void Render(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam)
		{
			BindMaterialTextures();
			commandBuffer.BeginSample("Particle Fluid/Final Lighting");
			ConfigureLightingCompositeToCamera();
			commandBuffer.Blit(null, finalTarget, lightingMaterial, LightingPass);
			commandBuffer.EndSample("Particle Fluid/Final Lighting");
		}

		void ConfigureLightingCompositeToCamera()
		{
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", materialRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetVector("particleFluidCompositeUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetVector("particleFluidCameraUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
		}

		internal void ConfigureLightingIntermediate()
		{
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidCompositeUvRect", new Vector4(0f, 0f, 1f, 1f));
			lightingMaterial.SetVector("particleFluidCameraUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
		}

		public bool ShouldRenderProjectedShadows()
		{
			return lightingMode == LightingMode.Shadows
			       && projectedShadowCompute != null
			       && lights[0].enabled
			       && lights[0].type == FluidLightSettings.LightType.Directional;
		}
		
		public bool ShouldRenderPhaseDiffuseLight()
		{
			PhaseMaterialSettings[] materials = PhaseMaterials;
			return lightingMode == LightingMode.FullCaustics
			       && (softLightMode == SoftLightMode.GaussianDiffuse || softLightMode == SoftLightMode.Hybrid)
			       && phaseDiffuseLightCompute != null
			       && (
				       materials[0].diffuseScatterStrength > 0f
				       || materials[1].diffuseScatterStrength > 0f
			       );
		}

		public bool ShouldRenderRadianceCascadeLight()
		{
			bool hasShader = radianceCascadeTraceMode == RadianceCascadeTraceMode.PhaseBoundarySdf
				? (radianceCascadeSdfShader != null || Shader.Find("Hidden/Particle2DMetaballRadianceCascadesSdf") != null)
				: radianceCascadeShader != null;
			return (softLightMode == SoftLightMode.RadianceCascade || softLightMode == SoftLightMode.Hybrid)
			       && hasShader;
		}

		internal bool UsesRadianceCascadeSdfField()
		{
			return true;
		}

		public bool ShouldRenderCausticDebug()
		{
			return debugMode != LightingDebugVisualization.None;
		}

		public void EnsureLightingResources(ParticleFluidRenderLayout2D renderLayout)
		{
#if UNITY_EDITOR
			EnsureEditorComputeReferences();
#endif
			bool useProjectedShadowMap =
				(ShouldRenderProjectedShadows()
				 || (denoisingEnabled
				     && (projectedShadowHistoryRejection || temporalMotionSource == TemporalMotionSource.ProjectedShadow)))
				&& projectedShadowCompute != null
				&& lights[0].enabled
				&& lights[0].type == FluidLightSettings.LightType.Directional;

			void EnsureProjectedShadowResources()
			{
				if (useProjectedShadowMap)
				{
					ComputeHelper.CreateStructuredBuffer<uint>(ref projectedShadowMapBuffer, projectedShadowMapBins);
					ComputeHelper.CreateRenderTexture(ref projectedShadowMapTexture, projectedShadowMapBins, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Projected Shadow Map");
					ComputeHelper.CreateRenderTexture(ref projectedShadowMapHistoryTexture, projectedShadowMapBins, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Projected Shadow Map History");
				}
				else
				{
					ComputeHelper.Release(projectedShadowMapBuffer);
					ComputeHelper.Release(projectedShadowMapTexture, projectedShadowMapHistoryTexture);
					projectedShadowMapBuffer = null;
					projectedShadowMapTexture = null;
					projectedShadowMapHistoryTexture = null;
					previousProjectedShadowDirection = Vector2.zero;
				}
			}

			currentCausticRenderRegion = renderLayout.Caustic;
			bool renderPhaseDiffuseLight = ShouldRenderPhaseDiffuseLight();
			bool renderRadianceCascadeLight = ShouldRenderRadianceCascadeLight();
			bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
			int causticWidth = currentCausticRenderRegion.PixelWidth;
			int causticHeight = currentCausticRenderRegion.PixelHeight;

			if (renderSoftLight)
			{
				float softLightScale = phaseDiffuseLightTextureScale;
				int softLightWidth = Mathf.Max(1, Mathf.RoundToInt(causticWidth * softLightScale));
				int softLightHeight = Mathf.Max(1, Mathf.RoundToInt(causticHeight * softLightScale));
				ComputeHelper.CreateRenderTexture(ref softLightTexture0, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 0");
				ComputeHelper.CreateRenderTexture(ref softLightTexture1, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 1");
				if (softLightMode == SoftLightMode.Hybrid)
				{
					ComputeHelper.CreateRenderTexture(ref softLightPhase0Texture, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light Phase 0");
				}
				else
				{
					ComputeHelper.Release(softLightPhase0Texture);
					softLightPhase0Texture = null;
				}
				if (renderRadianceCascadeLight && UsesRadianceCascadeSdfField())
				{
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedA, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed A");
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfSeedB, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Seed B");
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadA, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload A");
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfPayloadB, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Payload B");
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfNormalA, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Normal A");
					ComputeHelper.CreateRenderTexture(ref radianceCascadeSdfNormalB, softLightWidth, softLightHeight, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D RC SDF Normal B");
				}
				else
				{
					ComputeHelper.Release(radianceCascadeSdfSeedA, radianceCascadeSdfSeedB, radianceCascadeSdfPayloadA, radianceCascadeSdfPayloadB, radianceCascadeSdfNormalA, radianceCascadeSdfNormalB);
					radianceCascadeSdfSeedA = null;
					radianceCascadeSdfSeedB = null;
					radianceCascadeSdfPayloadA = null;
					radianceCascadeSdfPayloadB = null;
					radianceCascadeSdfNormalA = null;
					radianceCascadeSdfNormalB = null;
				}
			}
			else
			{
				ReleasePhaseDiffuseLightTextures();
			}

			if (lightingMode == LightingMode.FullCaustics)
			{
				RenderTexture CreateHistoryBackup(RenderTexture source, string name)
				{
					if (source == null || !source.IsCreated() || source.width <= 0 || source.height <= 0)
					{
						return null;
					}

					RenderTexture backup = ComputeHelper.CreateRenderTexture(source.width, source.height, source.filterMode, source.graphicsFormat, name);
					Graphics.Blit(source, backup);
					return backup;
				}

				int causticAccumulationCount = causticWidth * causticHeight * 4;
				
				ComputeHelper.CreateStructuredBuffer<uint>(ref causticAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<int>(ref causticMotionAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticLightParamsBuffer, 18);
				ComputeHelper.CreateStructuredBuffer<Vector4>(ref causticMaterialParamsBuffer, MaterialSlotCount * 2);
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
				ComputeHelper.CreateRenderTexture(ref causticMotionTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilatedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilated");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilationScratchTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilation Scratch");
				if (denoisingEnabled)
				{
					EnsureProjectedShadowResources();

					bool canMigrateHistory = hasPreviousCausticCamera && previousCausticWorldSize.x > 0f && previousCausticWorldSize.y > 0f;
					bool causticHistoryResize = causticHistoryTexture != null
						&& causticHistoryTexture.IsCreated()
						&& (causticHistoryTexture.width != causticWidth || causticHistoryTexture.height != causticHeight);
					RenderTexture causticHistoryBackup = canMigrateHistory && causticHistoryResize
						? CreateHistoryBackup(causticHistoryTexture, "Particle2D Caustic History Backup")
						: null;

					bool historyChanged = ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
					ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
					bool migratedHistory = false;
					if (causticHistoryBackup != null && historyChanged)
					{
						migratedHistory = temporalCaustics.TryMigrateHistoryOnResize(
							causticHistoryBackup,
							causticHistoryTexture,
							previousCausticWorldCenter,
							previousCausticWorldSize,
							currentCausticRenderRegion.WorldCenter,
							currentCausticRenderRegion.WorldSize);
					}

					ComputeHelper.Release(causticHistoryBackup);

					bool historyMigrationFailed = historyChanged && !migratedHistory;
					if (migratedHistory && !historyMigrationFailed)
					{
						previousCausticWorldCenter = currentCausticRenderRegion.WorldCenter;
						previousCausticWorldSize = currentCausticRenderRegion.WorldSize;
					}
					clearCausticHistory |= historyMigrationFailed;
				}
				else
				{
					ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture);
					ComputeHelper.Release(projectedShadowMapBuffer);
					ComputeHelper.Release(projectedShadowMapTexture, projectedShadowMapHistoryTexture);
					causticHistoryTexture = null;
					causticTemporalTexture = null;
					projectedShadowMapBuffer = null;
					projectedShadowMapTexture = null;
					projectedShadowMapHistoryTexture = null;
					previousProjectedShadowDirection = Vector2.zero;
					clearCausticHistory = true;
					hasPreviousCausticCamera = false;
					causticTemporalFrameCount = 0;
				}
			}
			else
			{
				ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer, causticLightParamsBuffer, causticMaterialParamsBuffer);
				causticAccumulationBuffer = null;
				causticMotionAccumulationBuffer = null;
				causticLightParamsBuffer = null;
				causticMaterialParamsBuffer = null;
				ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
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
				EnsureProjectedShadowResources();
			}
		}
		
		internal static ParticleFluidRenderRegion2D GetScaledRenderRegion(ParticleFluidRenderRegion2D source, float scale)
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

		internal static ParticleFluidRenderRegion2D GetCameraScaledRenderRegion(Camera cam, ParticleFluidRenderRegion2D source, float scale)
		{
			float clampedScale = Mathf.Max(scale, 0.0001f);
			int pixelWidth = Mathf.Max(1, Mathf.RoundToInt(source.SourceUvRect.z * cam.pixelWidth * clampedScale));
			int pixelHeight = Mathf.Max(1, Mathf.RoundToInt(source.SourceUvRect.w * cam.pixelHeight * clampedScale));
			return new ParticleFluidRenderRegion2D(
				source.WorldCenter,
				source.WorldSize,
				source.SourceUvRect,
				pixelWidth,
				pixelHeight,
				source.IsCropped);
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(ParticleFluidLighting2D.FluidLightSettings))]
	sealed class FluidLightSettingsDrawer : PropertyDrawer
	{
		static readonly string[] CommonFields =
		{
			"enabled",
			"type",
			"temperatureKelvin",
			"color",
			"intensity",
			"sampleBias"
		};

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);
			Rect line = NextLine(ref position);
			property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
			if (!property.isExpanded)
			{
				EditorGUI.EndProperty();
				return;
			}

			EditorGUI.indentLevel++;
			foreach (string fieldName in CommonFields)
			{
				DrawProperty(ref position, property.FindPropertyRelative(fieldName));
			}

			SerializedProperty typeProperty = property.FindPropertyRelative("type");
			bool isPointLight = typeProperty != null
				&& typeProperty.enumValueIndex == (int)ParticleFluidLighting2D.FluidLightSettings.LightType.Point;
			SerializedProperty group = property.FindPropertyRelative(isPointLight ? "point" : "directional");
			if (group != null)
			{
				if (isPointLight)
				{
					DrawProperty(ref position, group.FindPropertyRelative("position"));
					DrawProperty(ref position, group.FindPropertyRelative("followsMouse"));
					DrawProperty(ref position, group.FindPropertyRelative("height"));
					DrawProperty(ref position, group.FindPropertyRelative("range"));
					DrawProperty(ref position, group.FindPropertyRelative("falloff"));
				}
				else
				{
					DrawProperty(ref position, group.FindPropertyRelative("azimuthDegrees"));
					DrawProperty(ref position, group.FindPropertyRelative("elevationDegrees"));
					DrawProperty(ref position, group.FindPropertyRelative("angularRadiusDegrees"));
				}
			}

			EditorGUI.indentLevel--;
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			int visibleFieldCount = 1;
			if (property.isExpanded)
			{
				SerializedProperty typeProperty = property.FindPropertyRelative("type");
				bool isPointLight = typeProperty != null
					&& typeProperty.enumValueIndex == (int)ParticleFluidLighting2D.FluidLightSettings.LightType.Point;
				visibleFieldCount += CommonFields.Length + (isPointLight ? 5 : 3);
			}

			return visibleFieldCount * EditorGUIUtility.singleLineHeight
				+ Mathf.Max(0, visibleFieldCount - 1) * EditorGUIUtility.standardVerticalSpacing;
		}

		static void DrawProperty(ref Rect remainingPosition, SerializedProperty property)
		{
			if (property == null)
			{
				return;
			}

			EditorGUI.PropertyField(NextLine(ref remainingPosition), property);
		}

		static Rect NextLine(ref Rect remainingPosition)
		{
			float lineHeight = EditorGUIUtility.singleLineHeight;
			Rect line = new Rect(remainingPosition.x, remainingPosition.y, remainingPosition.width, lineHeight);
			remainingPosition.y += lineHeight + EditorGUIUtility.standardVerticalSpacing;
			return line;
		}
	}
#endif
}
