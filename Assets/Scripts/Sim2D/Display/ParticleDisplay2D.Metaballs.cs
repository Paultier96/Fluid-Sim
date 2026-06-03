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
		public sealed class MetaballSettings
		{
			[Header("Shaders")]
			[Tooltip("Shader that blits the blurred accumulation texture onto the camera, applying the density threshold and colour lookup.")]
			public Shader compositeShader;
			[Tooltip("Shader used for the separable Gaussian blur applied to the accumulation texture.")]
			public Shader blurShader;

			[Header("Shape")]
			[Tooltip("Resolution of the metaball render textures relative to the screen. Lower values improve performance at the cost of sharpness.")]
			[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
			[Tooltip("Radius in pixels at resolution factor 1 of the Gaussian blur. Larger values make particles merge at greater distances.")]
			[Min(0)] public float blurRadius = 6;
			[Tooltip("Blurred density value at which the fluid surface appears. Increase to shrink the visible fluid; decrease to expand it.")]
			[Min(0)] public float densityThreshold = 0.18f;
			[Tooltip("Width of the density falloff around the surface threshold. Larger values give a softer, more transparent edge. Clamped so the fade never starts below zero density.")]
			[Min(0.0001f)] public float edgeSoftness = 0.06f;
			[Tooltip("Screen-space width in pixels for anti-aliased blending between fluid phases.")]
			[Min(0.0001f)] public float phaseBlendWidth = 1f;
			[Tooltip("Render-only phase boundary bias. 0 is neutral, positive values make phase 0 visually expand, negative values make phase 1 expand.")]
			[Range(-0.99f, 0.99f)] public float phase0RenderBias = 0f;
			[Tooltip("How strongly phase boundary bias redistributes normal strength. The compressed phase is boosted strongly while the visually expanded phase is weakened mildly.")]
			[Range(0f, 10f)] public float phaseBiasNormalStrength = 0.5f;
			[Tooltip("Steepness of each particle's density kernel. Higher values make particles contribute a tighter, more localised density spike.")]
			[Min(0.01f)] public float sharpness = 3.5f;
			[Tooltip("Uniform scale applied to each particle's density contribution. Increase if particles are too sparse to merge.")]
			[Min(0)] public float intensity = 1.0f;
			[Tooltip("Screen-space dithering strength used by the metaball composite shader to reduce colour banding.")]
			[Min(0f)] public float ditherStrength = 1.0f / 255.0f;

			[Header("Lighting")]
			[Tooltip("Horizontal screen/world angle of the light direction in degrees.")]
			public float lightAzimuthDegrees = 122.5f;
			[Tooltip("Vertical angle of the light direction in degrees. 0 lies in the 2D plane, 90 points toward the camera.")]
			[Range(-89f, 89f)] public float lightElevationDegrees = 50.3f;
			[Tooltip("Colour of the directional light used to shade particles in normal rendering mode.")]
			[ColorUsage(false, true)] public Color lightColor = Color.white;
			[Tooltip("Unlit colour multiplier. Increase if shadowed particles are too dark.")]
			[Range(0f, 1f)] public float ambientLight = 0.65f;
			[Tooltip("Directional light strength applied from each particle's reconstructed normal.")]
			[Min(0f)] public float directionalLightIntensity = 0.45f;
			[Tooltip("Multiplier applied to reconstructed normal XY before rebuilding Z. Higher values make blurred normals look steeper.")]
			[Min(0f)] public float normalStrength = 1f;
			[Tooltip("Exponent used to increase normal strength with effective blur radius. 0 disables automatic compensation, 1 is linear.")]
			[Min(0f)] public float normalBlurCompensation = 0.5f;
			[Tooltip("Colour of the specular highlight used in normal rendering mode.")]
			[ColorUsage(false, true)] public Color specularColor = Color.white;
			[Tooltip("Specular highlight strength applied from each particle's reconstructed normal.")]
			[Min(0f)] public float specularIntensity = 0.25f;
			[Tooltip("Specular exponent. Higher values make highlights smaller and sharper.")]
			[Min(1f)] public float specularPower = 24f;
			[Tooltip("Colour added at grazing view angles to fake transparent liquid edges.")]
			[ColorUsage(false, true)] public Color fresnelColor = new Color(0.75f, 0.9f, 1f, 1f);
			[Tooltip("Strength of the Fresnel edge glow.")]
			[Min(0f)] public float fresnelIntensity = 0.25f;
			[Tooltip("Fresnel exponent. Higher values concentrate the glow closer to grazing angles.")]
			[Min(0.1f)] public float fresnelPower = 3f;
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
			[Tooltip("Warm colour added to phase 0 to fake wax subsurface scattering.")]
			[ColorUsage(false, true)] public Color subsurfaceColor = new Color(1.0f, 0.45f, 0.18f, 1f);
			[Tooltip("Strength of the phase 0 fake subsurface scattering term.")]
			[Min(0f)] public float subsurfaceIntensity = 0f;
			[Tooltip("Directional exponent for subsurface backscatter. Higher values make it more light-direction dependent.")]
			[Min(0.1f)] public float subsurfacePower = 2f;
			[Tooltip("Density range above the visible threshold used as fake thickness for subsurface scattering.")]
			[Min(0.0001f)] public float subsurfaceThickness = 0.25f;
			[Tooltip("How much subsurface scattering is boosted near thin edge regions.")]
			[Range(0f, 1f)] public float subsurfaceEdgeBoost = 0.6f;

			[Header("Curvature Boost")]
			[Tooltip("Render-only boost applied to convex high-curvature particles so small blobs survive larger blur radii. Set to 0 to disable.")]
			[Min(0f)] public float convexCurvatureBoost = 0f;
			[Tooltip("Curvature value that maps to full convex metaball boost.")]
			[Min(0.0001f)] public float convexCurvatureBoostMax = 5f;
			[Tooltip("Configured blur radius where convex curvature boost starts fading in.")]
			[Min(0f)] public float convexCurvatureBoostStartBlurRadius = 6f;
			[Tooltip("Configured blur radius range over which convex curvature boost reaches full strength.")]
			[Min(0.0001f)] public float convexCurvatureBoostBlurRange = 12f;

			[Header("Refraction")]
			[Tooltip("Metaball-only UV offset strength for refracting the blurred colour data outward from the normal. Alpha and phase remain unwarped.")]
			public float refractionStrength = 0.01f;
			[Tooltip("Density distance over which refraction fades in from the visible edge. Higher values push refraction farther inward.")]
			[Min(0.0001f)] public float refractionEdgeFade = 0.05f;

			[Header("Iridescence")]
			[Tooltip("Strength of fake thin-film iridescence in normal metaball rendering.")]
			[Min(0f)] public float iridescenceIntensity = 0f;
			[Tooltip("Number of hue cycles across the iridescence phase. Higher values make tighter rainbow bands.")]
			[Min(0f)] public float iridescenceScale = 2.0f;

			[Header("Ghost Boundary Normals")]
			[Tooltip("Blends analytic ellipse/cut-boundary normals into ghost particles in the metaball normal pass. Negative values flip the direction.")]
			[Range(-1f, 1f)] public float ghostBoundaryNormalStrength = 1f;
			[Tooltip("World-space distance around the horizontal cut corners used to blend ellipse and cut normals.")]
			public float ghostBoundaryCornerBlendWidth = 0.75f;
			[Tooltip("World-space band around the analytic boundary where fake ghost normals are applied.")]
			[Min(0.0001f)] public float ghostBoundaryNormalWidth = 1f;

			[Header("Bloom")]
			[Tooltip("Adds a metaball-only bloom pass from HDR lighting values without running full-scene post processing.")]
			public bool bloomEnabled;
			[Tooltip("Resolution of the bloom textures relative to the metaball render textures.")]
			[Range(0.125f, 1f)] public float bloomRenderTextureScale = 0.5f;
			[Tooltip("Gamma-space colour threshold where metaball bloom starts, matching Unity bloom's threshold convention.")]
			[Min(0f)] public float bloomThreshold = 1.0f;
			[Tooltip("Soft transition fraction around the bloom threshold. 0 is hard, 1 is fully soft.")]
			[Range(0f, 1f)] public float bloomSoftKnee = 0.5f;
			[Tooltip("Strength of the blurred bloom added back over the metaballs.")]
			[Min(0f)] public float bloomIntensity = 1.0f;
			[Tooltip("Power curve applied to extracted bloom brightness. 1 is neutral; higher values keep the core bright while making the falloff drop faster.")]
			[Min(0.01f)] public float bloomResponse = 1.0f;
			[Tooltip("Tent upsample scale for the metaball bloom pyramid. 8 is Unity-like; larger values spread each upsample more.")]
			[Min(0f)] public float bloomRadius = 8.0f;
			[Tooltip("Number of downsampled pyramid levels used for metaball-only bloom.")]
			[Range(1, 4)] public int bloomIterations = 3;

			[Header("Caustics")]
			[Tooltip("Compute shader used to raymarch the metaball density field and accumulate screen-space caustics.")]
			public ComputeShader causticsComputeShader;
			[Tooltip("Adds a low-resolution screen-space caustic texture generated from the metaball density iso-surface.")]
			public bool causticsEnabled;
			[Tooltip("Resolution of the caustic textures relative to the metaball render textures.")]
			[Range(0.125f, 1f)] public float causticsRenderTextureScale = 0.5f;
			[Tooltip("Brightness of the caustic contribution added before metaball tonemapping.")]
			[Min(0f)] public float causticsIntensity = 0.5f;
			[Tooltip("Multiplier for using the ray-marched caustic texture as coloured direct-light irradiance in metaball lighting. Values above 1 allow focused rays to brighten lighting strongly.")]
			[Min(0f)] public float causticsLightFieldIntensity = 1f;
			[Tooltip("0 makes caustics illuminate the existing fluid colour; 1 makes them a pure additive overlay.")]
			[Range(0f, 1f)] public float causticsAdditiveBlend = 0.25f;
			[Tooltip("Fluid index of refraction used at the inner analytic glass-fluid boundary.")]
			[Min(1.0001f)] public float causticsIndexOfRefraction = 1.33f;
			[Tooltip("Glass index of refraction used by the expanded analytic boundary shell.")]
			[Min(1.0001f)] public float causticsGlassIndexOfRefraction = 1.516f;
			[Tooltip("Index of refraction for phase 0 when bending caustic rays across visible phase boundaries.")]
			[Min(1.0001f)] public float causticsPhase0IndexOfRefraction = 1.442f;
			[Tooltip("Index of refraction for phase 1 when bending caustic rays across visible phase boundaries.")]
			[Min(1.0001f)] public float causticsPhase1IndexOfRefraction = 1.333f;
			[Tooltip("Energy retained by the transmitted caustic ray at each phase surface before Fresnel loss.")]
			[Range(0f, 1f)] public float causticsSurfaceTransmittance = 1f;
			[Tooltip("Strength of Fresnel energy loss at phase surfaces. 0 ignores Fresnel, 1 uses Schlick Fresnel.")]
			[Range(0f, 1f)] public float causticsFresnelStrength = 1f;
			[Tooltip("Uses Fresnel as a probability to randomly reflect caustic rays at surfaces instead of always transmitting one refracted ray.")]
			public bool causticsStochasticReflection = false;
			[Tooltip("Relative IOR spread used by stochastic spectral caustic tracing. 0.02 means red/blue use roughly -/+2% IOR.")]
			[Min(0f)] public float causticsDispersionStrength = 0f;
			[Tooltip("Angular radius of the caustic light source in degrees. 0 keeps perfectly parallel rays.")]
			[Min(0f)] public float causticsLightAngularRadiusDegrees = 0f;
			[Tooltip("Refracts caustic rays through the analytic ellipse/cut simulation boundary when elliptical bounds are enabled.")]
			public bool causticsUseAnalyticBoundary = true;
			[Tooltip("Additional world-space padding added to the caustic analytic boundary on top of the generated ghost-particle layer thickness.")]
			[Min(0f)] public float causticsAnalyticBoundaryPadding = 0f;
			[Tooltip("Exponential energy loss per world unit travelled through phase 0.")]
			[Min(0f)] public float causticsPhase0Absorption = 0f;
			[Tooltip("Exponential energy loss per world unit travelled through phase 1.")]
			[Min(0f)] public float causticsPhase1Absorption = 0f;
			[Tooltip("How quickly caustic rays inherit phase 0 colour, independent of brightness absorption.")]
			[Min(0f)] public float causticsPhase0ColorAbsorption = 0f;
			[Tooltip("How quickly caustic rays inherit phase 1 colour, independent of brightness absorption.")]
			[Min(0f)] public float causticsPhase1ColorAbsorption = 0f;
			[Tooltip("Boosts caustic absorption tint brightness while preserving hue. 0 uses the albedo colour directly, 1 normalizes by the brightest channel.")]
			[Range(0f, 1f)] public float causticsTintBoost = 0f;
			[Tooltip("Number of raymarch steps used per caustic ray. Higher values find exits more reliably but cost more.")]
			[Range(8, 192)] public int causticsRaySteps = 64;
			[HideInInspector] public float causticsStepPixels = 1.0f;
			[Tooltip("Brightness deposited along caustic ray paths. Keep at 0 to hide ray paths.")]
			[Min(0f)] public float causticsRayBrightness = 0.05f;
			[Tooltip("Launches only every Nth caustic ray for easier debugging. 1 uses every ray.")]
			[Min(1)] public int causticsRayStride = 1;
			[Tooltip("Launches multiple caustic rays per source pixel for denser supersampling. Cost scales roughly linearly.")]
			[Range(1, 8)] public int causticsRaysPerPixel = 1;
			[Tooltip("Splats each caustic ray sample bilinearly into four pixels. Disable to use the cheaper single-pixel atomic write.")]
			public bool causticsUseSoftSplat = true;
			[Tooltip("Small blur applied to the resolved caustic texture to reduce atomic splat noise.")]
			[Min(0f)] public float causticsBlurRadius = 1.5f;
			[Tooltip("Blends caustics with the previous frame to reduce flicker.")]
			public bool causticsTemporalEnabled = false;
			[Tooltip("Previous-frame weight used by caustic temporal blending. 0 uses only current frame; 0.55 means current * 0.45 + previous * 0.55.")]
			[Range(0f, 0.99f)] public float causticsTemporalHistoryWeight = 0.55f;
			[Tooltip("Frame-to-frame ray lattice jitter in caustic texture pixels. Useful with temporal blending.")]
			[Min(0f)] public float causticsTemporalJitterPixels = 0f;
			[Tooltip("Frame-to-frame relative IOR jitter used to slightly vary refracted ray paths for temporal smoothing. 0.005 means +/-0.5%.")]
			[Min(0f)] public float causticsTemporalIorJitter = 0f;

			[Header("Tonemapping")]
			[Tooltip("Compresses metaball lighting and custom bloom before output to reduce highlight clipping and hue shifts.")]
			public bool tonemapEnabled = true;
			[Tooltip("Exposure applied before metaball tonemapping. 1 preserves current brightness before compression.")]
			[Min(0f)] public float tonemapExposure = 1.0f;
			[Tooltip("Uses an ACES-style fitted curve instead of the peak-preserving exponential tonemap.")]
			public bool tonemapUseAces = false;
			[Tooltip("Desaturates very bright tonemapped highlights toward white to avoid coloured channel clipping.")]
			[Range(0f, 1f)] public float tonemapHighlightDesaturation = 0.5f;

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
		}
	}
}
