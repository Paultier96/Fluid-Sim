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
			bool renderRadianceCascade = owner.radianceCascadeEnabled;
			Texture phase0SoftLightTexture = Texture2D.blackTexture;
			Texture phase1SoftLightTexture = Texture2D.blackTexture;
			Texture gaussianBlurredTexture = Texture2D.blackTexture;
			if (renderPhaseDiffuse)
			{
				phase0SoftLightTexture = RenderPhaseDiffuseLight(context, targetCommandBuffer, surface, sharpCaustics, combinedAccumulationTexture);
				gaussianBlurredTexture = phase0SoftLightTexture;
				if (renderRadianceCascade && owner.gaussianDiffuseEnabled && owner.radianceCascadeEnabled && owner.softLightPhase0Texture != null)
				{
					targetCommandBuffer.Blit(phase0SoftLightTexture, owner.softLightPhase0Texture);
					phase0SoftLightTexture = owner.softLightPhase0Texture;
				}
			}
			if (renderRadianceCascade)
			{
				phase1SoftLightTexture = RenderRadianceCascadeSdfLight(context, targetCommandBuffer, surface, sharpCaustics);
			}
			owner.currentGaussianSoftLightInitTexture = owner.gaussianSoftLightInitTexture != null ? owner.gaussianSoftLightInitTexture : Texture2D.blackTexture;
			owner.currentGaussianSoftLightBlurTexture = gaussianBlurredTexture;
			owner.currentSoftLightPhase0Texture = phase0SoftLightTexture;
			owner.currentSoftLightPhase1Texture = phase1SoftLightTexture;
			owner.lightingMaterial.SetTexture("SoftLightTex", phase0SoftLightTexture);
			owner.lightingMaterial.SetTexture("SoftLightTexPhase1", phase1SoftLightTexture);
		}

		Texture RenderPhaseDiffuseLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture combinedAccumulationTexture)
		{
			targetCommandBuffer.BeginSample("Metaballs/Phase Diffuse Light");
			Material initMaterial = owner.phaseDiffuseLightInitMaterial;
			Material blurMaterial = owner.gaussianDiffuseBlurMaterial;
			if (initMaterial == null || blurMaterial == null)
			{
				targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
				return Texture2D.blackTexture;
			}

			int width = owner.gaussianSoftLightTexture0.width;
			int height = owner.gaussianSoftLightTexture0.height;

			SetPhaseDiffuseCommonParams(context, targetCommandBuffer, initMaterial, width, height, sharpCaustics, surface, combinedAccumulationTexture);
			targetCommandBuffer.Blit(null, owner.gaussianSoftLightTexture0, initMaterial, 0);
			if (owner.gaussianSoftLightInitTexture != null)
			{
				targetCommandBuffer.Blit(owner.gaussianSoftLightTexture0, owner.gaussianSoftLightInitTexture);
			}

			RenderTexture source = owner.gaussianSoftLightTexture0;
			RenderTexture target = owner.gaussianSoftLightTexture1;
			float gaussianRadiusScale = owner.GetRayTextureBlurScale(surface) * Mathf.Max(owner.gaussianDiffuseTextureScale, 0.0001f);
			float blurRadius = owner.gaussianDiffuseRadius * gaussianRadiusScale;
			blurMaterial.SetFloat("blurRadius", blurRadius);

			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1f, 0f));
			targetCommandBuffer.Blit(source, target, blurMaterial);
			Swap(ref source, ref target);

			targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0f, 1f));
			targetCommandBuffer.Blit(source, target, blurMaterial);
			Swap(ref source, ref target);

			targetCommandBuffer.EndSample("Metaballs/Phase Diffuse Light");
			return source;
		}

		void SetPhaseDiffuseCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Material material, int width, int height, Texture sharpCaustics, ParticleDisplay2D.MetaballSettings surface, RenderTexture combinedAccumulationTexture)
		{
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			material.SetTexture("SharpCausticsTex", sharpCaustics);
			material.SetTexture("CombinedTex", combinedAccumulationTexture);
			material.SetVector("softLightSize", new Vector4(width, height, 0f, 0f));
			material.SetFloat("densityThreshold", surface.densityThreshold);
			material.SetFloat("edgeSoftness", surface.edgeSoftness);
			material.SetFloat("phaseBlendWidth", surface.phaseBlendWidth);
			material.SetFloat("phase0RenderBias", surface.phase0RenderBias);
			material.SetFloat("scatterStrengthA", owner.gaussianDiffuseScatterStrength);
			material.SetFloat("lightIntensity", 1f);
			SetSharedBoundsGlobals(context, targetCommandBuffer);
			material.SetInt("useEllipticalBounds", context.display.sim.useEllipticalBounds ? 1 : 0);
			material.SetVector("ellipseBoundsCenter", new Vector4(context.display.sim.ellipseBoundsCenter.x, context.display.sim.ellipseBoundsCenter.y, 0f, 0f));
			material.SetVector("ellipseBoundsSize", new Vector4(context.display.sim.ellipseBoundsSize.x, context.display.sim.ellipseBoundsSize.y, 0f, 0f));
			material.SetFloat("obstacleY", context.display.sim.obstacleY);
			material.SetFloat("analyticBoundaryExpansion", context.analyticBoundaryExpansion);
			material.SetVector("softLightWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			material.SetVector("softLightWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			material.SetVector("softLightSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
		}

		Texture RenderRadianceCascadeSdfLight(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics)
		{
			if (owner.radianceCascadeSdfMaterial == null
			    || !BuildRadianceCascadeSdfField(context, targetCommandBuffer, surface, out RenderTexture sdfResult, out RenderTexture sdfPayload))
			{
				return Texture2D.blackTexture;
			}

			return RenderRadianceCascadePass(context, targetCommandBuffer, surface, sharpCaustics, sdfResult, sdfPayload, owner.radianceCascadeSdfMaterial, "Metaballs/Radiance Cascades SDF");
		}

		Texture RenderRadianceCascadePass(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, ParticleDisplay2D.MetaballSettings surface, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, Material material, string sampleName)
		{
			targetCommandBuffer.BeginSample(sampleName);
			int width = owner.radianceCascadeTexture0.width;
			int height = owner.radianceCascadeTexture0.height;
			int cascadeCount = Mathf.Clamp(owner.radianceCascadeCount, 1, 6);
			RenderTexture source = owner.radianceCascadeTexture0;
			RenderTexture target = owner.radianceCascadeTexture1;
			targetCommandBuffer.Blit(Texture2D.blackTexture, source);

			RadianceCascadeLightState lightState = BuildRadianceCascadeLightState(context.display);
			SetRadianceCascadeCommonParams(context, targetCommandBuffer, width, height, sharpCaustics, sdfResult, sdfPayload, lightState);
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

		void SetRadianceCascadeCommonParams(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, int width, int height, Texture sharpCaustics, RenderTexture sdfResult, RenderTexture sdfPayload, RadianceCascadeLightState lightState)
		{
			ParticleDisplay2D display = context.display;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			bool useBoundarySourceTexture = false;
			Texture boundarySourceTexture = Texture2D.blackTexture;
			if (owner.lightingMode == ParticleFluidLighting2D.LightingMode.FullCaustics)
			{
				if (owner.gaussianDiffuseEnabled && owner.radianceCascadeEnabled && owner.softLightPhase0Texture != null)
				{
					boundarySourceTexture = owner.softLightPhase0Texture;
					useBoundarySourceTexture = true;
				}
				else if (sharpCaustics != null)
				{
					boundarySourceTexture = sharpCaustics;
					useBoundarySourceTexture = true;
				}
			}
			targetCommandBuffer.SetGlobalTexture("_BoundarySourceTex", boundarySourceTexture);
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
			targetCommandBuffer.SetGlobalFloat("_SdfPhase0InsetPixels", Mathf.Max(owner.radianceCascadeSdfPhase0InsetPixels, 0f));
			targetCommandBuffer.SetGlobalInt("_UseBoundarySourceTex", useBoundarySourceTexture ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_SdfBoundarySourceMultiplyAlbedo", owner.radianceCascadeSdfBoundarySourceMultiplyAlbedo ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_SdfApproximateAbsorption", owner.radianceCascadeSdfApproximateAbsorption ? 1 : 0);
			targetCommandBuffer.SetGlobalInt("_HybridPhase1Only", owner.gaussianDiffuseEnabled && owner.radianceCascadeEnabled ? 1 : 0);
			targetCommandBuffer.SetGlobalVector("metaballWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetGlobalVector("metaballWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetGlobalFloat("causticsPhase0Absorption", materials[0].absorption);
			targetCommandBuffer.SetGlobalVector("radianceCascadePhase0AbsorptionTint", materials[0].diffuseLightTint);
			targetCommandBuffer.SetGlobalFloat("radianceCascadePhase0AbsorptionTintBlend", materials[0].radianceCascadeAbsorptionDiffuseTintBlend);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoBrightnessInfluence", owner.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetGlobalFloat("causticsAbsorptionAlbedoSaturationInfluence", owner.absorptionAlbedoSaturationInfluence);
		}

		RadianceCascadeLightState BuildRadianceCascadeLightState(ParticleDisplay2D display)
		{
			ParticleFluidLight2D light = owner.lightSlots[0];
			ParticleFluidDirectionalLight2D directionalLight = light as ParticleFluidDirectionalLight2D;
			bool useDirectionalLight =
				owner.radianceCascadeDirectionalLightEnabled
				&& directionalLight != null
				&& directionalLight.isActiveAndEnabled
				&& directionalLight.intensity > 0f;
			Vector3 direction = directionalLight != null ? directionalLight.GetDirectLightingDirection(owner, display) : Vector3.down;
			Vector4 color = useDirectionalLight && light != null ? light.EffectiveColor : Vector4.zero;
			float intensity = useDirectionalLight && light != null ? light.intensity : 0f;
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
