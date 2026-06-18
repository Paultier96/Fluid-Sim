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
		public Material TemporalMaterial => owner.TemporalMaterial;
		public Material CausticBlurMaterial => owner.CausticBlurMaterial;
		public Material CausticMotionBlurMaterial => owner.CausticMotionBlurMaterial;
		public Material LightDirectionBlurMaterial => owner.LightDirectionBlurMaterial;
		public Material RadianceCascadeMaterial => owner.RadianceCascadeMaterial;
		public RenderTexture CausticResolvedTexture => owner.causticResolvedTexture;
		public RenderTexture CausticBlurTexture => owner.causticBlurTexture;
		public RenderTexture CausticMotionTexture => owner.causticMotionTexture;
		public RenderTexture CausticMotionDilatedTexture => owner.causticMotionDilatedTexture;
		public RenderTexture CausticMotionDilationScratchTexture => owner.causticMotionDilationScratchTexture;
		public RenderTexture LightDirectionTexture => owner.lightDirectionTexture;
		public RenderTexture LightDirectionBlurTexture => owner.lightDirectionBlurTexture;
		public RenderTexture SoftLightTexture0 => owner.softLightTexture0;
		public RenderTexture SoftLightTexture1 => owner.softLightTexture1;
		public RenderTexture CausticHistoryTexture => owner.causticHistoryTexture;
		public RenderTexture CausticTemporalTexture => owner.causticTemporalTexture;
		public RenderTexture LightDirectionHistoryTexture => owner.lightDirectionHistoryTexture;
		public RenderTexture LightDirectionTemporalTexture => owner.lightDirectionTemporalTexture;
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

		public float Blur => owner.blur;
		public float DirectionalLightFieldBlur => owner.directionalLightFieldBlur;
		public float TemporalMotionBlur => owner.temporalMotionBlur;
		public float TemporalMotionDilationRadius => owner.temporalMotionDilationRadius;
		public int TemporalMotionDilationIterations => owner.temporalMotionDilationIterations;
		public float TemporalHistoryWeight => owner.temporalHistoryWeight;
		public ParticleFluidLighting2D.TemporalMotionSource TemporalMotionSource => owner.temporalMotionSource;
		public float PhaseDiffuseLightBoundarySharpness => owner.phaseDiffuseLightBoundarySharpness;
		public float PhaseDiffuseLightTextureScale => owner.phaseDiffuseLightTextureScale;
		public int RadianceCascadeCount => owner.radianceCascadeCount;
		public float RadianceCascadeRayRange => owner.radianceCascadeRayRange;
		public int RadianceCascadeRaySteps => owner.radianceCascadeRaySteps;
		public float RadianceCascadeIntensity => owner.radianceCascadeIntensity;
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
		public bool UseAnalyticBoundary => owner.useAnalyticBoundary;
		public ParticleFluidLighting2D.LightingDebugVisualization DebugMode => owner.debugMode;
		public int MaxCausticTraceThreadCount => owner.MaxCausticTraceThreadCount;
		public int CausticTraceThreadGroupWidth => owner.CausticTraceThreadGroupWidth;

		public bool ShouldRenderCaustics() => owner.ShouldRenderCaustics();
		public bool ShouldRenderDirectionalLightField() => owner.ShouldRenderDirectionalLightField();
		public bool ShouldRenderPhaseDiffuseLight() => owner.ShouldRenderPhaseDiffuseLight();
		public bool ShouldRenderRadianceCascadeLight() => owner.ShouldRenderRadianceCascadeLight();
		public float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface) => owner.GetRayTextureBlurScale(surface);
		public void SetSoftLightTexture(Texture texture) => owner.SetSoftLightTexture(texture);
		public void BindCausticAccumulationTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel) => owner.BindCausticAccumulationTextures(targetCommandBuffer, compute, kernel);
		public void BindCausticAccumulationTextures(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel) => owner.BindCausticAccumulationTextures(targetCommandBuffer, compute, kernel);
		public void BindCausticMotionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel) => owner.BindCausticMotionTextures(targetCommandBuffer, compute, kernel);
		public void BindCausticMotionTextures(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel) => owner.BindCausticMotionTextures(targetCommandBuffer, compute, kernel);
		public void BindLightDirectionTextures(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, bool renderDirectionalLightField) => owner.BindLightDirectionTextures(targetCommandBuffer, compute, kernel, renderDirectionalLightField);
		public void BindLightDirectionTextures(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, bool renderDirectionalLightField) => owner.BindLightDirectionTextures(targetCommandBuffer, compute, kernel, renderDirectionalLightField);
		public void GetCausticRayRange(ParticleFluidLighting2D.FrameContext context, int width, int height, Vector3 lightDirection, out float startOffset, out int rayCount) => owner.GetCausticRayRange(context, width, height, lightDirection, out startOffset, out rayCount);
		public void GetCausticPointRaySpan(ParticleFluidLighting2D.FrameContext context, ParticleFluidLighting2D.DirectionalLightSettings light, out float angleStart, out float angleRange) => owner.GetCausticPointRaySpan(context, light, out angleStart, out angleRange);
		public bool AnyEnabledCausticPointLight() => owner.AnyEnabledCausticPointLight();
	}
}
