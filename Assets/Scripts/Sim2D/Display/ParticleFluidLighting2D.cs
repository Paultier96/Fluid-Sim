using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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
			[Range(0f, 1f)] public float reflectance = 0f;
			[Range(0.02f, 1f)] public float roughness = 0.35f;
			[Range(0f, 1f)] public float metallic = 0f;
			[Range(0f, 1f)] public float screenSpaceReflectionStrength = 0f;
			[Min(0f)] public float diffuseScatterStrength = 0f;
			[Min(0f)] public float diffuseGaussianRadius = 24f;
			[ColorUsage(false, true)] public Color diffuseLightTint = Color.white;
			[Tooltip("Blends phase-aware diffuse lighting tint from Diffuse Light Tint toward the local phase albedo gradient. 0 uses Diffuse Light Tint; 1 uses albedo.")]
			[Range(0f, 1f)] public float diffuseAlbedoTintBlend = 0f;
			
			public PhaseMaterialSettings(float indexOfRefraction)
			{
				this.indexOfRefraction = indexOfRefraction;
			}
		}

		[Serializable]
		public class DirectionalLightSettings
		{
			public enum LightType
			{
				Directional,
				Point
			}

			public bool enabled = true;
			public LightType type = LightType.Directional;
			public float azimuthDegrees = 122.5f;
			[Range(-89, 89f)] public float elevationDegrees = 50.3f;
			[Tooltip("6500 -> neutral daylight; 2700 -> incandescent light")]
			[Range(1000f, 20000f)] public float temperatureKelvin = 6500f;
			[ColorUsage(false, true)] public Color color = Color.white;
			[Min(0f)] public float intensity = 0.45f;
			[Tooltip("Multiplier ray allocation.")]
			[Min(0f)] public float sampleBias = 1f;
			[Header("Point Light")]
			public Vector2 pointPosition;
			public bool pointFollowsMouse = false;
			[Min(0.0001f)] public float pointHeight = 8f;
			[Tooltip("World-space radius over which point-light brightness fades to zero.")]
			[Min(0.0001f)] public float pointRange = 20f;
			[Tooltip("Point-light attenuation exponent. 1 is linear, higher values make the light fall off faster near the edge of its range.")]
			[Min(0.1f)] public float pointFalloff = 2f;
			
			public DirectionalLightSettings(float azimuthDegrees, float elevationDegrees, float intensity)
				: this(azimuthDegrees, elevationDegrees, intensity, true)
			{
			}

			public DirectionalLightSettings(float azimuthDegrees, float elevationDegrees, float intensity, bool enabled)
			{
				this.azimuthDegrees = azimuthDegrees;
				this.elevationDegrees = elevationDegrees;
				this.intensity = intensity;
				this.enabled = enabled;
			}

			public Vector3 Direction
			{
				get
				{
					float azimuth = azimuthDegrees * Mathf.Deg2Rad;
					float elevation = elevationDegrees * Mathf.Deg2Rad;
					float planarLength = Mathf.Cos(elevation);
					return new Vector3(
						Mathf.Cos(azimuth) * planarLength,
						Mathf.Sin(azimuth) * planarLength,
						Mathf.Sin(elevation)
					).normalized;
				}
			}

			public Color EffectiveColor
			{
				get
				{
					Color kelvinColor = KelvinToRgb(temperatureKelvin);
					return new Color(
						color.r * kelvinColor.r,
						color.g * kelvinColor.g,
						color.b * kelvinColor.b,
						color.a
					);
				}
			}

			static Color KelvinToRgb(float kelvin)
			{
				float temperature = Mathf.Clamp(kelvin, 1000f, 20000f) / 100f;
				float red;
				float green;
				float blue;

				if (temperature <= 66f)
				{
					red = 1f;
					green = Mathf.Clamp01((99.4708025861f * Mathf.Log(temperature) - 161.1195681661f) / 255f);
				}
				else
				{
					red = Mathf.Clamp01((329.698727446f * Mathf.Pow(temperature - 60f, -0.1332047592f)) / 255f);
					green = Mathf.Clamp01((288.1221695283f * Mathf.Pow(temperature - 60f, -0.0755148492f)) / 255f);
				}

				blue = temperature >= 66f
					? 1f
					: temperature <= 19f
						? 0f
						: Mathf.Clamp01((138.5177312231f * Mathf.Log(temperature - 10f) - 305.0447927307f) / 255f);

				return new Color(red, green, blue, 1f);
			}
		}

		public enum TemporalMotionSource
		{
			Static,
			ParticleMotion,
			CausticMotion
		}

		public enum DirectionalLightingMode
		{
			Direct,
			RefractAtAnalyticBoundary,
			DirectionalLightField
		}

		public enum LightingDebugVisualization
		{
			None,
			Caustics,
			SoftLight,
			DirectionalLightField,
			CausticMotion,
			TemporalRejection,
			TemporalClamp
		}

		[Header("Shaders")]
		public Shader lightingShader;
		public Shader colorBleedShader;
		public Shader blurShader;
		public Shader temporalShader;
		public ComputeShader computeShader;
		public ComputeShader phaseDiffuseLightCompute;
		public Shader radianceCascadeShader;

		[Header("Debug")]
		public LightingDebugVisualization debugMode = LightingDebugVisualization.None;

		[Header("Lights")]
		public DirectionalLightSettings primaryLight = new (122.5f, 50.3f, 0.45f);
		public DirectionalLightSettings secondaryLight = new (45f, 40f, 0.2f, false);
		public DirectionalLightSettings tertiaryLight = new (-60f, 35f, 0.1f, false);
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

		[Header("Transmission")]
		[Min(0f)] public float transmissionIntensity = 0.2f;
		[Min(0.1f)] public float transmissionPower = 2f;

		[Header("Ambient Occlusion")]
		[Range(0f, 1f)] public float ambientOcclusion = 0.2f;
		[Min(0.1f)] public float ambientOcclusionPower = 2f;

		[Header("Diffuse Color Bleed")]
		public bool diffuseColorBleedEnabled = false;
		[Min(0f)] public float diffuseColorBleedStrength = 0.15f;
		[Range(0.125f, 1f)] public float diffuseColorBleedTextureScale = 0.35f;
		[Min(0f)] public float diffuseColorBleedRadius = 48f;
		[Range(0f, 1f)] public float diffuseColorBleedSelfSubtract = 0.5f;
		[Range(0f, 1f)] public float diffuseColorBleedNormalWeight = 0.5f;

		[Header("Iridescence")]
		[Min(0f)] public float iridescenceIntensity = 0f;
		[Min(0f)] public float iridescenceScale = 2.0f;

		[Header("Raymarched Lighting - Setup")]
		public bool causticsEnabled;
		[Range(0.125f, 1f)] public float textureScale = 0.5f;

		[Header("Phase Materials")]
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
		[Tooltip("Angular radius of the raymarched light source in degrees. 0 keeps perfectly parallel rays.")]
		[Min(0f)] public float lightAngularRadiusDegrees = 0f;
		[Tooltip("Refracts lighting rays through the analytic ellipse/cut simulation boundary when elliptical bounds are enabled.")]
		public bool useAnalyticBoundary = true;

		[Header("Raymarched Lighting - Rays")]
		[Tooltip("Number of raymarch steps used per lighting ray. Higher values find exits more reliably but cost more.")]
		[Range(8, 192)] public int raySteps = 64;
		[HideInInspector] public float stepPixels = 1.0f; //delete?
		[Min(0f)] public float rayBrightness = 1f; //delete?
		[Min(1)] public int rayStride = 1;
		[Range(1, 64)] public int raysPerPixel = 1;
		[Tooltip("Samples the medium colour every N ray steps while absorption is active.")]
		[Min(1)] public int colourSampleStride = 8;
		[Min(0f)] public float blur = 1.5f;
		public DirectionalLightingMode directionalLightingMode = DirectionalLightingMode.RefractAtAnalyticBoundary;
		[Min(0f)] public float directionalLightFieldBlur = 1.5f;

		[Header("Soft Subsurface Lighting")]
		public bool phaseDiffuseLightEnabled = false;
		[Range(0.25f, 1f)] public float phaseDiffuseLightTextureScale = 0.5f;
		[Tooltip("How strongly phase boundaries block phase-aware diffuse lighting. Higher values keep light inside each phase.")]
		[Min(0f)] public float phaseDiffuseLightBoundarySharpness = 5f; //delete?
		public bool radianceCascadeEnabled = false;
		[Tooltip("Number of cascade levels. Higher values spread soft light farther but cost one fullscreen pass per level.")]
		[Range(1, 6)] public int radianceCascadeCount = 4;
		[Tooltip("Maximum ray range in normalized lighting-texture UV space at the reference zoom.")]
		[Min(0.0001f)] public float radianceCascadeRayRange = 1.25f;
		[Tooltip("Raymarch samples per cascade ray segment.")]
		[Range(1, 64)] public int radianceCascadeRaySteps = 16;
		[Tooltip("Multiplier applied to the radiance cascade soft light before compositing.")]
		[Min(0f)] public float radianceCascadeIntensity = 1f;
		[Tooltip("Amount of sharp direct caustic lighting kept while a soft SSS pass is enabled. 0 replaces sharp caustics with soft SSS, 1 keeps the previous sharp+soft result.")]
		[Range(0f, 1f)] public float radianceCascadeDirectCausticStrength = 1f;
		[Tooltip("Applies the same Beer-Lambert RGB absorption used by raymarched caustics while radiance cascade rays travel through phase 0.")]
		public bool radianceCascadeAbsorption = true;

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

		
		internal void EnsureMaterial(Shader shader)
		{
			Shader bleedShader = colorBleedShader != null
				? colorBleedShader
				: Shader.Find("Hidden/Particle2DParticleFluidColorBleed");
			EnsureMaterials(shader, bleedShader);
		}

		const int LightingPass = 0;
		const int ColorBleedDownsamplePass = 0;
		const int ColorBleedHorizontalPass = 1;
		const int ColorBleedVerticalPass = 2;
		const int ColorBleedCompositePass = 3;
		static readonly int FinalLightingTempId = Shader.PropertyToID("_ParticleFluidFinalLightingTemp");
		static readonly int ColorBleedTemp0Id = Shader.PropertyToID("_ParticleFluidColorBleed0");
		static readonly int ColorBleedTemp1Id = Shader.PropertyToID("_ParticleFluidColorBleed1");

		Material lightingMaterial;
		Material colorBleedMaterial;
		Material radianceCascadeMaterial;
		Material causticBlurMaterial;
		Material lightDirectionBlurMaterial;
		Material temporalMaterial;
		Material causticMotionBlurMaterial;
		const int MaxCausticTraceThreads = 65535;
		const int CausticTraceThreadGroupSize = 64;
		const float TwoPi = 2 * Mathf.PI;
		const int PointLightBoundaryEllipseSamples = 128;
		const int PointLightBoundaryCutSamples = 31;
		readonly float[] pointLightBoundaryAngles = new float[PointLightBoundaryEllipseSamples + PointLightBoundaryCutSamples + 2];
		ParticleFluidCausticsView caustics;
		ParticleFluidCausticsTrace traceCaustics;
		ParticleFluidCausticsTemporal temporalCaustics;
		ParticleFluidCausticsSoftLight softLightCaustics;
		Texture materialAlbedoTexture;
		Texture materialNormal0Texture;
		Texture materialNormal1Texture;
		ParticleFluidRenderRegion2D materialRenderRegion;
		ParticleFluidRenderRegion2D causticRenderRegion;
		float currentZoomScale = 1f;
		
		
		ComputeBuffer causticAccumulationBuffer;
		ComputeBuffer causticMotionAccumulationBuffer;
		ComputeBuffer lightDirectionAccumulationBuffer;
		ComputeBuffer lightDirectionAccumulationFallbackBuffer;
		public RenderTexture lightDirectionResultFallbackTexture;
		public RenderTexture causticResolvedTexture;
		public RenderTexture causticBlurTexture;
		public RenderTexture causticMotionTexture;
		public RenderTexture causticMotionDilatedTexture;
		public RenderTexture causticMotionDilationScratchTexture;
		public RenderTexture lightDirectionTexture;
		public RenderTexture lightDirectionBlurTexture;
		public RenderTexture softLightTexture0;
		public RenderTexture softLightTexture1;
		public RenderTexture causticHistoryTexture;
		public RenderTexture causticTemporalTexture;
		public RenderTexture lightDirectionHistoryTexture;
		public RenderTexture lightDirectionTemporalTexture;
		
		public bool clearCausticHistory;
		public bool hasPreviousCausticCamera;
		public int causticFrameIndex;
		public int causticTemporalFrameCount;
		public Vector2 previousCausticWorldCenter;
		public Vector2 previousCausticWorldSize;
		public ParticleFluidRenderRegion2D currentCausticRenderRegion;
		
		
		public Material Material => lightingMaterial;
		public bool IsReady => lightingMaterial != null;
		internal ParticleFluidCausticsView Caustics => caustics ??= new ParticleFluidCausticsView(this);
		internal ParticleFluidCausticsTrace TraceCaustics => traceCaustics ??= new ParticleFluidCausticsTrace(Caustics);
		internal ParticleFluidCausticsTemporal TemporalCaustics => temporalCaustics ??= new ParticleFluidCausticsTemporal(Caustics);
		internal ParticleFluidCausticsSoftLight SoftLightCaustics => softLightCaustics ??= new ParticleFluidCausticsSoftLight(Caustics);
		internal Material TemporalMaterial => temporalMaterial;
		internal Material CausticMotionBlurMaterial => causticMotionBlurMaterial;
		internal Material CausticBlurMaterial => causticBlurMaterial;
		internal Material LightDirectionBlurMaterial => lightDirectionBlurMaterial;
		internal Material RadianceCascadeMaterial => radianceCascadeMaterial;
		internal int MaxCausticTraceThreadCount => MaxCausticTraceThreads;
		internal int CausticTraceThreadGroupWidth => CausticTraceThreadGroupSize;

		public void EnsureMaterials(Shader shader, Shader colorBleedShader)
		{
			Shader temporalShader = this.temporalShader != null ? this.temporalShader : Shader.Find("Hidden/Particle2DMetaballTemporal");
			EnsureMaterial(ref lightingMaterial, shader);
			EnsureMaterial(ref colorBleedMaterial, colorBleedShader);
			EnsureMaterial(ref radianceCascadeMaterial, radianceCascadeShader);
			EnsureMaterial(ref temporalMaterial, temporalShader);
			EnsureMaterial(ref causticBlurMaterial, blurShader);
			EnsureMaterial(ref causticMotionBlurMaterial, blurShader);
			EnsureMaterial(ref lightDirectionBlurMaterial, blurShader);
		}

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

		public void SetMaterialTextures(Texture albedo, Texture normal0, Texture normal1, ParticleFluidRenderRegion2D renderRegion)
		{
			materialAlbedoTexture = albedo != null ? albedo : Texture2D.blackTexture;
			materialNormal0Texture = normal0 != null ? normal0 : Texture2D.blackTexture;
			materialNormal1Texture = normal1 != null ? normal1 : Texture2D.blackTexture;
			materialRenderRegion = renderRegion;
			BindMaterialTextures();
		}

		public void BindMaterialTextures()
		{
			if (lightingMaterial == null)
			{
				return;
			}

			lightingMaterial.SetTexture("MaterialAlbedoTex", materialAlbedoTexture != null ? materialAlbedoTexture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("MaterialNormalTex", materialNormal0Texture != null ? materialNormal0Texture : Texture2D.blackTexture);
			lightingMaterial.SetTexture("MaterialNormalTex1", materialNormal1Texture != null ? materialNormal1Texture : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", materialRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetVector("particleFluidCompositeUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetVector("particleFluidCameraUvRect", materialRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
			int width = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.width : 1, 1);
			int height = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.height : 1, 1);
			lightingMaterial.SetVector("MaterialAlbedoTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
		}
		
		public Vector3 GetDirectLightingDirection(ParticleDisplay2D display, Vector3 lightDirection)
		{
			if (directionalLightingMode == DirectionalLightingMode.Direct || !display.sim.useEllipticalBounds)
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
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(phase1Material.indexOfRefraction, 1.0001f));
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
			public readonly ParticleFluidRenderRegion2D materialRenderRegion;
			public readonly ParticleFluidRenderRegion2D causticRenderRegion;
			public readonly float zoomScale;
			public readonly float analyticBoundaryExpansion;

			public FrameContext(
				ParticleDisplay2D display,
				Camera cam,
				ParticleFluidRenderRegion2D materialRenderRegion,
				ParticleFluidRenderRegion2D causticRenderRegion,
				float zoomScale,
				float analyticBoundaryExpansion)
			{
				this.display = display;
				this.cam = cam;
				this.materialRenderRegion = materialRenderRegion;
				this.causticRenderRegion = causticRenderRegion;
				this.zoomScale = zoomScale;
				this.analyticBoundaryExpansion = analyticBoundaryExpansion;
			}
		}
		
		public void ApplyFrameSettings(ParticleDisplay2D display, Camera cam, ParticleFluidRenderRegion2D currentMaterialRenderRegion, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			FrameContext context = new FrameContext(
				display,
				cam,
				currentMaterialRenderRegion,
				currentCausticRenderRegion,
				display.GetZoomScale(cam),
				MetaballRenderer2D.GetAnalyticBoundaryExpansion(display)
			);

			ApplyTemporalSettings(context, combinedAccumulationTexture, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture);
			bool renderCaustics = ShouldRenderCaustics();
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField();
			bool renderSoftLight = ShouldRenderPhaseDiffuseLight() || ShouldRenderRadianceCascadeLight();
			Texture causticTexture = denoisingEnabled ? causticTemporalTexture : causticResolvedTexture;
			Texture lightDirectionTextureForLighting = Texture2D.blackTexture;
			if (renderDirectionalLightField)
			{
				lightDirectionTextureForLighting = denoisingEnabled ? lightDirectionTemporalTexture : lightDirectionTexture;
			}

			ApplySettings(
				context,
				renderCaustics,
				renderDirectionalLightField,
				renderSoftLight,
				ShouldRenderRadianceCascadeLight(),
				causticTexture,
				lightDirectionTextureForLighting
			);
		}
		

		public void ApplySettings(
			FrameContext context,
			bool renderCaustics,
			bool renderDirectionalLightField,
			bool renderSoftLight,
			bool radianceCascadeSoftLight,
			Texture causticTexture,
			Texture lightDirectionTexture)
		{
			if (lightingMaterial == null)
			{
				return;
			}

			ParticleDisplay2D display = context.display;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;

			Vector3 primaryDirectLightingDirection = GetDirectLightingDirection(display, primaryLight.Direction);
			Vector3 secondaryDirectLightingDirection = GetDirectLightingDirection(display, secondaryLight.Direction);
			Vector3 tertiaryDirectLightingDirection = GetDirectLightingDirection(display, tertiaryLight.Direction);

			materialRenderRegion = context.materialRenderRegion;
			causticRenderRegion = context.causticRenderRegion;
			currentZoomScale = context.zoomScale;

			BindMaterialTextures();
			lightingMaterial.SetVector("particleFluidWorldCenter", new Vector4(materialRenderRegion.WorldCenter.x, materialRenderRegion.WorldCenter.y, 0f, 0f));
			lightingMaterial.SetVector("particleFluidWorldSize", new Vector4(materialRenderRegion.WorldSize.x, materialRenderRegion.WorldSize.y, 0f, 0f));
			lightingMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			lightingMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			lightingMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			lightingMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			lightingMaterial.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
			lightingMaterial.SetInt("particleFluidCausticsEnabled", renderCaustics ? 1 : 0);
			lightingMaterial.SetTexture("CausticTex", causticTexture != null ? causticTexture : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidCausticRegionEnabled", causticRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetVector("particleFluidCausticUvRect", causticRenderRegion.SourceUvRect);
			lightingMaterial.SetInt("particleFluidDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			lightingMaterial.SetTexture("LightDirectionTex", lightDirectionTexture != null ? lightDirectionTexture : Texture2D.blackTexture);
			lightingMaterial.SetInt("particleFluidPhaseDiffuseLightEnabled", renderSoftLight ? 1 : 0);
			lightingMaterial.SetInt("particleFluidSoftLightPhase0Only", radianceCascadeSoftLight ? 1 : 0);
			lightingMaterial.SetFloat("particleFluidRadianceCascadeDirectCausticStrength", radianceCascadeDirectCausticStrength);
			lightingMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			lightingMaterial.SetColor("particleFluidPhase0DiffuseLightTint", phase0Material.diffuseLightTint);
			lightingMaterial.SetColor("particleFluidPhase1DiffuseLightTint", phase1Material.diffuseLightTint);
			lightingMaterial.SetFloat("particleFluidIridescenceIntensity", iridescenceIntensity);
			lightingMaterial.SetFloat("particleFluidIridescenceScale", iridescenceScale);
			lightingMaterial.SetVector("particleBaseLightDirection", primaryLight.Direction);
			lightingMaterial.SetVector("particleLightDirection", primaryDirectLightingDirection);
			lightingMaterial.SetInt("particleLightType", (int)primaryLight.type);
			lightingMaterial.SetVector("particleLightPoint", GetPointLightVector(primaryLight));
			lightingMaterial.SetFloat("particleLightPointFalloff", primaryLight.pointFalloff);
			lightingMaterial.SetVector("particleSecondaryBaseLightDirection", secondaryLight.Direction);
			lightingMaterial.SetVector("particleSecondaryLightDirection", secondaryDirectLightingDirection);
			lightingMaterial.SetInt("particleSecondaryLightEnabled", secondaryLight.enabled && secondaryLight.intensity > 0f ? 1 : 0);
			lightingMaterial.SetInt("particleSecondaryLightType", (int)secondaryLight.type);
			lightingMaterial.SetVector("particleSecondaryLightPoint", GetPointLightVector(secondaryLight));
			lightingMaterial.SetFloat("particleSecondaryLightPointFalloff", secondaryLight.pointFalloff);
			lightingMaterial.SetVector("particleTertiaryBaseLightDirection", tertiaryLight.Direction);
			lightingMaterial.SetVector("particleTertiaryLightDirection", tertiaryDirectLightingDirection);
			lightingMaterial.SetInt("particleTertiaryLightEnabled", tertiaryLight.enabled && tertiaryLight.intensity > 0f ? 1 : 0);
			lightingMaterial.SetInt("particleTertiaryLightType", (int)tertiaryLight.type);
			lightingMaterial.SetVector("particleTertiaryLightPoint", GetPointLightVector(tertiaryLight));
			lightingMaterial.SetFloat("particleTertiaryLightPointFalloff", tertiaryLight.pointFalloff);
			lightingMaterial.SetColor("particleLightColor", primaryLight.EffectiveColor);
			lightingMaterial.SetColor("particleSecondaryLightColor", secondaryLight.EffectiveColor);
			lightingMaterial.SetColor("particleTertiaryLightColor", tertiaryLight.EffectiveColor);
			lightingMaterial.SetFloat("particleAmbientLight", ambientLight);
			lightingMaterial.SetFloat("particleLightIntensity", primaryLight.enabled ? primaryLight.intensity : 0f);
			lightingMaterial.SetFloat("particleSecondaryLightIntensity", secondaryLight.intensity);
			lightingMaterial.SetFloat("particleTertiaryLightIntensity", tertiaryLight.intensity);
			lightingMaterial.SetFloat("particlePhase0Reflectance", phase0Material.reflectance);
			lightingMaterial.SetFloat("particlePhase1Reflectance", phase1Material.reflectance);
			lightingMaterial.SetFloat("particlePhase0Roughness", phase0Material.roughness);
			lightingMaterial.SetFloat("particlePhase1Roughness", phase1Material.roughness);
			lightingMaterial.SetFloat("particlePhase0Metallic", phase0Material.metallic);
			lightingMaterial.SetFloat("particlePhase1Metallic", phase1Material.metallic);
			lightingMaterial.SetColor("particleFresnelColor", fresnelColor);
			lightingMaterial.SetFloat("particleFresnelIntensity", fresnelIntensity);
			lightingMaterial.SetFloat("particleFresnelPower", fresnelPower);
			lightingMaterial.SetFloat("screenSpaceReflectionStrength0", phase0Material.screenSpaceReflectionStrength);
			lightingMaterial.SetFloat("screenSpaceReflectionStrength1", phase1Material.screenSpaceReflectionStrength);
			lightingMaterial.SetFloat("screenSpaceReflectionDistance", screenSpaceReflectionDistance * currentZoomScale);
			lightingMaterial.SetFloat("screenSpaceReflectionEdgePower", screenSpaceReflectionEdgePower);
			lightingMaterial.SetFloat("particleSpecularCausticSampleOffset", specularCausticSampleOffset * currentZoomScale);
			Vector4 phaseRadiusScale = GetPhaseRadiusScale(display.metaballs.phase0RenderBias);
			lightingMaterial.SetVector("particleSpecularCausticPhaseScale", phaseRadiusScale);
			lightingMaterial.SetVector("particleAmbientOcclusionPhaseScale", phaseRadiusScale);
			lightingMaterial.SetFloat("particleTransmissionIntensity", transmissionIntensity);
			lightingMaterial.SetFloat("particleTransmissionPower", transmissionPower);
			lightingMaterial.SetFloat("particleAmbientOcclusion", ambientOcclusion);
			lightingMaterial.SetFloat("particleAmbientOcclusionPower", ambientOcclusionPower);
		}

		static Vector4 GetPhaseRadiusScale(float phase0RenderBias)
		{
			float phaseBoundary = Mathf.Clamp01(0.5f + Mathf.Clamp(phase0RenderBias, -1f, 1f) * 0.5f);
			float phase0Scale = Mathf.Sqrt(Mathf.Max(phaseBoundary * 2f, 0.0001f));
			float phase1Scale = Mathf.Sqrt(Mathf.Max((1f - phaseBoundary) * 2f, 0.0001f));
			return new Vector4(phase0Scale, phase1Scale, 0f, 0f);
		}

		public static Vector4 GetPointLightVector(DirectionalLightSettings light)
		{
			if (light == null)
			{
				return new Vector4(0f, 0f, 0.0001f, 0.0001f);
			}

			return new Vector4(light.pointPosition.x, light.pointPosition.y, Mathf.Max(light.pointHeight, 0.0001f), Mathf.Max(light.pointRange, 0.0001f));
		}

		public void SetSoftLightTexture(Texture texture)
		{
			if (lightingMaterial != null)
			{
				lightingMaterial.SetTexture("SoftLightTex", texture != null ? texture : Texture2D.blackTexture);
			}
		}
		
		internal void Render(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}

			BindMaterialTextures();
			commandBuffer.BeginSample("Particle Fluid/Final Lighting");
			if (!ShouldRenderColorBleed())
			{
				ConfigureLightingCompositeToCamera();
				commandBuffer.Blit(null, finalTarget, lightingMaterial, LightingPass);
				ConfigureLightingIntermediate();
			}
			else
			{
				RenderWithColorBleed(commandBuffer, finalTarget, cam);
			}
			commandBuffer.EndSample("Particle Fluid/Final Lighting");
		}

		bool ShouldRenderColorBleed()
		{
			return diffuseColorBleedEnabled
			       && diffuseColorBleedStrength > 0f
			       && diffuseColorBleedRadius > 0f
			       && colorBleedMaterial != null;
		}

		void RenderWithColorBleed(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, Camera cam)
		{
			int materialWidth = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.width : 1, 1);
			int materialHeight = Mathf.Max(materialAlbedoTexture != null ? materialAlbedoTexture.height : 1, 1);
			int sourceWidth = materialRenderRegion.IsCropped ? materialWidth : Mathf.Max(cam != null ? cam.pixelWidth : materialWidth, 1);
			int sourceHeight = materialRenderRegion.IsCropped ? materialHeight : Mathf.Max(cam != null ? cam.pixelHeight : materialHeight, 1);
			float bleedScale = Mathf.Clamp(diffuseColorBleedTextureScale, 0.125f, 1f);
			int bleedWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * bleedScale));
			int bleedHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * bleedScale));

			commandBuffer.GetTemporaryRT(FinalLightingTempId, sourceWidth, sourceHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
			commandBuffer.GetTemporaryRT(ColorBleedTemp0Id, bleedWidth, bleedHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
			commandBuffer.GetTemporaryRT(ColorBleedTemp1Id, bleedWidth, bleedHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

			commandBuffer.SetRenderTarget(FinalLightingTempId);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			ConfigureLightingIntermediate();
			commandBuffer.Blit(null, FinalLightingTempId, lightingMaterial, LightingPass);
			BindColorBleedMaterial(bleedScale, false);
			commandBuffer.SetGlobalTexture("_ParticleFluidSourceTex", FinalLightingTempId);
			commandBuffer.Blit(FinalLightingTempId, ColorBleedTemp0Id, colorBleedMaterial, ColorBleedDownsamplePass);
			commandBuffer.Blit(ColorBleedTemp0Id, ColorBleedTemp1Id, colorBleedMaterial, ColorBleedHorizontalPass);
			commandBuffer.Blit(ColorBleedTemp1Id, ColorBleedTemp0Id, colorBleedMaterial, ColorBleedVerticalPass);
			BindColorBleedMaterial(bleedScale, true);
			commandBuffer.SetGlobalTexture("_ParticleFluidSourceTex", FinalLightingTempId);
			commandBuffer.SetGlobalTexture("_ParticleFluidBleedTex", ColorBleedTemp0Id);
			commandBuffer.Blit(FinalLightingTempId, finalTarget, colorBleedMaterial, ColorBleedCompositePass);

			commandBuffer.ReleaseTemporaryRT(ColorBleedTemp1Id);
			commandBuffer.ReleaseTemporaryRT(ColorBleedTemp0Id);
			commandBuffer.ReleaseTemporaryRT(FinalLightingTempId);
		}

		void ConfigureLightingCompositeToCamera()
		{
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", materialRenderRegion.IsCropped ? 1 : 0);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
		}

		void ConfigureLightingIntermediate()
		{
			lightingMaterial.SetInt("particleFluidCompositeRegionEnabled", 0);
			lightingMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			lightingMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
		}

		void BindColorBleedMaterial(float bleedScale, bool compositeToCamera)
		{
			colorBleedMaterial.SetTexture("MaterialAlbedoTex", materialAlbedoTexture != null ? materialAlbedoTexture : Texture2D.blackTexture);
			colorBleedMaterial.SetTexture("MaterialNormalTex", materialNormal0Texture != null ? materialNormal0Texture : Texture2D.blackTexture);
			colorBleedMaterial.SetTexture("MaterialNormalTex1", materialNormal1Texture != null ? materialNormal1Texture : Texture2D.blackTexture);
			colorBleedMaterial.SetInt("particleFluidCompositeRegionEnabled", compositeToCamera && materialRenderRegion.IsCropped ? 1 : 0);
			colorBleedMaterial.SetInt("particleFluidClipRegionEnabled", 0);
			colorBleedMaterial.SetVector("particleFluidCompositeUvRect", materialRenderRegion.SourceUvRect);
			colorBleedMaterial.SetVector("particleFluidClipRect", materialRenderRegion.SourceUvRect);
			colorBleedMaterial.SetFloat("_BleedStrength", Mathf.Max(diffuseColorBleedStrength, 0f));
			colorBleedMaterial.SetFloat("_BleedRadius", Mathf.Max(diffuseColorBleedRadius * currentZoomScale * bleedScale, 0f));
			colorBleedMaterial.SetFloat("_BleedSelfSubtract", Mathf.Clamp01(diffuseColorBleedSelfSubtract));
			colorBleedMaterial.SetFloat("_BleedNormalWeight", Mathf.Clamp01(diffuseColorBleedNormalWeight));
		}
		
		public bool ShouldRenderCaustics()
		{
			return causticsEnabled && computeShader != null;
		}
		
		public bool ShouldRenderPhaseDiffuseLight()
		{
			return ShouldRenderCaustics()
			       && phaseDiffuseLightEnabled
			       && phaseDiffuseLightEnabled
			       && phaseDiffuseLightCompute != null
			       && (
				       phase0Material.diffuseScatterStrength > 0f
				       || phase1Material.diffuseScatterStrength > 0f
			       );
		}

		public bool ShouldRenderRadianceCascadeLight()
		{
			return ShouldRenderCaustics()
			       && radianceCascadeEnabled
			       && radianceCascadeShader != null
			       && phase0Material.diffuseScatterStrength > 0f;
		}

		public bool ShouldRenderDirectionalLightField()
		{
			return ShouldRenderCaustics() && directionalLightingMode == DirectionalLightingMode.DirectionalLightField;
		}
		
		public bool ShouldRenderCausticDebug()
		{
			return debugMode != LightingDebugVisualization.None;
		}

		public void EnsureLightingResources(ParticleFluidRenderRegion2D currentMaterialRenderRegion)
		{
			currentCausticRenderRegion = GetScaledRenderRegion(currentMaterialRenderRegion, textureScale);
			if (ShouldRenderCaustics() && (this != null || ShouldRenderCausticDebug()))
			{
				bool renderDirectionalLightField = ShouldRenderDirectionalLightField();
				bool renderPhaseDiffuseLight = ShouldRenderPhaseDiffuseLight();
				bool renderRadianceCascadeLight = ShouldRenderRadianceCascadeLight();
				bool renderSoftLight = renderPhaseDiffuseLight || renderRadianceCascadeLight;
				int causticWidth = currentCausticRenderRegion.PixelWidth;
				int causticHeight = currentCausticRenderRegion.PixelHeight;
				int causticAccumulationCount = causticWidth * causticHeight * 4;
				
				
				ComputeHelper.CreateStructuredBuffer<uint>(ref causticAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<int>(ref causticMotionAccumulationBuffer, causticAccumulationCount);
				ComputeHelper.CreateStructuredBuffer<uint>(ref lightDirectionAccumulationFallbackBuffer, 4);
				ComputeHelper.CreateRenderTexture(ref lightDirectionResultFallbackTexture, 1, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Result Fallback");
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
				ComputeHelper.CreateRenderTexture(ref causticMotionTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilatedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilated");
				ComputeHelper.CreateRenderTexture(ref causticMotionDilationScratchTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilation Scratch");
				if (renderDirectionalLightField)
				{
					ComputeHelper.CreateStructuredBuffer<uint>(ref lightDirectionAccumulationBuffer, causticAccumulationCount);
					ComputeHelper.CreateRenderTexture(ref lightDirectionTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction");
					ComputeHelper.CreateRenderTexture(ref lightDirectionBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Blur");
				}
				else
				{
					ReleaseLightDirectionTextures();
				}
				if (renderSoftLight)
				{
					float softLightScale = phaseDiffuseLightTextureScale;
					int softLightWidth = Mathf.Max(1, Mathf.RoundToInt(causticWidth * softLightScale));
					int softLightHeight = Mathf.Max(1, Mathf.RoundToInt(causticHeight * softLightScale));
					ComputeHelper.CreateRenderTexture(ref softLightTexture0, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 0");
					ComputeHelper.CreateRenderTexture(ref softLightTexture1, softLightWidth, softLightHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Diffuse Light 1");
				}
				else
				{
					ReleasePhaseDiffuseLightTextures();
				}
				if (denoisingEnabled)
				{
					bool historyChanged = ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
					ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
					if (renderDirectionalLightField)
					{
						historyChanged |= ComputeHelper.CreateRenderTexture(ref lightDirectionHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction History");
						ComputeHelper.CreateRenderTexture(ref lightDirectionTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Light Direction Temporal");
					}
					else
					{
						ComputeHelper.Release(lightDirectionHistoryTexture, lightDirectionTemporalTexture);
						lightDirectionHistoryTexture = null;
						lightDirectionTemporalTexture = null;
					}
					clearCausticHistory |= historyChanged;
				}
				else
				{
					ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture, lightDirectionHistoryTexture, lightDirectionTemporalTexture);
					causticHistoryTexture = null;
					causticTemporalTexture = null;
					lightDirectionHistoryTexture = null;
					lightDirectionTemporalTexture = null;
					clearCausticHistory = true;
					hasPreviousCausticCamera = false;
					causticTemporalFrameCount = 0;
				}
			}
			else
			{
				ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer);
				causticAccumulationBuffer = null;
				causticMotionAccumulationBuffer = null;
				ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
				ReleaseOptionalCausticFallbackTextures();
				ReleaseLightDirectionTextures();
				ReleasePhaseDiffuseLightTextures();
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
			}
		}
		
		static ParticleFluidRenderRegion2D GetScaledRenderRegion(ParticleFluidRenderRegion2D source, float scale)
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
	}
}
