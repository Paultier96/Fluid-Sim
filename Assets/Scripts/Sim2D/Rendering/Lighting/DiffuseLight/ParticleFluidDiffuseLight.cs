using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidDiffuseLight
	{
		readonly ParticleFluidLighting2D owner;

		readonly struct RadianceCascadeLightState
		{
			public readonly bool useDirectionalLight;
			public readonly Vector3 direction;
			public readonly Vector4 color;
			public readonly float intensity;

			public RadianceCascadeLightState(bool useDirectionalLight, Vector3 direction, Vector4 color, float intensity)
			{
				this.useDirectionalLight = useDirectionalLight;
				this.direction = direction;
				this.color = color;
				this.intensity = intensity;
			}
		}

		public ParticleFluidDiffuseLight(ParticleFluidLighting2D owner)
		{
			this.owner = owner;
		}

		public void RecordSoftLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			bool renderPhaseDiffuse = owner.ShouldRenderPhaseDiffuseLight();
			bool renderRadianceCascade = owner.ShouldRenderRadianceCascadeLight();
			Texture phase0SoftLightTexture = Texture2D.blackTexture;
			Texture phase1SoftLightTexture = Texture2D.blackTexture;
			if (renderPhaseDiffuse)
			{
				phase0SoftLightTexture = RenderPhaseDiffuseLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
			}
			else if (renderRadianceCascade)
			{
				switch (owner.radianceCascadeTraceMode)
				{
					case ParticleFluidLighting2D.RadianceCascadeTraceMode.PhaseBoundarySdf:
						phase1SoftLightTexture = RenderRadianceCascadeSdfLight(context, targetCommandBuffer, surface, sharpCaustics);
						break;
					default:
						phase1SoftLightTexture = RenderRadianceCascadeVolumetricLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
						break;
				}
			}
			owner.currentSoftLightPhase0Texture = phase0SoftLightTexture;
			owner.currentSoftLightPhase1Texture = phase1SoftLightTexture;
			owner.lightingMaterial.SetTexture("SoftLightTex", phase0SoftLightTexture);
			owner.lightingMaterial.SetTexture("SoftLightTexPhase1", phase1SoftLightTexture);
		}

		Texture RenderPhaseDiffuseLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			ComputeShader compute = owner.phaseDiffuseLightCompute;
			int initKernel = compute.FindKernel("Init");
			int gaussianHorizontalKernel = compute.FindKernel("MaskedGaussianHorizontal");
			int gaussianVerticalKernel = compute.FindKernel("MaskedGaussianVertical");
			int width = owner.softLightTexture0.width;
			int height = owner.softLightTexture0.height;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SoftLightWrite", owner.softLightTexture0);
			DispatchCompute(targetCommandBuffer, compute, initKernel, width, height);

			RenderTexture source = owner.softLightTexture0;
			RenderTexture target = owner.softLightTexture1;
			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SoftLightWrite", target);
			DispatchCompute(targetCommandBuffer, compute, gaussianHorizontalKernel, width, height);

			Swap(ref source, ref target);

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, compute, initKernel, gaussianHorizontalKernel, gaussianVerticalKernel, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightRead", source);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SoftLightWrite", target);
			DispatchCompute(targetCommandBuffer, compute, gaussianVerticalKernel, width, height);

			Swap(ref source, ref target);

			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return source;
		}

		void SetPhaseDiffuseCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ComputeShader compute, int initKernel, int gaussianHorizontalKernel, int gaussianVerticalKernel, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, RenderTexture combinedAccumulationTexture)
		{
			ParticleDisplay2D display = context.display;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, initKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianHorizontalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "SharpCausticsTex", sharpCaustics);
			targetCommandBuffer.SetComputeTextureParam(compute, gaussianVerticalKernel, "CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSize", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBlendWidth", surface.phaseBlendWidth);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeFloatParam(compute, "phaseBoundarySharpness", owner.phaseDiffuseLightBoundarySharpness);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthA", materials[0].diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "scatterStrengthB", materials[1].diffuseScatterStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "lightIntensity", 1f);
			float gaussianRadiusScale = owner.GetRayTextureBlurScale(surface) * Mathf.Max(owner.phaseDiffuseLightTextureScale, 0.0001f);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius0", materials[0].diffuseGaussianRadius * gaussianRadiusScale);
			targetCommandBuffer.SetComputeFloatParam(compute, "gaussianRadius1", materials[1].diffuseGaussianRadius * gaussianRadiusScale);
			SetSharedBoundsComputeParams(context, targetCommandBuffer, compute);
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		Texture RenderRadianceCascadeVolumetricLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			if (!BuildRadianceCascadeSdfField(context, targetCommandBuffer, surface, out RenderTexture sdfResult, out _))
			{
				return RenderRadianceCascadePass(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture, null, null, owner.radianceCascadeMaterial, "Metaballs/Radiance Cascades", false);
			}

			return RenderRadianceCascadePass(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture, sdfResult, null, owner.radianceCascadeMaterial, "Metaballs/Radiance Cascades SDF Accelerated", false);
		}

		Texture RenderRadianceCascadeSdfLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics)
		{
			if (owner.radianceCascadeSdfMaterial == null
			    || !BuildRadianceCascadeSdfField(context, targetCommandBuffer, surface, out RenderTexture sdfResult, out RenderTexture sdfPayload))
			{
				return Texture2D.blackTexture;
			}

			return RenderRadianceCascadePass(context, targetCommandBuffer, surface, sharpCaustics, null, sdfResult, sdfPayload, owner.radianceCascadeSdfMaterial, "Metaballs/Radiance Cascades SDF", true);
		}

		Texture RenderRadianceCascadePass(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture, RenderTexture sdfResult, RenderTexture sdfPayload, Material material, string sampleName, bool sdfBoundaryMode)
		{
			targetCommandBuffer.BeginSample(sampleName);
			int width = owner.softLightTexture0.width;
			int height = owner.softLightTexture0.height;
			int cascadeCount = Mathf.Clamp(owner.radianceCascadeCount, 1, 6);
			RenderTexture source = owner.softLightTexture0;
			RenderTexture target = owner.softLightTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			RadianceCascadeLightState lightState = BuildRadianceCascadeLightState(context.display);
			SetRadianceCascadeCommonParams(context, targetCommandBuffer, surface, sharpCaustics, width, height, combinedAccumulationTexture, sdfResult, sdfPayload, lightState, sdfBoundaryMode);
			for (int level = cascadeCount - 1; level >= 0; level--)
			{
				targetCommandBuffer.SetGlobalInt("_CascadeLevel", level);
				targetCommandBuffer.SetGlobalTexture("_UpperCascadeTex", source);
				targetCommandBuffer.Blit(source, target, material, 0);
				Swap(ref source, ref target);
			}

			targetCommandBuffer.EndSample(sampleName);
			return source;
		}

		bool BuildRadianceCascadeSdfField(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, out RenderTexture resultTexture, out RenderTexture payloadTexture)
		{
			resultTexture = null;
			payloadTexture = null;
			ParticleDisplay2D display = context.display;
			ComputeShader compute = owner.radianceCascadeSdfCompute;
			if (compute == null
			    || owner.radianceCascadeSdfSeedA == null
			    || owner.radianceCascadeSdfSeedB == null
			    || owner.radianceCascadeSdfPayloadA == null
			    || owner.radianceCascadeSdfPayloadB == null
			    || owner.combinedSourceTexture == null)
			{
				return false;
			}

			int width = owner.radianceCascadeSdfSeedA.width;
			int height = owner.radianceCascadeSdfSeedA.height;
			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("SeedBoundary");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			int resolveDistanceKernel = compute.FindKernel("ResolveDistance");

			targetCommandBuffer.SetComputeIntParam(compute, "_Width", width);
			targetCommandBuffer.SetComputeIntParam(compute, "_Height", height);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", owner.combinedSourceTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", owner.combinedSourceTexture.height);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			SetSharedBoundsComputeParams(context, targetCommandBuffer, compute);
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldCenter", new Vector4(context.renderLayout.Caustic.WorldCenter.x, context.renderLayout.Caustic.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "metaballWorldSize", new Vector4(context.renderLayout.Caustic.WorldSize.x, context.renderLayout.Caustic.WorldSize.y, 0f, 0f));

			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "Result", owner.radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultPayload", owner.radianceCascadeSdfPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			targetCommandBuffer.DispatchCompute(compute, clearKernel, gx, gy, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "CombinedTex", owner.combinedSourceTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "Result", owner.radianceCascadeSdfSeedA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultPayload", owner.radianceCascadeSdfPayloadA);
			targetCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "CombinedTex", owner.combinedSourceTexture);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ColourMap", display.gradientTexture);
			targetCommandBuffer.DispatchCompute(compute, seedKernel, gx, gy, 1);

			RenderTexture src = owner.radianceCascadeSdfSeedA;
			RenderTexture dst = owner.radianceCascadeSdfSeedB;
			RenderTexture payloadSrc = owner.radianceCascadeSdfPayloadA;
			RenderTexture payloadDst = owner.radianceCascadeSdfPayloadB;
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

				Swap(ref src, ref dst);
				Swap(ref payloadSrc, ref payloadDst);
			}

			RenderTexture resolvedSdf = owner.radianceCascadeSdfNormalA;
			RenderTexture resolvedPayload = owner.radianceCascadeSdfNormalB;
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcTex", src);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "_SrcPayloadTex", payloadSrc);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "Result", resolvedSdf);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveDistanceKernel, "ResultPayload", resolvedPayload);
			targetCommandBuffer.DispatchCompute(compute, resolveDistanceKernel, gx, gy, 1);

			resultTexture = resolvedSdf;
			payloadTexture = resolvedPayload;
			return true;
		}

		void SetRadianceCascadeCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, int width, int height, RenderTexture combinedAccumulationTexture, RenderTexture sdfResult, RenderTexture sdfPayload, RadianceCascadeLightState lightState, bool sdfBoundaryMode)
		{
			ParticleDisplay2D display = context.display;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			targetCommandBuffer.SetGlobalTexture("_CausticTex", sharpCaustics != null ? sharpCaustics : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("_ResultTex", sdfResult != null ? sdfResult : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("_PayloadTex", sdfPayload);
			targetCommandBuffer.SetGlobalVector("_CascadeResolution", new Vector4(width, height, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("_RayRange", Mathf.Max(owner.radianceCascadeRayRange * display.GetZoomScale(context.cam), 0.001f));
			targetCommandBuffer.SetGlobalInt("_CascadeCount", Mathf.Clamp(owner.radianceCascadeCount, 1, 6));
			targetCommandBuffer.SetGlobalInt("_RaySteps", Mathf.Max(owner.radianceCascadeRaySteps, 1));
			targetCommandBuffer.SetGlobalFloat("_RadianceIntensity", owner.radianceCascadeIntensity);
			targetCommandBuffer.SetGlobalFloat("_BlobEmissionStrength", owner.radianceCascadeBlobEmissionStrength);
			ApplyRadianceCascadeLightGlobals(targetCommandBuffer, lightState);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightStrength", owner.radianceCascadeDirectionalLightStrength);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightCascadeStart", owner.radianceCascadeDirectionalLightCascadeStart);
			targetCommandBuffer.SetGlobalFloat("_SdfBoundaryThicknessPixels", Mathf.Max(owner.radianceCascadeSdfBoundaryThicknessPixels, 0f));
			targetCommandBuffer.SetGlobalInt("_DirectionalLightSdfVisibility", owner.radianceCascadeDirectionalLightSdfVisibility && !sdfBoundaryMode ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_SdfBoundarySource", sdfBoundaryMode ? (int)owner.radianceCascadeSdfBoundarySource : 0);
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			if (sdfBoundaryMode)
			{
				return;
			}

			targetCommandBuffer.SetGlobalTexture("CombinedTex", combinedAccumulationTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap", display.gradientTexture);
			targetCommandBuffer.SetGlobalTexture("ColourMap2", display.gradientTexture2);
			targetCommandBuffer.SetGlobalInt("_UseSdfSkipping", sdfResult != null ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("radianceCascadeAbsorption", owner.radianceCascadeAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalFloat("densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetGlobalFloat("edgeSoftness", surface.edgeSoftness);
			targetCommandBuffer.SetGlobalFloat("phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthA", materials[0].diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("scatterStrengthB", materials[1].diffuseScatterStrength);
			targetCommandBuffer.SetGlobalFloat("lightIntensity", 1f);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", materials[0].absorption);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1Absorption", materials[1].absorption);
			targetCommandBuffer.SetGlobalVector("causticsPhase0AbsorptionTint", materials[0].diffuseLightTint);
			targetCommandBuffer.SetGlobalVector("causticsPhase1AbsorptionTint", materials[1].diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("causticsPhase0AbsorptionTintBlend", materials[0].absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsPhase1AbsorptionTintBlend", materials[1].absorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", owner.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", owner.absorptionAlbedoSaturationInfluence);
			SetSharedBoundsGlobals(context, targetCommandBuffer);
			targetCommandBuffer.SetGlobalVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		RadianceCascadeLightState BuildRadianceCascadeLightState(ParticleDisplay2D display)
		{
			bool useDirectionalLight =
				owner.radianceCascadeDirectionalLightEnabled
				&& owner.lights[0].enabled
				&& owner.lights[0].type == ParticleFluidLighting2D.FluidLightSettings.LightType.Directional
				&& owner.lights[0].intensity > 0f;
			Vector3 direction = owner.GetDirectLightingDirection(display, owner.lights[0].Direction);
			Vector4 color = useDirectionalLight ? owner.lights[0].EffectiveColor : Vector4.zero;
			float intensity = useDirectionalLight ? owner.lights[0].intensity : 0f;
			return new RadianceCascadeLightState(useDirectionalLight, direction, color, intensity);
		}

		void ApplyRadianceCascadeLightGlobals(CommandBuffer targetCommandBuffer, RadianceCascadeLightState lightState)
		{
			targetCommandBuffer.SetGlobalInt("_DirectionalLightEnabled", lightState.useDirectionalLight ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("_DirectionalLightDirection", new Vector4(lightState.direction.x, lightState.direction.y, lightState.direction.z, 0f));
			targetCommandBuffer.SetGlobalVector("_DirectionalLightColor", lightState.color);
			targetCommandBuffer.SetGlobalFloat("_DirectionalLightIntensity", lightState.intensity);
		}

		void SetSharedBoundsComputeParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ComputeShader compute)
		{
			ParticleDisplay2D display = context.display;
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", context.analyticBoundaryExpansion);
		}

		void SetSharedBoundsGlobals(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleDisplay2D display = context.display;
			targetCommandBuffer.SetGlobalInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetGlobalFloat("analyticBoundaryExpansion", context.analyticBoundaryExpansion);
		}

		static void DispatchCompute(CommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		static void Swap(ref RenderTexture a, ref RenderTexture b)
		{
			RenderTexture temp = a;
			a = b;
			b = temp;
		}
	}
}
