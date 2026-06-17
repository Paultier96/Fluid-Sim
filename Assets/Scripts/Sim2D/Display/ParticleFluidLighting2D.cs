using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	public class ParticleFluidLighting2D : MonoBehaviour
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
		Texture materialAlbedoTexture = Texture2D.blackTexture;
		Texture materialNormal0Texture = Texture2D.blackTexture;
		Texture materialNormal1Texture = Texture2D.blackTexture;
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
		
		public Vector3 GetDirectLightingDirection(ParticleDisplay2D display, ParticleFluidLighting2D settings, Vector3 lightDirection)
		{
			if (settings.directionalLightingMode == ParticleFluidLighting2D.DirectionalLightingMode.Direct || !display.sim.useEllipticalBounds)
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
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(settings.phase1Material.indexOfRefraction, 1.0001f));
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
		
		public void ApplyFrameSettings(ParticleDisplay2D display, Camera cam, ParticleFluidRenderRegion2D currentMaterialRenderRegion, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			ApplyTemporalSettings(display, combinedAccumulationTexture, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture);
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
				display,
				cam,
				renderCaustics,
				renderDirectionalLightField,
				renderSoftLight,
				ShouldRenderRadianceCascadeLight(),
				causticTexture,
				lightDirectionTextureForLighting,
				currentMaterialRenderRegion,
				currentCausticRenderRegion
			);
		}
		

		public void ApplySettings(
			ParticleDisplay2D display,
			Camera cam,
			bool renderCaustics,
			bool renderDirectionalLightField,
			bool renderSoftLight,
			bool radianceCascadeSoftLight,
			Texture causticTexture,
			Texture lightDirectionTexture,
			ParticleFluidRenderRegion2D renderRegion,
			ParticleFluidRenderRegion2D causticRegion)
		{
			if (lightingMaterial == null)
			{
				return;
			}

			float analyticBoundaryExpansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);


			Vector3 primaryDirectLightingDirection = GetDirectLightingDirection(display, this, primaryLight.Direction);
			Vector3 secondaryDirectLightingDirection = GetDirectLightingDirection(display, this, secondaryLight.Direction);
			Vector3 tertiaryDirectLightingDirection = GetDirectLightingDirection(display, this, tertiaryLight.Direction);

			materialRenderRegion = renderRegion;
			causticRenderRegion = causticRegion;
			currentZoomScale = display.GetZoomScale(cam);

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
			return LightingDebugMode(this) != LightingDebugVisualization.None;
		}

		public static LightingDebugVisualization LightingDebugMode(ParticleFluidLighting2D settings)
		{
			return settings != null ? settings.debugMode : LightingDebugVisualization.None;
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
		
		public void ClearCausticHistory()
		{
			clearCausticHistory = true;
			hasPreviousCausticCamera = false;
			causticTemporalFrameCount = 0;
		}
		
		public void BindCausticAccumulationTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", causticAccumulationBuffer);
		}

		public void BindCausticMotionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticMotionAccum", causticMotionAccumulationBuffer);
		}

		public void BindLightDirectionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, bool renderDirectionalLightField)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "LightDirectionAccum", renderDirectionalLightField ? lightDirectionAccumulationBuffer : lightDirectionAccumulationFallbackBuffer);
		}
		
		static float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings)
		{
			return Mathf.Max(surface.renderTextureScale * settings.textureScale, 0.0001f);
		}

		static Vector4 GetCausticMultiplier(Color color, float intensity)
		{
			float clampedIntensity = Mathf.Max(intensity, 0f);
			return new Vector4(
				color.r * clampedIntensity,
				color.g * clampedIntensity,
				color.b * clampedIntensity,
				0f
			);
		}
		
		public void BuildCaustics(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Caustics");
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ComputeShader compute = computeShader;
			bool renderDirectionalLightField = ShouldRenderDirectionalLightField();
			bool renderCausticMotion = ShouldRenderCaustics() && (temporalMotionSource == TemporalMotionSource.CausticMotion || LightingDebugMode(this) == LightingDebugVisualization.CausticMotion);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticResolvedTexture.width;
			int height = causticResolvedTexture.height;
			Vector3 lightDirection = primaryLight.Direction;
			Vector3 secondaryLightDirection = secondaryLight.Direction;
			Vector3 tertiaryLightDirection = tertiaryLight.Direction;
			bool primaryLightEnabled = SupportsCausticRaymarch(primaryLight);
			bool secondaryLightEnabled = SupportsCausticRaymarch(secondaryLight);
			bool tertiaryLightEnabled = SupportsCausticRaymarch(tertiaryLight);
			float analyticBoundaryExpansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);

			Vector2 currentWorldCenter = currentCausticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = currentCausticRenderRegion.WorldSize;
			GetCausticRayRange(display, this, width, height, lightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float primaryRayStartOffset, out int primaryRangeRayCount);
			GetCausticRayRange(display, this, width, height, secondaryLightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float secondaryRayStartOffset, out int secondaryRangeRayCount);
			GetCausticRayRange(display, this, width, height, tertiaryLightDirection, currentWorldCenter, currentWorldSize, analyticBoundaryExpansion, out float tertiaryRayStartOffset, out int tertiaryRangeRayCount);
			GetCausticPointRaySpan(display, this.primaryLight, useAnalyticBoundary, out float primaryPointAngleStart, out float primaryPointAngleRange);
			GetCausticPointRaySpan(display, secondaryLight, useAnalyticBoundary, out float secondaryPointAngleStart, out float secondaryPointAngleRange);
			GetCausticPointRaySpan(display, tertiaryLight, useAnalyticBoundary, out float tertiaryPointAngleStart, out float tertiaryPointAngleRange);
			if (primaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				primaryRangeRayCount = GetCausticPointRayCount(primaryLight, currentWorldSize, width, height);
				primaryRayStartOffset = 0f;
			}
			if (secondaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				secondaryRangeRayCount = GetCausticPointRayCount(secondaryLight, currentWorldSize, width, height);
				secondaryRayStartOffset = 0f;
			}
			if (tertiaryLight.type == DirectionalLightSettings.LightType.Point)
			{
				tertiaryRangeRayCount = GetCausticPointRayCount(tertiaryLight, currentWorldSize, width, height);
				tertiaryRayStartOffset = 0f;
			}
			int raysPerPixel = Mathf.Max(1, this.raysPerPixel);
			int maxRayCount = Mathf.Max(1, MaxCausticTraceThreads / raysPerPixel);
			float primaryLightWeight = primaryLightEnabled ? LightSampleWeight(primaryLight) : 0f;
			float secondaryLightWeight = secondaryLightEnabled ? LightSampleWeight(secondaryLight) : 0f;
			float tertiaryLightWeight = tertiaryLightEnabled ? LightSampleWeight(tertiaryLight) : 0f;
			int enabledRangeRayCount = Mathf.Max(
				primaryLightWeight > 0f ? primaryRangeRayCount : 0,
				secondaryLightWeight > 0f ? secondaryRangeRayCount : 0,
				tertiaryLightWeight > 0f ? tertiaryRangeRayCount : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0
				? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount))
				: 1;
			GetLightRayShares(primaryLightWeight, secondaryLightWeight, tertiaryLightWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare);
			int secondarySubRaysPerPixel = secondaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * secondaryShare)
				: 0;
			secondarySubRaysPerPixel = Mathf.Clamp(secondarySubRaysPerPixel, 0, raysPerPixel);
			int tertiarySubRaysPerPixel = tertiaryShare > 0f && raysPerPixel > 1
				? Mathf.RoundToInt(raysPerPixel * tertiaryShare)
				: 0;
			tertiarySubRaysPerPixel = Mathf.Clamp(tertiarySubRaysPerPixel, 0, raysPerPixel - secondarySubRaysPerPixel);
			if (AnyEnabledCausticPointLight(this))
			{
				secondarySubRaysPerPixel = 0;
				tertiarySubRaysPerPixel = 0;
			}
			int primarySubRaysPerPixel = primaryShare > 0f ? Mathf.Max(0, raysPerPixel - secondarySubRaysPerPixel - tertiarySubRaysPerPixel) : 0;
			bool splitBySubRay = secondarySubRaysPerPixel > 0 || tertiarySubRaysPerPixel > 0;
			int secondaryRayBudget = secondaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * secondaryShare) : 0;
			secondaryRayBudget = Mathf.Clamp(secondaryRayBudget, 0, Mathf.Min(totalRayBudget, secondaryRangeRayCount));
			int tertiaryRayBudget = tertiaryShare > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * tertiaryShare) : 0;
			tertiaryRayBudget = Mathf.Clamp(tertiaryRayBudget, 0, Mathf.Min(totalRayBudget - secondaryRayBudget, tertiaryRangeRayCount));
			int primaryRayBudget = primaryShare > 0f
				? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - secondaryRayBudget - tertiaryRayBudget)
				: 0;
			primaryRayBudget = Mathf.Clamp(primaryRayBudget, 0, primaryRangeRayCount);
			float primaryRaySpacing = primaryRangeRayCount > 1 && primaryRayBudget > 1
				? (primaryRangeRayCount - 1f) / (primaryRayBudget - 1f)
				: 1f;
			int secondarySpacingRayCount = splitBySubRay ? totalRayBudget : secondaryRayBudget;
			float secondaryRaySpacing = secondaryRangeRayCount > 1 && secondarySpacingRayCount > 1
				? (secondaryRangeRayCount - 1f) / (secondarySpacingRayCount - 1f)
				: 1f;
			int tertiarySpacingRayCount = splitBySubRay ? totalRayBudget : tertiaryRayBudget;
			float tertiaryRaySpacing = tertiaryRangeRayCount > 1 && tertiarySpacingRayCount > 1
				? (tertiaryRangeRayCount - 1f) / (tertiarySpacingRayCount - 1f)
				: 1f;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", totalRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimaryRayCount", primaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryRayCount", secondaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryRayCount", tertiaryRayBudget);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsPrimarySubRaysPerPixel", primarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondarySubRaysPerPixel", secondarySubRaysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiarySubRaysPerPixel", tertiarySubRaysPerPixel);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayStartOffset", primaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRaySpacing", primaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRayStartOffset", secondaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryRaySpacing", secondaryRaySpacing);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRayStartOffset", tertiaryRayStartOffset);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryRaySpacing", tertiaryRaySpacing);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", raySteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsStepPixels", stepPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassIndexOfRefraction", boundaryMaterial.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsGlassReflectance", boundaryMaterial.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0IndexOfRefraction", phase0Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1IndexOfRefraction", phase1Material.indexOfRefraction);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Reflectance", phase0Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Reflectance", phase1Material.reflectance);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Metallic", phase0Material.metallic);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Metallic", phase1Material.metallic);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionRotation", dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightAngularRadius", lightAngularRadiusDegrees * Mathf.Deg2Rad);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0Absorption", phase0Material.absorption);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1Absorption", phase1Material.absorption);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase0AbsorptionTint", phase0Material.diffuseLightTint);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsPhase1AbsorptionTint", phase1Material.diffuseLightTint);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase0AbsorptionTintBlend", phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsPhase1AbsorptionTintBlend", phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoBrightnessInfluence", absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoSaturationInfluence", absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsDirectionalLightFieldEnabled", renderDirectionalLightField ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceNormalJitterPixels", surfaceNormalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", causticFrameIndex++);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsLightType", (int)primaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightTemperatureKelvin", primaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightDispersionScale", GetSaturationDispersionScale(primaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightPoint", GetPointLightVector(primaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointFalloff", primaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleStart", primaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsLightPointAngleRange", primaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsLightMultiplier", primaryLightWeight > 0f ? GetCausticMultiplier(primaryLight.EffectiveColor, primaryLight.intensity) : Vector4.zero);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightEnabled", secondarySubRaysPerPixel > 0 || secondaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsSecondaryLightType", (int)secondaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightTemperatureKelvin", secondaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightDispersionScale", GetSaturationDispersionScale(secondaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightPoint", GetPointLightVector(secondaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointFalloff", secondaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleStart", secondaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSecondaryLightPointAngleRange", secondaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightDirection", new Vector4(secondaryLightDirection.x, secondaryLightDirection.y, secondaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSecondaryLightMultiplier", GetCausticMultiplier(secondaryLight.EffectiveColor, secondaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightEnabled", tertiarySubRaysPerPixel > 0 || tertiaryRayBudget > 0 ? 1 : 0);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsTertiaryLightType", (int)tertiaryLight.type);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightTemperatureKelvin", tertiaryLight.temperatureKelvin);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightDispersionScale", GetSaturationDispersionScale(tertiaryLight.color));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightPoint", GetPointLightVector(tertiaryLight));
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointFalloff", tertiaryLight.pointFalloff);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleStart", tertiaryPointAngleStart);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTertiaryLightPointAngleRange", tertiaryPointAngleRange);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightDirection", new Vector4(tertiaryLightDirection.x, tertiaryLightDirection.y, tertiaryLightDirection.z, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsTertiaryLightMultiplier", GetCausticMultiplier(tertiaryLight.EffectiveColor, tertiaryLight.intensity));
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && useAnalyticBoundary ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

			BindCausticAccumulationTextures(targetCommandBuffer, compute, clearKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, clearKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, clearKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, clearKernel, width, height);

			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex0", velocityPhase0AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "VelocityTex1", velocityPhase1AccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap2", display.gradientTexture2);
			BindCausticAccumulationTextures(targetCommandBuffer, compute, traceKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, traceKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, traceKernel, renderDirectionalLightField);
			DispatchCausticTrace(targetCommandBuffer, compute, traceKernel, totalRayBudget, raysPerPixel);

			BindCausticAccumulationTextures(targetCommandBuffer, compute, resolveKernel);
			BindCausticMotionTextures(targetCommandBuffer, compute, resolveKernel);
			BindLightDirectionTextures(targetCommandBuffer, compute, resolveKernel, renderDirectionalLightField);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticMotionResult", causticMotionTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "LightDirectionResult", renderDirectionalLightField ? lightDirectionTexture : lightDirectionResultFallbackTexture);
			DispatchCaustics(targetCommandBuffer, compute, resolveKernel, width, height);

			float rayTextureBlurScale = GetRayTextureBlurScale(surface, this);
			float causticBlurRadius = blur * rayTextureBlurScale;
			float directionalLightFieldBlurRadius = directionalLightFieldBlur * rayTextureBlurScale;
			float temporalMotionBlurRadius = temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				causticBlurMaterial.SetFloat("blurRadius", causticBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(causticResolvedTexture, causticBlurTexture, causticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(causticBlurTexture, causticResolvedTexture, causticBlurMaterial);
			}
			if (renderDirectionalLightField && directionalLightFieldBlurRadius > 0.001f)
			{
				lightDirectionBlurMaterial.SetFloat("blurRadius", directionalLightFieldBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(lightDirectionBlurTexture, lightDirectionTexture, lightDirectionBlurMaterial);
			}
			RenderTexture temporalMotionTexture = causticMotionTexture;
			bool useCausticMotion = temporalMaterial != null && temporalMotionSource == TemporalMotionSource.CausticMotion;
			if (useCausticMotion && temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, temporalMotionDilationIterations);
				float motionDilationRadius = temporalMotionDilationRadius * GetRayTextureBlurScale(surface, this) / dilationIterations;
				temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, temporalMaterial, 1);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == causticMotionDilatedTexture ? causticMotionDilationScratchTexture : causticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && causticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == causticMotionDilationScratchTexture
					? causticMotionDilatedTexture
					: causticMotionDilationScratchTexture;
				causticMotionBlurMaterial.SetFloat("blurRadius", temporalMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(temporalMotionTexture, motionBlurScratch, causticMotionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(motionBlurScratch, temporalMotionTexture, causticMotionBlurMaterial);
			}
			//debugMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : causticMotionTexture);

			if (denoisingEnabled && temporalMaterial != null)
			{
				if (clearCausticHistory || !hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionHistoryTexture);
					}
					clearCausticHistory = false;
					causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(previousCausticWorldCenter.x, previousCausticWorldCenter.y, 0f, 0f));
					temporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(previousCausticWorldSize.x, previousCausticWorldSize.y, 0f, 0f));
					temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);
					if (renderDirectionalLightField)
					{
						temporalMaterial.SetTexture("CausticHistoryTex", lightDirectionHistoryTexture);
						temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
						targetCommandBuffer.Blit(lightDirectionTexture, lightDirectionTemporalTexture, temporalMaterial, 0);
						targetCommandBuffer.Blit(lightDirectionTemporalTexture, lightDirectionHistoryTexture);
					}
					causticTemporalFrameCount = nextFrameCount;
				}

				previousCausticWorldCenter = currentWorldCenter;
				previousCausticWorldSize = currentWorldSize;
				hasPreviousCausticCamera = true;
			}
			else
			{
				hasPreviousCausticCamera = false;
				causticTemporalFrameCount = 0;
			}

			if (ShouldRenderRadianceCascadeLight())
			{
				RenderRadianceCascadeLight(display, cam, targetCommandBuffer, surface, this, denoisingEnabled ? causticTemporalTexture : causticResolvedTexture, currentWorldCenter, currentWorldSize, combinedAccumulationTexture);
			}
			else if (ShouldRenderPhaseDiffuseLight())
			{
				RenderPhaseDiffuseLight(display, targetCommandBuffer, surface, this, denoisingEnabled ? causticTemporalTexture : causticResolvedTexture, currentWorldCenter, currentWorldSize, combinedAccumulationTexture);
			}
			targetCommandBuffer.EndSample("Metaballs/Caustics");
		}
		
		void RenderPhaseDiffuseLight(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize,
			RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			ComputeShader compute = settings.phaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int gaussianHorizontalKernel = compute.FindKernel("MaskedGaussianHorizontal");
			int gaussianVerticalKernel = compute.FindKernel("MaskedGaussianVertical");
			int width = settings.softLightTexture0.width;
			int height = settings.softLightTexture0.height;

			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", settings.softLightTexture0);
			DispatchCaustics(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = settings.softLightTexture0;
			RenderTexture target = settings.softLightTexture1;
			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianHorizontalKernel, width, height);

			RenderTexture previousSource = source;
			source = target;
			target = previousSource;

			SetPhaseDiffuseCommonParams(display, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, settings, currentWorldCenter, currentWorldSize, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightWrite", target);
			DispatchCaustics(targetCommandBuffer, compute, gaussianVerticalKernel, width, height);

			previousSource = source;
			source = target;
			target = previousSource;

			//debugMaterial?.SetTexture("SoftLightTex", source);
			SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
		}
		
		void SetPhaseDiffuseCommonParams(ParticleDisplay2D display, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int gaussianHorizontalKernel, int gaussianVerticalKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Vector2 currentWorldCenter, Vector2 currentWorldSize,
			RenderTexture combinedAccumulationTexture )
		{
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "softLightHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", sharpCaustics.width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", sharpCaustics.height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBlendWidth", surface.phaseBlendWidth);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", settings.phaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", settings.phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", settings.phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", 1f);
			float gaussianRadiusScale = ParticleFluidLighting2D.GetRayTextureBlurScale(surface, settings) * Mathf.Max(settings.phaseDiffuseLightTextureScale, 0.0001f);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius0", settings.phase0Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius1", settings.phase1Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", MetaballRenderer2D.GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}
		
		void DispatchCaustics(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchCausticTrace(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), MaxCausticTraceThreads);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)CausticTraceThreadGroupSize), 1, 1);
			}
		}
		
		void GetCausticPointRaySpan(ParticleDisplay2D display, ParticleFluidLighting2D.DirectionalLightSettings light, bool useAnalyticBoundary, out float angleStart, out float angleRange)
		{
			angleStart = 0f;
			angleRange = TwoPi;
			if (display == null
			    || light == null
			    || light.type != ParticleFluidLighting2D.DirectionalLightSettings.LightType.Point
			    || !useAnalyticBoundary
			    || !display.sim.useEllipticalBounds)
			{
				return;
			}

			Vector2 point = light.pointPosition;
			float expansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			float cutY = display.sim.obstacleY - expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f || IsInsideAnalyticBoundary(point, center, radii, cutY))
			{
				return;
			}

			int angleCount = 0;
			for (int i = 0; i < PointLightBoundaryEllipseSamples; i++)
			{
				float t = i / (float)PointLightBoundaryEllipseSamples * TwoPi;
				Vector2 boundaryPoint = center + new Vector2(Mathf.Cos(t) * radii.x, Mathf.Sin(t) * radii.y);
				if (boundaryPoint.y >= cutY)
				{
					AddPointLightBoundaryAngle(point, boundaryPoint, ref angleCount);
				}
			}

			float cutRelY = cutY - center.y;
			if (Mathf.Abs(cutRelY) <= radii.y)
			{
				float cutHalfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY / (radii.y * radii.y)));
				for (int i = 0; i < PointLightBoundaryCutSamples; i++)
				{
					float t = PointLightBoundaryCutSamples > 1 ? i / (float)(PointLightBoundaryCutSamples - 1) : 0.5f;
					AddPointLightBoundaryAngle(point, new Vector2(center.x + Mathf.Lerp(-cutHalfWidth, cutHalfWidth, t), cutY), ref angleCount);
				}
			}

			if (angleCount < 2)
			{
				return;
			}

			System.Array.Sort(pointLightBoundaryAngles, 0, angleCount);
			float largestGap = -1f;
			int largestGapIndex = 0;
			for (int i = 0; i < angleCount; i++)
			{
				float current = pointLightBoundaryAngles[i];
				float next = i == angleCount - 1 ? pointLightBoundaryAngles[0] + TwoPi : pointLightBoundaryAngles[i + 1];
				float gap = next - current;
				if (gap > largestGap)
				{
					largestGap = gap;
					largestGapIndex = i;
				}
			}

			float padding = 2f * Mathf.Deg2Rad;
			angleStart = Mathf.Repeat(pointLightBoundaryAngles[(largestGapIndex + 1) % angleCount] - padding, TwoPi);
			angleRange = Mathf.Clamp(TwoPi - largestGap + padding * 2f, 0.0001f, TwoPi);
		}

		void AddPointLightBoundaryAngle(Vector2 lightPoint, Vector2 boundaryPoint, ref int angleCount)
		{
			if (angleCount >= pointLightBoundaryAngles.Length)
			{
				return;
			}

			Vector2 delta = boundaryPoint - lightPoint;
			if (delta.sqrMagnitude <= 0.000001f)
			{
				return;
			}

			pointLightBoundaryAngles[angleCount++] = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x), TwoPi);
		}

		static bool IsInsideAnalyticBoundary(Vector2 point, Vector2 center, Vector2 radii, float cutY)
		{
			if (point.y < cutY)
			{
				return false;
			}

			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			return ellipseValue <= 1f;
		}
		
		void ApplyTemporalSettings(ParticleDisplay2D display, RenderTexture combinedAccumulationTexture, RenderTexture velocityPhase0AccumulationTexture, RenderTexture velocityPhase1AccumulationTexture)
		{
			if (temporalMaterial == null)
			{
				return;
			}
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			temporalMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture);
			temporalMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			temporalMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			temporalMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			temporalMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, this));
			temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : temporalHistoryWeight);
			temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", temporalHistoryClampStrength);
			temporalMaterial.SetFloat("causticTemporalClampRejection", temporalClampRejection);
			temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", temporalRejectedSpatialFilter);
			temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
		}

		public float GetCausticDebugExposure()
		{
			float exposure = SupportsCausticRaymarch(primaryLight) ? Luminance(primaryLight.EffectiveColor) * Mathf.Max(primaryLight.intensity, 0f) : 0f;
			if (SupportsCausticRaymarch(secondaryLight))
			{
				exposure += Luminance(secondaryLight.EffectiveColor) * Mathf.Max(secondaryLight.intensity, 0f);
			}
			if (SupportsCausticRaymarch(tertiaryLight))
			{
				exposure += Luminance(tertiaryLight.EffectiveColor) * Mathf.Max(tertiaryLight.intensity, 0f);
			}
			return Mathf.Max(exposure, 0.0001f);
		}

		static float Luminance(Color colour)
		{
			return colour.r * 0.2126f + colour.g * 0.7152f + colour.b * 0.0722f;
		}

		static float GetSaturationDispersionScale(Color color)
		{
			float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
			float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}

		static void GetLightRayShares(float primaryWeight, float secondaryWeight, float tertiaryWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare)
		{
			float totalWeight = primaryWeight + secondaryWeight + tertiaryWeight;
			if (totalWeight > 0.0001f)
			{
				primaryShare = primaryWeight / totalWeight;
				secondaryShare = secondaryWeight / totalWeight;
				tertiaryShare = tertiaryWeight / totalWeight;
				return;
			}

			primaryShare = 0f;
			secondaryShare = 0f;
			tertiaryShare = 0f;
		}

		static float LightSampleWeight(DirectionalLightSettings light)
		{
			if (!SupportsCausticRaymarch(light))
			{
				return 0f;
			}
			return Mathf.Max(0f, Luminance(light.EffectiveColor) * light.intensity * light.sampleBias);
		}

		static bool SupportsCausticRaymarch(DirectionalLightSettings light)
		{
			return light != null
			       && light.enabled
			       && light.intensity > 0f;
		}

		static bool AnyEnabledCausticPointLight(ParticleFluidLighting2D settings)
		{
			return IsWeightedPointLight(settings.primaryLight)
			       || IsWeightedPointLight(settings.secondaryLight)
			       || IsWeightedPointLight(settings.tertiaryLight);
		}

		static bool IsWeightedPointLight(DirectionalLightSettings light)
		{
			return light != null
			       && light.type == DirectionalLightSettings.LightType.Point
			       && LightSampleWeight(light) > 0f;
		}

		void GetCausticRayRange(ParticleDisplay2D display, ParticleFluidLighting2D settings, int width, int height, Vector3 lightDirection, Vector2 worldCenter, Vector2 worldSize, float analyticBoundaryExpansion, out float startOffset, out int rayCount)
		{
			Vector2 lightXY = new Vector2(-lightDirection.x, -lightDirection.y);
			Vector2 rayDir = lightXY.sqrMagnitude > 0.0001f ? lightXY.normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(width * width + height * height);
			float screenMinOffset = -fullSpan * 0.5f;
			float screenMaxOffset = fullSpan * 0.5f;
			startOffset = screenMinOffset;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!display.sim.useEllipticalBounds || !settings.useAnalyticBoundary)
			{
				return;
			}

			float minOffset = float.PositiveInfinity;
			float maxOffset = float.NegativeInfinity;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * analyticBoundaryExpansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return;
			}

			for (int i = 0; i < 128; i++)
			{
				float angle = i * Mathf.PI * 2f / 128f;
				Vector2 world = display.sim.ellipseBoundsCenter + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
				if (world.y >= display.sim.obstacleY - analyticBoundaryExpansion)
				{
					IncludeCausticLaunchPoint(world, worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				}
			}

			float expandedObstacleY = display.sim.obstacleY - analyticBoundaryExpansion;
			float cutRelY = (expandedObstacleY - display.sim.ellipseBoundsCenter.y) / radii.y;
			if (Mathf.Abs(cutRelY) <= 1f)
			{
				float cutX = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY));
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x - cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x + cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
			}

			if (float.IsNaN(minOffset) || float.IsInfinity(minOffset) || float.IsNaN(maxOffset) || float.IsInfinity(maxOffset))
			{
				return;
			}

			float angularPadding = Mathf.Sin(settings.lightAngularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(settings.stepPixels * 4f + angularPadding, 2f);
			float clippedMinOffset = Mathf.Max(minOffset - padding, screenMinOffset);
			float clippedMaxOffset = Mathf.Min(maxOffset + padding, screenMaxOffset);
			if (clippedMaxOffset <= clippedMinOffset)
			{
				rayCount = 0;
				return;
			}

			startOffset = Mathf.Floor(clippedMinOffset);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(clippedMaxOffset - startOffset));
		}

		void IncludeCausticLaunchPoint(Vector2 world, Vector2 worldCenter, Vector2 worldSize, int width, int height, Vector2 tangent, ref float minOffset, ref float maxOffset)
		{
			Vector2 uv = new Vector2(
				(world.x - worldCenter.x) / Mathf.Max(worldSize.x, 0.0001f) + 0.5f,
				(world.y - worldCenter.y) / Mathf.Max(worldSize.y, 0.0001f) + 0.5f
			);
			Vector2 pixel = new Vector2(uv.x * width, uv.y * height);
			Vector2 centredPixel = pixel - new Vector2(width, height) * 0.5f;
			float offset = Vector2.Dot(centredPixel, tangent);
			minOffset = Mathf.Min(minOffset, offset);
			maxOffset = Mathf.Max(maxOffset, offset);
		}
		
		static int GetCausticPointRayCount(DirectionalLightSettings light, Vector2 worldSize, int width, int height)
		{
			if (light == null)
			{
				return 0;
			}

			float pixelsPerWorldUnit = Mathf.Max(
				width / Mathf.Max(worldSize.x, 0.0001f),
				height / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(light.pointRange, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2 * radiusPixels));
		}
		
		void RenderRadianceCascadeLight(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Radiance Cascades");
			int width = settings.softLightTexture0.width;
			int height = settings.softLightTexture0.height;
			int cascadeCount = Mathf.Clamp(settings.radianceCascadeCount, 1, 6);
			RenderTexture source = settings.softLightTexture0;
			RenderTexture target = settings.softLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeCommonParams(display, cam, targetCommandBuffer, surface, settings, sharpCaustics, currentWorldCenter, currentWorldSize, width, height, combinedAccumulationTexture);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, radianceCascadeMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			//debugMaterial?.SetTexture("SoftLightTex", source);
			SetSoftLightTexture(source);
			targetCommandBuffer.EndSample("Metaballs/Radiance Cascades");
		}
		
		void SetRadianceCascadeCommonParams(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, ParticleFluidLighting2D settings, Texture sharpCaustics, Vector2 currentWorldCenter, Vector2 currentWorldSize, int width, int height, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics);
			targetCommandBuffer.SetGlobalTexture("CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("_Aspect", new Vector4(1f, width / Mathf.Max(height, 1f), 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(settings.radianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(settings.radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(settings.radianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", settings.radianceCascadeIntensity);
			targetCommandBuffer.SetGlobalInt("radianceCascadeAbsorption", settings.radianceCascadeAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetGlobalFloat("edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetGlobalFloat("phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthA", settings.phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthB", settings.phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("lightIntensity", 1f);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", settings.phase0Material.absorption);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1Absorption", settings.phase1Material.absorption);
			targetCommandBuffer.SetGlobalVector("causticsPhase0AbsorptionTint", settings.phase0Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalVector("causticsPhase1AbsorptionTint", settings.phase1Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0AbsorptionTintBlend", settings.phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1AbsorptionTintBlend", settings.phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", settings.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", settings.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetGlobalInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetGlobalFloat("analyticBoundaryExpansion", MetaballRenderer2D.GetAnalyticBoundaryExpansion(display));
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}
		
		public void Release()
		{
			if (lightingMaterial != null)
			{
				DestroyImmediate(lightingMaterial);
				lightingMaterial = null;
			}

			if (colorBleedMaterial != null)
			{
				DestroyImmediate(colorBleedMaterial);
				colorBleedMaterial = null;
			}
			if (radianceCascadeMaterial != null)
			{
				DestroyImmediate(radianceCascadeMaterial);
				radianceCascadeMaterial = null;
			}
			if (temporalMaterial != null)
			{
				DestroyImmediate(temporalMaterial);
				temporalMaterial = null;
			}
			if (causticBlurMaterial != null)
			{
				DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}

			if (causticMotionBlurMaterial != null)
			{
				DestroyImmediate(causticMotionBlurMaterial);
				causticMotionBlurMaterial = null;
			}

			if (lightDirectionBlurMaterial != null)
			{
				DestroyImmediate(lightDirectionBlurMaterial);
				lightDirectionBlurMaterial = null;
			}
			
			ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer);
			causticAccumulationBuffer = null;
			causticMotionAccumulationBuffer = null;
			ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
			ReleaseOptionalCausticFallbackTextures();
			ReleaseLightDirectionTextures();
			ReleasePhaseDiffuseLightTextures();
		}
		
		void ReleasePhaseDiffuseLightTextures()
		{
			ComputeHelper.Release(softLightTexture0, softLightTexture1);
			softLightTexture0 = null;
			softLightTexture1 = null;
		}

		void ReleaseLightDirectionTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationBuffer);
			lightDirectionAccumulationBuffer = null;
			ComputeHelper.Release(lightDirectionTexture, lightDirectionBlurTexture, lightDirectionHistoryTexture, lightDirectionTemporalTexture);
			lightDirectionTexture = null;
			lightDirectionBlurTexture = null;
			lightDirectionHistoryTexture = null;
			lightDirectionTemporalTexture = null;
		}

		void ReleaseOptionalCausticFallbackTextures()
		{
			ComputeHelper.Release(lightDirectionAccumulationFallbackBuffer);
			lightDirectionAccumulationFallbackBuffer = null;
			ComputeHelper.Release(lightDirectionResultFallbackTexture);
			lightDirectionResultFallbackTexture = null;
		}
	}
}
