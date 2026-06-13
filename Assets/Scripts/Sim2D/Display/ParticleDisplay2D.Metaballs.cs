using System;
using UnityEngine;
using UnityEngine.Serialization;

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
			return GetEffectiveMotionBlurRadius(cam, 0f);
		}

		internal float GetEffectiveMotionBlurRadius(Camera cam, float motionBlurRadius)
		{
			motionBlurRadius *= ParticleResolutionLengthScale;
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
		public sealed class MetaballSettings
		{
			[Header("Shaders")]
			[FormerlySerializedAs("compositeShader")]
			[Tooltip("Shader used only for metaball debug visualizations.")]
			public Shader debugShader;
			[Tooltip("Shader used by the separated material-map pass. If left empty, Hidden/Particle2DMetaballMaterial is used as a fallback.")]
			public Shader materialShader;
			[Tooltip("Shader used for the separable Gaussian blur applied to the accumulation texture.")]
			public Shader blurShader;

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

			[Header("Ghost Boundary Normals")]
			[Tooltip("Strength of the analytic ellipse/cut-boundary normals in the metaball composite. Values above 1 make the boundary normal ramp steeper; negative values flip the direction.")]
			[Range(-4f, 4f)] public float ghostBoundaryNormalStrength = 1f;
			[Tooltip("World-space expansion applied to the analytic boundary. 0 uses the original, non-expanded analytic boundary.")]
			[Min(0f)] public float analyticBoundaryPadding = 0.175f;
		}
	}
}
