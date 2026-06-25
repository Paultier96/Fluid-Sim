using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsView
	{
		readonly ParticleFluidLighting2D owner;

		public ParticleFluidCausticsView(ParticleFluidLighting2D owner)
		{
			this.owner = owner;
		}

		public ComputeShader ComputeShader => owner.computeShader;
		public ComputeShader PhaseDiffuseLightCompute => owner.phaseDiffuseLightCompute;
		public Material TemporalMaterial => owner.temporalMaterial;
		public Material CausticBlurMaterial => owner.causticBlurMaterial;
		public Material CausticMotionBlurMaterial => owner.causticMotionBlurMaterial;
		public Material LightDirectionBlurMaterial => owner.lightDirectionBlurMaterial;
		public Material RadianceCascadeMaterial => owner.radianceCascadeMaterial;
		public Material RadianceCascadeSdfMaterial => owner.radianceCascadeSdfMaterial;
		public ComputeShader RadianceCascadeSdfCompute => owner.radianceCascadeSdfCompute;
		public ComputeBuffer CausticAccumulationBuffer => owner.causticAccumulationBuffer;
		public ComputeBuffer CausticMotionAccumulationBuffer => owner.causticMotionAccumulationBuffer;
		public ComputeBuffer LightDirectionAccumulationBuffer => owner.lightDirectionAccumulationBuffer;
		public ComputeBuffer LightDirectionAccumulationFallbackBuffer => owner.lightDirectionAccumulationFallbackBuffer;
		public RenderTexture CausticResolvedTexture => owner.causticResolvedTexture;
		public RenderTexture CombinedSourceTexture => owner.combinedSourceTexture;
		public RenderTexture CausticBlurTexture => owner.causticBlurTexture;
		public RenderTexture CausticMotionTexture => owner.causticMotionTexture;
		public RenderTexture CausticMotionDilatedTexture => owner.causticMotionDilatedTexture;
		public RenderTexture CausticMotionDilationScratchTexture => owner.causticMotionDilationScratchTexture;
		public RenderTexture LightDirectionTexture => owner.lightDirectionTexture;
		public RenderTexture LightDirectionBlurTexture => owner.lightDirectionBlurTexture;
		public RenderTexture SoftLightTexture0 => owner.softLightTexture0;
		public RenderTexture SoftLightTexture1 => owner.softLightTexture1;
		public RenderTexture RadianceCascadeSdfSeedA => owner.radianceCascadeSdfSeedA;
		public RenderTexture RadianceCascadeSdfSeedB => owner.radianceCascadeSdfSeedB;
		public RenderTexture RadianceCascadeSdfPayloadA => owner.radianceCascadeSdfPayloadA;
		public RenderTexture RadianceCascadeSdfPayloadB => owner.radianceCascadeSdfPayloadB;
		public RenderTexture RadianceCascadeSdfNormalA => owner.radianceCascadeSdfNormalA;
		public RenderTexture RadianceCascadeSdfNormalB => owner.radianceCascadeSdfNormalB;
		public RenderTexture CausticHistoryTexture => owner.causticHistoryTexture;
		public RenderTexture CausticTemporalTexture => owner.causticTemporalTexture;
		public RenderTexture LightDirectionHistoryTexture => owner.lightDirectionHistoryTexture;
		public RenderTexture LightDirectionTemporalTexture => owner.lightDirectionTemporalTexture;
		public ComputeBuffer ReactiveShadowMapBuffer => owner.reactiveShadowMapBuffer;
		public RenderTexture ReactiveShadowMapTexture => owner.reactiveShadowMapTexture;
		public RenderTexture ReactiveShadowMapHistoryTexture => owner.reactiveShadowMapHistoryTexture;
		public RenderTexture LightDirectionFallbackTexture => owner.lightDirectionResultFallbackTexture;

		public ParticleFluidLighting2D.DirectionalLightSettings PrimaryLight => owner.primaryLight;
		public ParticleFluidLighting2D.DirectionalLightSettings SecondaryLight => owner.secondaryLight;
		public ParticleFluidLighting2D.DirectionalLightSettings TertiaryLight => owner.tertiaryLight;
		public ParticleFluidLighting2D.PhaseMaterialSettings Phase0Material => owner.phase0Material;
		public ParticleFluidLighting2D.PhaseMaterialSettings Phase1Material => owner.phase1Material;
		public ParticleFluidLighting2D.PhaseMaterialSettings BoundaryMaterial => owner.boundaryMaterial;

		public bool DenoisingEnabled => owner.denoisingEnabled;
		public bool ClearHistory
		{
			get => owner.clearCausticHistory;
			set => owner.clearCausticHistory = value;
		}
		public bool HasPreviousCamera
		{
			get => owner.hasPreviousCausticCamera;
			set => owner.hasPreviousCausticCamera = value;
		}
		public int TemporalFrameCount
		{
			get => owner.causticTemporalFrameCount;
			set => owner.causticTemporalFrameCount = value;
		}
		public Vector2 PreviousWorldCenter
		{
			get => owner.previousCausticWorldCenter;
			set => owner.previousCausticWorldCenter = value;
		}
		public Vector2 PreviousWorldSize
		{
			get => owner.previousCausticWorldSize;
			set => owner.previousCausticWorldSize = value;
		}
		public Vector2 PreviousReactiveShadowDirection
		{
			get => owner.previousReactiveShadowDirection;
			set => owner.previousReactiveShadowDirection = value;
		}

		public float Blur => owner.blur;
		public float DirectionalLightFieldBlur => owner.directionalLightFieldBlur;
		public float TemporalMotionBlur => owner.temporalMotionBlur;
		public float TemporalMotionDilationRadius => owner.temporalMotionDilationRadius;
		public int TemporalMotionDilationIterations => owner.temporalMotionDilationIterations;
		public float TemporalHistoryWeight => owner.temporalHistoryWeight;
		public ParticleFluidLighting2D.TemporalMotionSource TemporalMotionSource => owner.temporalMotionSource;
		public float MotionVelocityThreshold => owner.motionVelocityThreshold;
		public float PhaseDiffuseLightBoundarySharpness => owner.phaseDiffuseLightBoundarySharpness;
		public float PhaseDiffuseLightTextureScale => owner.phaseDiffuseLightTextureScale;
		public int RadianceCascadeCount => owner.radianceCascadeCount;
		public ParticleFluidLighting2D.RadianceCascadeTraceMode RadianceCascadeTraceMode => owner.radianceCascadeTraceMode;
		public float RadianceCascadeRayRange => owner.radianceCascadeRayRange;
		public int RadianceCascadeRaySteps => owner.radianceCascadeRaySteps;
		public float RadianceCascadeIntensity => owner.radianceCascadeIntensity;
		public float RadianceCascadeBlobEmissionStrength => owner.radianceCascadeBlobEmissionStrength;
		public bool RadianceCascadeDirectionalLightEnabled => owner.radianceCascadeDirectionalLightEnabled;
		public float RadianceCascadeDirectionalLightStrength => owner.radianceCascadeDirectionalLightStrength;
		public float RadianceCascadeDirectionalLightCascadeStart => owner.radianceCascadeDirectionalLightCascadeStart;
		public bool RadianceCascadeDirectionalLightSdfVisibility => owner.radianceCascadeDirectionalLightSdfVisibility;
		public float RadianceCascadeSdfBoundaryThicknessPixels => owner.radianceCascadeSdfBoundaryThicknessPixels;
		public ParticleFluidLighting2D.RadianceCascadeSdfBoundarySource RadianceCascadeSdfBoundarySource => owner.radianceCascadeSdfBoundarySource;
		public float RadianceCascadePhase0Visibility => owner.radianceCascadePhase0Visibility;
		public float RadianceCascadePhase1Visibility => owner.radianceCascadePhase1Visibility;
		public bool RadianceCascadeAbsorption => owner.radianceCascadeAbsorption;
		public int RaysPerPixel => owner.raysPerPixel;
		public int RaySteps => owner.raySteps;
		public int RayStride => owner.rayStride;
		public int ColourSampleStride => owner.colourSampleStride;
		public float StepPixels => owner.stepPixels;
		public bool StochasticReflection => owner.stochasticReflection;
		public float DispersionStrength => owner.dispersionStrength;
		public float DispersionRotation => owner.dispersionRotation;
		public float LightAngularRadiusDegrees => owner.lightAngularRadiusDegrees;
		public float AbsorptionAlbedoBrightnessInfluence => owner.absorptionAlbedoBrightnessInfluence;
		public float AbsorptionAlbedoSaturationInfluence => owner.absorptionAlbedoSaturationInfluence;
		public float RayBrightness => owner.rayBrightness;
		public float TemporalJitterPixels => owner.temporalJitterPixels;
		public float SurfaceNormalJitterPixels => owner.surfaceNormalJitterPixels;
		public float ProjectedShadowOffset => owner.projectedShadowOffset;
		public float ProjectedShadowExpansion => owner.projectedShadowExpansion;
		public bool ProjectedShadowHistoryRejection => owner.projectedShadowHistoryRejection;
		public bool UseAnalyticBoundary => owner.useAnalyticBoundary;
		public ParticleFluidLighting2D.LightingDebugVisualization DebugMode => owner.debugMode;
		public int MaxCausticTraceThreadCount => ParticleFluidLighting2D.MaxCausticTraceThreads;
		public int CausticTraceThreadGroupWidth => ParticleFluidLighting2D.CausticTraceThreadGroupSize;
		public int ReactiveShadowMapBins => owner.ReactiveShadowMapBins;

		public bool ShouldRenderCaustics() => owner.ShouldRenderCaustics();
		public bool ShouldRenderDirectionalLightField() => owner.ShouldRenderDirectionalLightField();
		public bool ShouldRenderPhaseDiffuseLight() => owner.ShouldRenderPhaseDiffuseLight();
		public bool ShouldRenderRadianceCascadeLight() => owner.ShouldRenderRadianceCascadeLight();
		public bool UsesRadianceCascadeSdfField() => owner.UsesRadianceCascadeSdfField();
		public float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface) => owner.GetRayTextureBlurScale(surface);
		public Vector3 GetDirectLightingDirection(ParticleDisplay2D display, Vector3 lightDirection) => owner.GetDirectLightingDirection(display, lightDirection);
		public void SetSoftLightTextures(Texture phase0Texture, Texture phase1Texture)
		{
			owner.currentSoftLightPhase0Texture = phase0Texture;
			owner.currentSoftLightPhase1Texture = phase1Texture;
			owner.lightingMaterial.SetTexture("SoftLightTex", phase0Texture);
			owner.lightingMaterial.SetTexture("SoftLightTexPhase1", phase1Texture);
		}

		public void GetCausticRayRange(ParticleFluidLighting2D.FrameContext context, int width, int height, Vector3 lightDirection, out float startOffset, out int rayCount) => owner.GetCausticRayRange(context, width, height, lightDirection, out startOffset, out rayCount);
		public void GetCausticPointRaySpan(ParticleFluidLighting2D.FrameContext context, ParticleFluidLighting2D.DirectionalLightSettings light, out float angleStart, out float angleRange) => owner.GetCausticPointRaySpan(context, light, out angleStart, out angleRange);
		public bool AnyEnabledCausticPointLight() => owner.AnyEnabledCausticPointLight();
		public ParticleFluidProjectedShadow ProjectedShadow => owner.ProjectedShadow;
	}
}
