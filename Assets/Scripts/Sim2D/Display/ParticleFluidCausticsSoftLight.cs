using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsSoftLight
	{
		readonly ParticleFluidCausticsView caustics;

		public ParticleFluidCausticsSoftLight(ParticleFluidCausticsView caustics)
		{
			this.caustics = caustics;
		}

		public void RecordSoftLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			bool renderPhaseDiffuse = caustics.ShouldRenderPhaseDiffuseLight();
			bool renderRadianceCascade = caustics.ShouldRenderRadianceCascadeLight();
			Texture phase0SoftLightTexture = Texture2D.blackTexture;
			Texture phase1SoftLightTexture = Texture2D.blackTexture;
			if (renderPhaseDiffuse)
			{
				phase0SoftLightTexture = RenderPhaseDiffuseLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
				if (renderRadianceCascade && phase0SoftLightTexture is RenderTexture phase0Texture && caustics.SoftLightTexture2 != null)
				{
					targetCommandBuffer.Blit(phase0Texture, caustics.SoftLightTexture2);
					phase0SoftLightTexture = caustics.SoftLightTexture2;
				}
			}
			if (renderRadianceCascade)
			{
				phase1SoftLightTexture = RenderRadianceCascadeLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
			}
			caustics.SetSoftLightTextures(phase0SoftLightTexture, phase1SoftLightTexture);
		}

		Texture RenderPhaseDiffuseLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			ComputeShader compute = caustics.PhaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int gaussianHorizontalKernel = compute.FindKernel("MaskedGaussianHorizontal");
			int gaussianVerticalKernel = compute.FindKernel("MaskedGaussianVertical");
			int width = caustics.SoftLightTexture0.width;
			int height = caustics.SoftLightTexture0.height;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", caustics.SoftLightTexture0);
			DispatchCompute(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = caustics.SoftLightTexture0;
			RenderTexture target = caustics.SoftLightTexture1;
			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightWrite", target);
			DispatchCompute(targetCommandBuffer, compute, gaussianHorizontalKernel, width, height);

			RenderTexture previousSource = source;
			source = target;
			target = previousSource;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightWrite", target);
			DispatchCompute(targetCommandBuffer, compute, gaussianVerticalKernel, width, height);

			previousSource = source;
			source = target;
			target = previousSource;

			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return source;
		}

		void SetPhaseDiffuseCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int gaussianHorizontalKernel, int gaussianVerticalKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
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
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", caustics.PhaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", caustics.Phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", caustics.Phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", 1f);
			float gaussianRadiusScale = caustics.GetRayTextureBlurScale(surface) * Mathf.Max(caustics.PhaseDiffuseLightTextureScale, 0.0001f);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius0", caustics.Phase0Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius1", caustics.Phase1Material.diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", context.analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		Texture RenderRadianceCascadeLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Radiance Cascades");
			int width = caustics.SoftLightTexture0.width;
			int height = caustics.SoftLightTexture0.height;
			int cascadeCount = Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6);
			RenderTexture source = caustics.SoftLightTexture0;
			RenderTexture target = caustics.SoftLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeCommonParams(context, targetCommandBuffer, surface, sharpCaustics, width, height, combinedAccumulationTexture);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, caustics.RadianceCascadeMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			targetCommandBuffer.EndSample("Metaballs/Radiance Cascades");
			return source;
		}

		void SetRadianceCascadeCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, int width, int height, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D display = context.display;
			Camera cam = context.cam;
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
			Vector3 primaryDirectLightingDirection = caustics.GetDirectLightingDirection(display, caustics.PrimaryLight.Direction);
			bool useDirectionalLight =
				caustics.RadianceCascadeDirectionalLightEnabled
				&& caustics.PrimaryLight.enabled
				&& caustics.PrimaryLight.type == ParticleFluidLighting2D.DirectionalLightSettings.LightType.Directional
				&& caustics.PrimaryLight.intensity > 0f;
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics);
			targetCommandBuffer.SetGlobalTexture("CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(caustics.RadianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(caustics.RadianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", caustics.RadianceCascadeIntensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", caustics.RadianceCascadeBlobEmissionStrength);
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", caustics.RadianceCascadeDirectionalLightStrength);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightDirection", new Vector4(primaryDirectLightingDirection.x, primaryDirectLightingDirection.y, primaryDirectLightingDirection.z, 0f));
			targetCommandBuffer.SetGlobalVector("_DirectionalLightColor", useDirectionalLight ? (Vector4)caustics.PrimaryLight.EffectiveColor : Vector4.zero);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightIntensity", useDirectionalLight ? caustics.PrimaryLight.intensity : 0f);
			targetCommandBuffer.SetGlobalInt("radianceCascadeAbsorption", caustics.RadianceCascadeAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetGlobalFloat("edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetGlobalFloat("phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthA", caustics.Phase0Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthB", caustics.Phase1Material.diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("lightIntensity", 1f);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", caustics.Phase0Material.absorption);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1Absorption", caustics.Phase1Material.absorption);
			targetCommandBuffer.SetGlobalVector("causticsPhase0AbsorptionTint", caustics.Phase0Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalVector("causticsPhase1AbsorptionTint", caustics.Phase1Material.diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0AbsorptionTintBlend", caustics.Phase0Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1AbsorptionTintBlend", caustics.Phase1Material.absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", caustics.AbsorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", caustics.AbsorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetGlobalInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetGlobalFloat("analyticBoundaryExpansion", context.analyticBoundaryExpansion);
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		static void DispatchCompute(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}
	}
}
