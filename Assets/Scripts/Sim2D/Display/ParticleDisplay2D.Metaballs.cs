using System;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleDisplay2D
	{
		[Header("Metaballs")]
		public MetaballSettings metaballs = new MetaballSettings();

		MetaballRenderer2D metaballRenderer;
		internal MetaballRenderer2D MetaballRenderer => metaballRenderer ??= new MetaballRenderer2D();

		const float BlurReferenceOrthoSize = 15f;

		internal float EffectiveConfiguredBlurRadius => metaballs.blurRadius * ParticleResolutionLengthScale;

		float ParticleResolutionLengthScale
		{
			get
			{
				float resolutionFactor = Mathf.Max(0.0001f, sim.particleResolutionFactor);
				return 1f / Mathf.Sqrt(resolutionFactor);
			}
		}

		internal float GetEffectiveBlurRadius(Camera cam)
		{
			return EffectiveConfiguredBlurRadius * GetZoomScale(cam) * Mathf.Max(metaballs.renderTextureScale, 0.0001f);
		}

		internal float GetEffectiveMotionBlurRadius(Camera cam)
		{
			float motionBlurRadius = metaballs.motionBlurRadius * ParticleResolutionLengthScale;
			return motionBlurRadius * GetZoomScale(cam) * Mathf.Max(metaballs.renderTextureScale, 0.0001f);
		}

		internal float GetZoomScale(Camera cam)
		{
			if (!cam.orthographic)
			{
				return 1f;
			}

			return BlurReferenceOrthoSize / Mathf.Max(cam.orthographicSize, 0.0001f);
		}

		internal float GetEffectiveNormalStrength(float referenceBlurRadius)
		{
			float baseStrength = Mathf.Max(0f, metaballs.normalStrength);
			float compensation = Mathf.Max(0f, metaballs.normalBlurCompensation);
			if (compensation <= 0f)
			{
				return baseStrength;
			}

			float blurScale = Mathf.Max(0f, referenceBlurRadius) / 6f;
			return baseStrength * Mathf.Max(1f, Mathf.Pow(blurScale, compensation));
		}

		[Serializable]
		public sealed class PhaseMaterialSettings
		{
			[Tooltip("Index of refraction used when bending lighting rays across visible phase boundaries.")]
			[Min(1.0001f)] public float indexOfRefraction = 1.333f;
			[Tooltip("Exponential energy loss per world unit travelled through this phase.")]
			[Min(0f)] public float absorption = 0f;
			[Tooltip("Blends ray absorption colour from the phase albedo gradient toward Diffuse Light Tint. 0 uses the current albedo-based absorption; 1 uses Diffuse Light Tint.")]
			[Range(0f, 1f)] public float absorptionDiffuseTintBlend = 0f;
			[Tooltip("Minimum reflection probability at this phase's surface before Fresnel is applied. 0 uses Fresnel only; higher values make the phase more reflective at all angles.")]
			[Range(0f, 1f)] public float reflectance = 0f;
			[Tooltip("Surface roughness used by the metaball direct lighting. Lower values make smaller, sharper highlights; higher values make broader, dimmer highlights.")]
			[Range(0.02f, 1f)] public float roughness = 0.35f;
			[Tooltip("Tints reflected rays toward this material's colour. 0 keeps reflections neutral, 1 fully applies the material colour to reflected light.")]
			[Range(0f, 1f)] public float metallic = 0f;
			[Tooltip("Strength of cheap screen-space reflections from neighbouring metaballs on this phase.")]
			[Min(0f)] public float screenSpaceReflectionStrength = 0f;
			[Tooltip("Initial fraction of sharp raymarched light injected into the phase-aware diffuse lighting pass.")]
			[Min(0f)] public float diffuseScatterStrength = 0f;
			[Tooltip("Per-iteration diffusion amount for the phase-aware diffuse lighting pass.")]
			[Range(0f, 1f)] public float diffuseDiffusionRate = 0.2f;
			[Tooltip("Tint applied to this phase's phase-aware diffuse lighting.")]
			[ColorUsage(false, true)] public Color diffuseLightTint = Color.white;
			[Tooltip("Blends phase-aware diffuse lighting tint from Diffuse Light Tint toward the local phase albedo gradient. 0 uses Diffuse Light Tint; 1 uses albedo.")]
			[Range(0f, 1f)] public float diffuseAlbedoTintBlend = 0f;

			public PhaseMaterialSettings()
			{
			}

			public PhaseMaterialSettings(float indexOfRefraction)
			{
				this.indexOfRefraction = indexOfRefraction;
			}
		}

		public enum TemporalMotionSource
		{
			Static,
			ParticleMotion,
			CausticMotion
		}

		[Serializable]
		public sealed class MetaballSettings
		{
			public enum AnalyticBoundaryLightingMode
			{
				Off,
				SimpleRefractAtAnalyticBoundary,
				FullDirectionalLightField
			}

			[Header("Shaders")]
			[Tooltip("Shader that blits the blurred accumulation texture onto the camera, applying the density threshold and colour lookup.")]
			public Shader compositeShader;
			[Tooltip("Shader used for the separable Gaussian blur applied to the accumulation texture.")]
			public Shader blurShader;
			[Tooltip("Compute shader used for the optional phase-aware diffuse caustics/SSS lighting pass.")]
			public ComputeShader phaseDiffuseLightCompute;

			[Header("Shape - Surface")]
			[Tooltip("Resolution of the metaball render textures relative to the screen. Lower values improve performance at the cost of sharpness.")]
			[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
			[Tooltip("Radius in pixels at resolution factor 1 of the Gaussian blur. Larger values make particles merge at greater distances.")]
			[Min(0)] public float blurRadius = 6;
			[Tooltip("Blurred density value at which the fluid surface appears. Increase to shrink the visible fluid; decrease to expand it.")]
			[Min(0)] public float densityThreshold = 0.18f;
			[Tooltip("Width of the density falloff around the surface threshold. Larger values give a softer, more transparent edge. Clamped so the fade never starts below zero density.")]
			[Min(0.0001f)] public float edgeSoftness = 0.06f;

			[Header("Shape - Phase Boundary")]
			[Tooltip("Screen-space width in pixels for anti-aliased blending between fluid phases.")]
			[Min(0.0001f)] public float phaseBlendWidth = 1f;
			[Tooltip("Render-only phase boundary bias. 0 is neutral, positive values make phase 0 visually expand, negative values make phase 1 expand.")]
			[Range(-0.99f, 0.99f)] public float phase0RenderBias = 0f;
			[Tooltip("How strongly phase boundary bias redistributes normal strength. The compressed phase is boosted strongly while the visually expanded phase is weakened mildly.")]
			[Range(0f, 10f)] public float phaseBiasNormalStrength = 0.5f;

			[Header("Shape - Particle Kernel")]
			[Tooltip("Steepness of each particle's density kernel. Higher values make particles contribute a tighter, more localised density spike.")]
			[Min(0.01f)] public float sharpness = 3.5f;
			[Tooltip("Uniform scale applied to each particle's density contribution. Increase if particles are too sparse to merge.")]
			[Min(0)] public float intensity = 1.0f;

			[Header("Lighting - Normals")]
			[Tooltip("Multiplier applied to reconstructed normal XY before rebuilding Z. Higher values make blurred normals look steeper.")]
			[Min(0f)] public float normalStrength = 1f;
			[Tooltip("Exponent used to increase normal strength with effective blur radius. 0 disables automatic compensation, 1 is linear.")]
			[Min(0f)] public float normalBlurCompensation = 0.5f;

			[Header("Light")]
			[Tooltip("Horizontal screen/world angle of the light direction in degrees.")]
			public float lightAzimuthDegrees = 122.5f;
			[Tooltip("Vertical angle of the light direction in degrees. 0 lies in the 2D plane, 90 points toward the camera.")]
			[Range(0, 89f)] public float lightElevationDegrees = 50.3f;
			[Tooltip("Colour temperature of the directional light in Kelvin. 6500 is neutral daylight; lower values are warmer, higher values are cooler.")]
			[Range(1000f, 20000f)] public float lightTemperatureKelvin = 6500f;
			[Tooltip("Colour of the directional light used to shade particles in normal rendering mode.")]
			[ColorUsage(false, true)] public Color lightColor = Color.white;
			[Tooltip("Unlit colour multiplier. Increase if shadowed particles are too dark.")]
			[Range(0f, 1f)] public float ambientLight = 0.65f;
			[Tooltip("Intensity of the directional light used by diffuse, specular, transmission, and glow lighting.")]
			[Min(0f)] public float lightIntensity = 0.45f;
			[Tooltip("Cheaply bends the direct metaball lighting direction once through the analytic boundary normal nearest the light direction. This approximates the broad highlight rotation caused by the boundary material IOR.")]
			public bool refractDirectLightAtAnalyticBoundary = false;

			[Header("Lighting - Fresnel")]
			[Tooltip("Colour added at grazing view angles to fake transparent liquid edges.")]
			[ColorUsage(false, true)] public Color fresnelColor = new Color(0.75f, 0.9f, 1f, 1f);
			[Tooltip("Strength of the Fresnel edge glow.")]
			[Min(0f)] public float fresnelIntensity = 0.25f;
			[Tooltip("Fresnel exponent. Higher values concentrate the glow closer to grazing angles.")]
			[Min(0.1f)] public float fresnelPower = 3f;

			[Header("Lighting - Screen-Space Refraction And Reflections")]
			[Tooltip("Screen-space UV offset strength for refracting the blurred colour data outward from the normal. This is separate from raymarched refraction. Alpha and phase remain unwarped.")]
			[Min(0)]public float refractionStrength = 0.01f;
			[Tooltip("Density distance over which screen-space refraction fades in from the visible edge. Higher values push refraction farther inward.")]
			[Min(0)] public float refractionEdgeFade = 0.05f;
			[Tooltip("Allows screen-space refraction to sample colours from the other fluid phase. Disable to preserve sharp same-phase refraction.")]
			public bool screenSpaceRefractionCanCrossPhases = false;
			[Tooltip("Reflection lookup distance in metaball texture pixels. Higher values let blobs reflect farther-away neighbours.")]
			[Min(0f)] public float screenSpaceReflectionDistance = 24f;
			[Tooltip("Edge mask exponent for screen-space reflections. Higher values keep reflections tighter to side-facing normals.")]
			[Min(0.1f)] public float screenSpaceReflectionEdgePower = 1.5f;

			[Header("Lighting - Rim And Transmission")]
			[Tooltip("Screen/normal-space direction for the one-sided liquid-glass rim glow.")]
			public Vector2 glowDirection = new Vector2(-0.75f, -0.45f);
			[Tooltip("Colour of the one-sided liquid-glass rim glow.")]
			[ColorUsage(false, true)] public Color glowColor = Color.white;
			[Tooltip("Strength of the one-sided liquid-glass rim glow.")]
			[Min(0f)] public float glowIntensity = 0.35f;
			[Tooltip("Directional glow exponent. Higher values make the glow narrower along its chosen side.")]
			[Min(0.1f)] public float glowPower = 1.5f;
			[Tooltip("Strength of the fake transmission/backlight term.")]
			[Min(0f)] public float transmissionIntensity = 0.2f;
			[Tooltip("Transmission exponent. Higher values make transmission more directional.")]
			[Min(0.1f)] public float transmissionPower = 2f;
			[Tooltip("Darkens thin/edge regions to fake inner shadow and liquid thickness.")]
			[Range(0f, 1f)] public float edgeDarkening = 0.2f;
			[Tooltip("Edge darkening exponent. Higher values keep the darkening tighter to the edge.")]
			[Min(0.1f)] public float edgeDarkeningPower = 2f;

			[Header("Iridescence")]
			[Tooltip("Strength of fake thin-film iridescence in normal metaball rendering.")]
			[Min(0f)] public float iridescenceIntensity = 0f;
			[Tooltip("Number of hue cycles across the iridescence phase. Higher values make tighter rainbow bands.")]
			[Min(0f)] public float iridescenceScale = 2.0f;

			[Header("Ghost Boundary Normals")]
			[Tooltip("Strength of the analytic ellipse/cut-boundary normals in the metaball composite. Values above 1 make the boundary normal ramp steeper; negative values flip the direction.")]
			[Range(-4f, 4f)] public float ghostBoundaryNormalStrength = 1f;
			[Tooltip("World-space expansion applied to the analytic boundary. 0 uses the original, non-expanded analytic boundary.")]
			[Min(0f)] public float analyticBoundaryPadding = 0.175f;

			[Header("Raymarched Lighting - Setup")]
			[Tooltip("Compute shader used to raymarch the metaball density field and accumulate screen-space lighting.")]
			public ComputeShader computeShader;
			[Tooltip("Adds low-resolution screen-space raymarched lighting generated from the metaball density iso-surface.")]
			public bool enabled;
			[Tooltip("Resolution of the raymarched lighting textures relative to the metaball render textures.")]
			[Range(0.125f, 1f)] public float textureScale = 0.5f;

			[Header("Phase Materials")]
			public PhaseMaterialSettings phase0Material = new PhaseMaterialSettings(1.442f);
			public PhaseMaterialSettings phase1Material = new PhaseMaterialSettings(1.333f);
			public PhaseMaterialSettings boundaryMaterial = new PhaseMaterialSettings(1.516f);

			[Header("Raymarched Lighting - Refraction")]
			[Tooltip("Multiplier for Fresnel reflection at phase surfaces. 0 disables stochastic reflections, 1 is physical Schlick Fresnel, higher values exaggerate internal reflections.")]
			[Min(0f)] public float fresnelStrength = 1f;
			[Tooltip("Uses Fresnel as a probability to randomly reflect lighting rays at surfaces instead of always transmitting one refracted ray.")]
			public bool stochasticReflection = false;
			[Tooltip("Relative IOR spread used by stochastic spectral raymarching. 0.02 means red/blue use roughly -/+2% IOR.")]
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
			[HideInInspector] public float stepPixels = 1.0f;
			[Tooltip("Brightness deposited along ray paths. Keep at 0 to hide debug-visible ray paths.")]
			[Min(0f)] public float rayBrightness = 0.05f;
			[Tooltip("Launches only every Nth ray for easier debugging. 1 uses every ray.")]
			[Min(1)] public int rayStride = 1;
			[Tooltip("Launches multiple rays per source pixel for denser supersampling. Cost scales roughly linearly.")]
			[Range(1, 64)] public int raysPerPixel = 1;
			[Tooltip("Samples the medium colour every N ray steps while absorption is active. 1 samples every step; higher values are cheaper but preserve less interior colour variation.")]
			[Min(1)] public int colourSampleStride = 8;
			[Tooltip("Small full-resolution pixel blur applied to the resolved raymarched lighting texture to reduce atomic splat noise. Internally scaled by render texture scale and lighting texture scale.")]
			[Min(0f)] public float blur = 1.5f;
			[Tooltip("Accumulates average ray direction into an extra texture and uses it for local diffuse/specular lighting direction. Disable to use the global light direction only.")]
			public bool directionalLightFieldEnabled = false;
			[Tooltip("Full-resolution pixel blur radius for the directional light-field texture. Higher values reduce specular noise but make local light direction less precise.")]
			[Min(0f)] public float directionalLightFieldBlur = 1.5f;
			[Tooltip("Enables a separate phase-aware diffuse lighting pass derived from the sharp raymarched lighting.")]
			public bool phaseDiffuseLightEnabled = false;
			[Tooltip("Resolution of the phase-aware diffuse lighting textures relative to the raymarched lighting texture.")]
			[Range(0.25f, 1f)] public float phaseDiffuseLightTextureScale = 0.5f;
			[Tooltip("Number of Jacobi diffusion iterations for phase-aware diffuse lighting.")]
			[Range(1, 40)] public int phaseDiffuseLightIterations = 16;
			[Tooltip("How strongly phase boundaries block phase-aware diffuse lighting. Higher values keep light inside each phase.")]
			[Min(0f)] public float phaseDiffuseLightBoundarySharpness = 12f;
			[Header("Raymarched Lighting - Temporal Smoothing")]
			[Tooltip("Blends raymarched lighting with the previous frame to reduce flicker.")]
			public bool temporalEnabled = false;
			[Tooltip("Previous-frame weight used by temporal blending. 0 uses only current frame; 0.55 means current * 0.45 + previous * 0.55.")]
			[Range(0f, 0.99f)] public float temporalHistoryWeight = 0.95f;
			[Tooltip("Motion source used to reproject temporal raymarched lighting history.")]
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

			public Vector3 LightDirection
			{
				get
				{
					float azimuth = lightAzimuthDegrees * Mathf.Deg2Rad;
					float elevation = lightElevationDegrees * Mathf.Deg2Rad;
					float planarLength = Mathf.Cos(elevation);
					return new Vector3(
						Mathf.Cos(azimuth) * planarLength,
						Mathf.Sin(azimuth) * planarLength,
						Mathf.Sin(elevation)
					).normalized;
				}
			}

			public Color EffectiveLightColor
			{
				get
				{
					Color kelvinColor = KelvinToRgb(lightTemperatureKelvin);
					return new Color(
						lightColor.r * kelvinColor.r,
						lightColor.g * kelvinColor.g,
						lightColor.b * kelvinColor.b,
						lightColor.a
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
	}
}
