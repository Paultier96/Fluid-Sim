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
			}
			else if (renderRadianceCascade)
			{
				switch (caustics.RadianceCascadeTraceMode)
				{
					case ParticleFluidLighting2D.RadianceCascadeTraceMode.PhaseBoundarySdf:
						phase1SoftLightTexture = RenderRadianceCascadeSdfLight(context, targetCommandBuffer, surface, sharpCaustics);
						break;
					default:
						phase1SoftLightTexture = RenderRadianceCascadeVolumetricLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
						break;
				}
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

		Texture RenderRadianceCascadeLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture, RenderTexture sdfResult, string sampleName)
		{
			targetCommandBuffer.BeginSample(sampleName);
			int width = caustics.SoftLightTexture0.width;
			int height = caustics.SoftLightTexture0.height;
			int cascadeCount = Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6);
			RenderTexture source = caustics.SoftLightTexture0;
			RenderTexture target = caustics.SoftLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeCommonParams(context, targetCommandBuffer, surface, sharpCaustics, width, height, combinedAccumulationTexture, sdfResult);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, caustics.RadianceCascadeMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			targetCommandBuffer.EndSample(sampleName);
			return source;
		}

		Texture RenderRadianceCascadeVolumetricLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			if (!BuildRadianceCascadeSdfField(context, targetCommandBuffer, surface, out RenderTexture sdfResult, out _))
			{
				return RenderRadianceCascadeLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture, null, "Metaballs/Radiance Cascades");
			}

			return RenderRadianceCascadeLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture, sdfResult, "Metaballs/Radiance Cascades SDF Accelerated");
		}

		Texture RenderRadianceCascadeSdfLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics)
		{
			if (caustics.RadianceCascadeSdfMaterial == null
			    || !BuildRadianceCascadeSdfField(context, targetCommandBuffer, surface, out RenderTexture sdfResult, out RenderTexture sdfPayload))
			{
				return Texture2D.blackTexture;
			}

			targetCommandBuffer.BeginSample("Metaballs/Radiance Cascades SDF");
			int width = caustics.SoftLightTexture0.width;
			int height = caustics.SoftLightTexture0.height;
			int cascadeCount = Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6);
			RenderTexture source = caustics.SoftLightTexture0;
			RenderTexture target = caustics.SoftLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			SetRadianceCascadeSdfCommonParams(context, targetCommandBuffer, surface, width, height, sdfResult, sdfPayload, sharpCaustics);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, caustics.RadianceCascadeSdfMaterial, 0);

				RenderTexture previousSource = source;
				source = target;
				target = previousSource;
			}

			targetCommandBuffer.EndSample("Metaballs/Radiance Cascades SDF");
			return source;
		}

		bool BuildRadianceCascadeSdfField(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, out RenderTexture resultTexture, out RenderTexture payloadTexture)
		{
			resultTexture = null;
			payloadTexture = null;
			ParticleDisplay2D display = context.display;
			ComputeShader compute = caustics.RadianceCascadeSdfCompute;
			if (compute == null
			    || caustics.RadianceCascadeSdfSeedA == null
			    || caustics.RadianceCascadeSdfSeedB == null
			    || caustics.RadianceCascadeSdfPayloadA == null
			    || caustics.RadianceCascadeSdfPayloadB == null
			    || caustics.CombinedSourceTexture == null)
			{
				return false;
			}

			int width = caustics.RadianceCascadeSdfSeedA.width;
			int height = caustics.RadianceCascadeSdfSeedA.height;
			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("SeedBoundary");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			int resolveDistanceKernel = compute.FindKernel("ResolveDistance");

			targetCommandBuffer.SetComputeIntParam(compute, "_Width", width);
			targetCommandBuffer.SetComputeIntParam(compute, "_Height", height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", caustics.CombinedSourceTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", caustics.CombinedSourceTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", context.analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldCenter", new Vector4(context.causticRenderRegion.WorldCenter.x, context.causticRenderRegion.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldSize", new Vector4(context.causticRenderRegion.WorldSize.x, context.causticRenderRegion.WorldSize.y, 0f, 0f));

			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "Result", caustics.RadianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultPayload", caustics.RadianceCascadeSdfPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			targetCommandBuffer.DispatchCompute(compute, clearKernel, gx, gy, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "CombinedTex", caustics.CombinedSourceTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "Result", caustics.RadianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultPayload", caustics.RadianceCascadeSdfPayloadA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "CombinedTex", caustics.CombinedSourceTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.DispatchCompute(compute, seedKernel, gx, gy, 1);

			RenderTexture src = caustics.RadianceCascadeSdfSeedA;
			RenderTexture dst = caustics.RadianceCascadeSdfSeedB;
			RenderTexture payloadSrc = caustics.RadianceCascadeSdfPayloadA;
			RenderTexture payloadDst = caustics.RadianceCascadeSdfPayloadB;
			int maxDim = Mathf.Max(width, height);
			int step = 1;
			while ((step << 1) < maxDim)
			{
				step <<= 1;
			}

			for (int s = step; s >= 1; s >>= 1)
			{
				targetCommandBuffer.SetComputeIntParam(compute, "_Step", s);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcTex", src);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcPayloadTex", payloadSrc);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstTex", dst);
				targetCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstPayloadTex", payloadDst);
				targetCommandBuffer.DispatchCompute(compute, jumpFloodKernel, gx, gy, 1);

				RenderTexture tmp = src;
				src = dst;
				dst = tmp;
				tmp = payloadSrc;
				payloadSrc = payloadDst;
				payloadDst = tmp;
			}

			RenderTexture resolvedSdf = caustics.RadianceCascadeSdfNormalA;
			RenderTexture resolvedPayload = caustics.RadianceCascadeSdfNormalB;
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcTex", src);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcPayloadTex", payloadSrc);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "Result", resolvedSdf);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ResultPayload", resolvedPayload);
			targetCommandBuffer.DispatchCompute(compute, resolveDistanceKernel, gx, gy, 1);

			resultTexture = resolvedSdf;
			payloadTexture = resolvedPayload;
			return true;
		}

		void SetRadianceCascadeCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, int width, int height, RenderTexture combinedAccumulationTexture, RenderTexture sdfResult)
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
			targetCommandBuffer.SetGlobalTexture("_ResultTex", sdfResult != null ? sdfResult : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalInt("_UseSdfSkipping", sdfResult != null ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(caustics.RadianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(caustics.RadianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", caustics.RadianceCascadeIntensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", caustics.RadianceCascadeBlobEmissionStrength);
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", caustics.RadianceCascadeDirectionalLightStrength);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightCascadeStart", caustics.RadianceCascadeDirectionalLightCascadeStart);
			targetCommandBuffer.SetGlobalInt("_DirectionalLightSdfVisibility", caustics.RadianceCascadeDirectionalLightSdfVisibility ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("_SdfBoundaryThicknessPixels", Mathf.Max(caustics.RadianceCascadeSdfBoundaryThicknessPixels, 0f));
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

		void SetRadianceCascadeSdfCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, int width, int height, RenderTexture sdfResult, RenderTexture sdfPayload, Texture sharpCaustics)
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
			targetCommandBuffer.SetGlobalTexture("_ResultTex", sdfResult);
			targetCommandBuffer.SetGlobalTexture("_PayloadTex", sdfPayload);
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics != null ? sharpCaustics : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(caustics.RadianceCascadeRayRange * display.GetZoomScale(cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(caustics.RadianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(caustics.RadianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", caustics.RadianceCascadeIntensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", caustics.RadianceCascadeBlobEmissionStrength);
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", caustics.RadianceCascadeDirectionalLightStrength);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightCascadeStart", caustics.RadianceCascadeDirectionalLightCascadeStart);
			targetCommandBuffer.SetGlobalFloat("_SdfBoundaryThicknessPixels", Mathf.Max(caustics.RadianceCascadeSdfBoundaryThicknessPixels, 0f));
			targetCommandBuffer.SetGlobalInt("_SdfBoundarySource", (int)caustics.RadianceCascadeSdfBoundarySource);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightDirection", new Vector4(primaryDirectLightingDirection.x, primaryDirectLightingDirection.y, primaryDirectLightingDirection.z, 0f));
			targetCommandBuffer.SetGlobalVector("_DirectionalLightColor", useDirectionalLight ? (Vector4)caustics.PrimaryLight.EffectiveColor : Vector4.zero);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightIntensity", useDirectionalLight ? caustics.PrimaryLight.intensity : 0f);
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
		}

		static void DispatchCompute(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}
	}
}
