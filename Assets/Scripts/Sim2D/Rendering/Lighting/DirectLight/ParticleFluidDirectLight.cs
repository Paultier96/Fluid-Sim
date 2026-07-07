using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleFluidLighting2D))]
	public sealed class ParticleFluidDirectLight : MonoBehaviour
	{
		[Header("Shaders")]
		public Shader blurShader;
		public Shader temporalShader;
		public ComputeShader computeShader;
		public ComputeShader projectedShadowCompute;

		[Header("Direct Light")]
		public ParticleFluidLighting2D.LightingMode lightingMode = ParticleFluidLighting2D.LightingMode.Caustics;
		[Range(0.125f, 1f)] public float textureScale = 0.5f;

		[Header("Ray marched Lighting - Refraction")]
		public bool stochasticReflection = true;
		[Min(0f)] public float dispersionStrength = 0f;
		[Range(0f, 1f)] public float dispersionRotation = 1f;

		[Header("Ray marched Lighting - Rays")]
		[Range(8, 192)] public int extraRayTravelSteps = 64;
		[Min(0f)] public float rayBrightness = 1f;
		[Min(1)] public int rayStride = 1;
		[Range(1, 128)] public int raysPerPixel = 1;
		[Min(1)] public int colourSampleStride = 8;
		[Min(0f)] public float blur = 1.5f;

		[Header("Ray marched Lighting - Temporal Denoising")]
		public bool denoisingEnabled = true;
		[Range(0f, 0.99f)] public float temporalHistoryWeight = 0.95f;
		[Range(0f, 1f)] public float temporalHistoryClampStrength = 0.6f;
		[Min(0f)] public float temporalClampRejection = 0.5f;
		[Range(0f, 1f)] public float temporalRejectedSpatialFilter = 1f;
		public ParticleFluidLighting2D.TemporalMotionSource temporalMotionSource = ParticleFluidLighting2D.TemporalMotionSource.Static;
		[Min(0)] public float motionBlurRadius = 6;
		[Min(0f)] public float temporalMotionBlur = 1.5f;
		[Min(0f)] public float temporalMotionDilationRadius = 12f;
		[Range(1, 8)] public int temporalMotionDilationIterations = 3;
		[Range(0f, 1f)] public float temporalJitterPixels = 0f;
		[Min(0f)] public float surfaceNormalJitterPixels = 0f;
		public float projectedShadowOffset = 0f;
		[Min(0f)] public float projectedShadowExpansion = 0f;
		public bool projectedShadowHistoryRejection = true;
		public int projectedShadowMapBins = 2048;

		internal ParticleFluidLighting2D Owner => GetComponent<ParticleFluidLighting2D>();
		internal ParticleFluidCausticsTrace traceCaustics;
		internal ParticleFluidProjectedShadow projectedShadow;

		void Awake()
		{
			traceCaustics = new ParticleFluidCausticsTrace(this);
			projectedShadow = new ParticleFluidProjectedShadow();
		}

		internal void EnsureMaterials()
		{
			traceCaustics.temporalCaustics.EnsureMaterials();
		}

		internal void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityPhase0AccumulationTexture, Texture velocityPhase1AccumulationTexture)
		{
			traceCaustics.temporalCaustics.ApplyTemporalSettings(context, velocityPhase0AccumulationTexture, velocityPhase1AccumulationTexture);
		}

		internal Texture GetCurrentDirectLightTexture()
		{
			if (lightingMode != ParticleFluidLighting2D.LightingMode.Caustics)
			{
				return Texture2D.blackTexture;
			}

			return denoisingEnabled ? (Texture)traceCaustics.temporalCaustics.causticTemporalTexture : traceCaustics.causticResolvedTexture;
		}

		internal void ApplyInactiveProjectedShadow(Material material)
		{
			projectedShadow.ApplyToMaterial(material, false, Vector2.zero, projectedShadowOffset, projectedShadowExpansion);
		}

		internal void EnsureResources(Vector2Int causticSize, Bounds domainRegion)
		{
			bool useProjectedShadowMap = (projectedShadow.ShouldRender(this) || (denoisingEnabled && (projectedShadowHistoryRejection || temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.ProjectedShadow))) && projectedShadowCompute != null && Owner.lightManager.GetMainDirectionalLight() is { isActiveAndEnabled: true };

			void EnsureProjectedShadowResources()
			{
				projectedShadow.EnsureResources(useProjectedShadowMap, projectedShadowMapBins);
			}

			int causticWidth = causticSize.x;
			int causticHeight = causticSize.y;
			if (lightingMode == ParticleFluidLighting2D.LightingMode.Caustics)
			{
				traceCaustics.EnsureResources(causticWidth, causticHeight, true);
				if (denoisingEnabled)
				{
					EnsureProjectedShadowResources();
					traceCaustics.temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, true, useProjectedShadowMap);
				}
				else
				{
					traceCaustics.temporalCaustics.EnsureTemporalResources(causticWidth, causticHeight, domainRegion, false, false);
					projectedShadow.Release();
				}
			}
			else
			{
				traceCaustics.Release();
				EnsureProjectedShadowResources();
			}
		}

		internal void Release()
		{
			traceCaustics.Release();
			projectedShadow.Release();
		}
	}
}

