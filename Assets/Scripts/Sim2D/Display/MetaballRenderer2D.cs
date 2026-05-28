using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballRenderer2D
	{
		const int MaxCustomBloomLevels = 4;
		const string CommandBufferName = "Sim2D Metaball Render";

		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		Material causticBlurMaterial;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		RenderTexture causticAccumulationRTexture;
		RenderTexture causticAccumulationGTexture;
		RenderTexture causticAccumulationBTexture;
		RenderTexture causticResolvedTexture;
		RenderTexture causticBlurTexture;
		RenderTexture causticHistoryTexture;
		RenderTexture causticTemporalTexture;
		readonly RenderTexture[] bloomDownTextures = new RenderTexture[MaxCustomBloomLevels];
		readonly RenderTexture[] bloomUpTextures = new RenderTexture[MaxCustomBloomLevels];
		CommandBuffer commandBuffer;
		bool commandBufferAttached;
		bool clearCausticHistory;
		bool hasPreviousCausticCamera;
		int causticFrameIndex;
		Vector2 previousCausticWorldCenter;
		Vector2 previousCausticWorldSize;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			EnsureMaterials(display);
			if (metaballMaterial == null || compositeMaterial == null || blurMaterial == null || causticBlurMaterial == null)
			{
				RemoveCommandBuffer();
				return;
			}

			EnsureCommandBuffer(cam);
			EnsureRenderTextures(display, cam);
			ApplyMetaballMaterialSettings(display);
			ApplyCompositeSettings(display, cam);
			BuildCommandBuffer(display, cam);
		}

		public void RemoveCommandBuffer()
		{
			if (commandBuffer != null)
			{
				RemoveFromCamera(Camera.main);
#if UNITY_EDITOR
				RemoveFromCamera(ParticleDisplay2D.GetSceneViewCamera());
#endif
			}

			commandBufferAttached = false;
		}

		public void RemoveFromCamera(Camera cam)
		{
			if (cam == null || commandBuffer == null)
			{
				return;
			}

			cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
			cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
			cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
			RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything);
			RemoveCommandBuffersByName(cam, CameraEvent.AfterForwardAlpha);
			RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects);
		}

		public void Release()
		{
			RemoveCommandBuffer();
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture);
			ComputeHelper.Release(causticAccumulationRTexture, causticAccumulationGTexture, causticAccumulationBTexture, causticResolvedTexture, causticBlurTexture, causticHistoryTexture, causticTemporalTexture);
			ReleaseBloomTextures();

			if (commandBuffer != null)
			{
				commandBuffer.Release();
				commandBuffer = null;
			}

			if (metaballMaterial != null)
			{
				Object.DestroyImmediate(metaballMaterial);
				metaballMaterial = null;
			}

			if (compositeMaterial != null)
			{
				Object.DestroyImmediate(compositeMaterial);
				compositeMaterial = null;
			}

			if (blurMaterial != null)
			{
				Object.DestroyImmediate(blurMaterial);
				blurMaterial = null;
			}

			if (causticBlurMaterial != null)
			{
				Object.DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}
		}

		void EnsureMaterials(ParticleDisplay2D display)
		{
			EnsureMaterial(ref metaballMaterial, display.metaballShader);
			EnsureMaterial(ref compositeMaterial, display.metaballs.compositeShader);
			EnsureMaterial(ref blurMaterial, display.metaballs.blurShader);
			EnsureMaterial(ref causticBlurMaterial, display.metaballs.blurShader);
		}

		static void EnsureMaterial(ref Material material, Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			if (material != null)
			{
				Object.DestroyImmediate(material);
			}

			material = new Material(shader);
		}

		void ApplyMetaballMaterialSettings(ParticleDisplay2D display)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			display.BindSimulationBuffers(metaballMaterial);
			display.ApplyCommonParticleSettings(metaballMaterial);
			metaballMaterial.SetFloat("metaballSharpness", settings.sharpness);
			metaballMaterial.SetFloat("metaballIntensity", settings.intensity);
			metaballMaterial.SetFloat("convexCurvatureMetaballBoost", settings.convexCurvatureBoost);
			metaballMaterial.SetFloat("convexCurvatureBoostMax", settings.convexCurvatureBoostMax);
			metaballMaterial.SetFloat("convexCurvatureBoostStartBlurRadius", settings.convexCurvatureBoostStartBlurRadius);
			metaballMaterial.SetFloat("convexCurvatureBoostBlurRange", settings.convexCurvatureBoostBlurRange);
			metaballMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			metaballMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			metaballMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			metaballMaterial.SetFloat("obstacleY", display.sim.obstacleY);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			metaballMaterial.SetFloat("metaballGhostBoundaryCornerBlendWidth", settings.ghostBoundaryCornerBlendWidth);
			metaballMaterial.SetFloat("metaballGhostBoundaryNormalWidth", settings.ghostBoundaryNormalWidth);
		}

		void EnsureCommandBuffer(Camera cam)
		{
			if (commandBuffer == null)
			{
				commandBuffer = new CommandBuffer { name = CommandBufferName };
			}

#if UNITY_EDITOR
			RemoveFromCamera(ParticleDisplay2D.GetSceneViewCamera());
#endif

			if (!commandBufferAttached && cam != null)
			{
				cam.RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
				cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything);
				RemoveCommandBuffersByName(cam, CameraEvent.AfterForwardAlpha);
				RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects);
				cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				commandBufferAttached = true;
			}
		}

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * settings.renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * settings.renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Accumulation");
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Blur");

			if (settings.bloomEnabled)
			{
				int bloomLevels = GetCustomBloomLevelCount(display);
				int bloomWidth = Mathf.Max(1, Mathf.RoundToInt(width * settings.bloomRenderTextureScale));
				int bloomHeight = Mathf.Max(1, Mathf.RoundToInt(height * settings.bloomRenderTextureScale));
				for (int i = 0; i < MaxCustomBloomLevels; i++)
				{
					if (i < bloomLevels)
					{
						int mipWidth = Mathf.Max(1, bloomWidth >> i);
						int mipHeight = Mathf.Max(1, bloomHeight >> i);
						ComputeHelper.CreateRenderTexture(ref bloomDownTextures[i], mipWidth, mipHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Bloom Down " + i);
						ComputeHelper.CreateRenderTexture(ref bloomUpTextures[i], mipWidth, mipHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Bloom Up " + i);
					}
					else
					{
						ComputeHelper.Release(bloomDownTextures[i], bloomUpTextures[i]);
						bloomDownTextures[i] = null;
						bloomUpTextures[i] = null;
					}
				}
			}
			else
			{
				ReleaseBloomTextures();
			}

			if (ShouldRenderCaustics(settings))
			{
				int causticWidth = Mathf.Max(1, Mathf.RoundToInt(width * settings.causticsRenderTextureScale));
				int causticHeight = Mathf.Max(1, Mathf.RoundToInt(height * settings.causticsRenderTextureScale));
				ComputeHelper.CreateRenderTexture(ref causticAccumulationRTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation R");
				ComputeHelper.CreateRenderTexture(ref causticAccumulationGTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation G");
				ComputeHelper.CreateRenderTexture(ref causticAccumulationBTexture, causticWidth, causticHeight, FilterMode.Point, GraphicsFormat.R32_UInt, "Particle2D Caustic Accumulation B");
				ComputeHelper.CreateRenderTexture(ref causticResolvedTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Resolved");
				ComputeHelper.CreateRenderTexture(ref causticBlurTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
				if (settings.causticsTemporalEnabled)
				{
					bool historyChanged = ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
					ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, causticWidth, causticHeight, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
					clearCausticHistory |= historyChanged;
				}
				else
				{
					ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture);
					causticHistoryTexture = null;
					causticTemporalTexture = null;
					clearCausticHistory = true;
					hasPreviousCausticCamera = false;
				}
			}
			else
			{
				ComputeHelper.Release(causticAccumulationRTexture, causticAccumulationGTexture, causticAccumulationBTexture, causticResolvedTexture, causticBlurTexture, causticHistoryTexture, causticTemporalTexture);
				causticAccumulationRTexture = null;
				causticAccumulationGTexture = null;
				causticAccumulationBTexture = null;
				causticResolvedTexture = null;
				causticBlurTexture = null;
				causticHistoryTexture = null;
				causticTemporalTexture = null;
				clearCausticHistory = true;
				hasPreviousCausticCamera = false;
			}
		}

		void ApplyCompositeSettings(ParticleDisplay2D display, Camera cam)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			compositeMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", settings.edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			compositeMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			compositeMaterial.SetFloat("phaseBiasNormalStrength", settings.phaseBiasNormalStrength);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			compositeMaterial.SetTexture("ColourMap", display.gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", display.gradientTexture2);
			compositeMaterial.SetTexture("DebugHeatMap", display.debugHeatMapTexture);
			compositeMaterial.SetTexture("DebugSignedHeatMap", display.debugSignedHeatMapTexture);

			float effectiveBlurRadius = display.GetEffectiveBlurRadius(cam);
			float effectiveRefractionStrength = settings.refractionStrength * display.GetZoomScale(cam);
			float effectiveBloomSampleScale = Mathf.Max(0.5f, settings.bloomRadius / 8f);
			float effectiveConfiguredBlurRadius = display.EffectiveConfiguredBlurRadius;
			metaballMaterial.SetFloat("metaballBlurRadius", effectiveConfiguredBlurRadius);
			compositeMaterial.SetFloat("metaballRefractionStrength", effectiveRefractionStrength);
			compositeMaterial.SetFloat("metaballRefractionEdgeFade", settings.refractionEdgeFade);
			compositeMaterial.SetFloat("metaballIridescenceIntensity", settings.iridescenceIntensity);
			compositeMaterial.SetFloat("metaballIridescenceScale", settings.iridescenceScale);
			compositeMaterial.SetFloat("customBloomThreshold", settings.bloomThreshold);
			compositeMaterial.SetFloat("customBloomSoftKnee", settings.bloomSoftKnee);
			compositeMaterial.SetFloat("customBloomIntensity", settings.bloomIntensity);
			compositeMaterial.SetFloat("customBloomResponse", settings.bloomResponse);
			compositeMaterial.SetFloat("customBloomSampleScale", Mathf.Max(0.5f, effectiveBloomSampleScale));
			compositeMaterial.SetInt("customBloomEnabled", settings.bloomEnabled ? 1 : 0);
			compositeMaterial.SetInt("metaballTonemapEnabled", settings.tonemapEnabled ? 1 : 0);
			compositeMaterial.SetFloat("metaballTonemapExposure", settings.tonemapExposure);
			compositeMaterial.SetInt("metaballTonemapUseAces", settings.tonemapUseAces ? 1 : 0);
			compositeMaterial.SetFloat("metaballTonemapHighlightDesaturation", settings.tonemapHighlightDesaturation);
			compositeMaterial.SetInt("metaballCausticsEnabled", ShouldRenderCaustics(settings) ? 1 : 0);
			compositeMaterial.SetTexture("CausticTex", settings.causticsTemporalEnabled ? causticTemporalTexture : causticResolvedTexture);
			compositeMaterial.SetFloat("metaballCausticsIntensity", settings.causticsIntensity);
			compositeMaterial.SetFloat("metaballCausticsAdditiveBlend", settings.causticsAdditiveBlend);
			compositeMaterial.SetColor("metaballCausticsColor", settings.lightColor);
			compositeMaterial.SetFloat("causticTemporalHistoryWeight", settings.causticsTemporalHistoryWeight);
			float effectiveNormalStrength = display.GetEffectiveNormalStrength(effectiveConfiguredBlurRadius);
			compositeMaterial.SetInt("debugMode", (int)display.debugMode);
			display.ApplyDebugClipSettings(compositeMaterial);
			compositeMaterial.SetFloat("ditherStrength", settings.ditherStrength);
			compositeMaterial.SetVector("particleLightDirection", settings.LightDirection);
			compositeMaterial.SetColor("particleLightColor", settings.lightColor);
			compositeMaterial.SetFloat("particleAmbientLight", settings.ambientLight);
			compositeMaterial.SetFloat("particleDirectionalLightIntensity", settings.directionalLightIntensity);
			compositeMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			compositeMaterial.SetColor("particleSpecularColor", settings.specularColor);
			compositeMaterial.SetFloat("particleSpecularIntensity", settings.specularIntensity);
			compositeMaterial.SetFloat("particleSpecularPower", settings.specularPower);
			compositeMaterial.SetColor("particleFresnelColor", settings.fresnelColor);
			compositeMaterial.SetFloat("particleFresnelIntensity", settings.fresnelIntensity);
			compositeMaterial.SetFloat("particleFresnelPower", settings.fresnelPower);
			compositeMaterial.SetVector("particleGlowDirection", new Vector4(settings.glowDirection.x, settings.glowDirection.y, 0f, 0f));
			compositeMaterial.SetColor("particleGlowColor", settings.glowColor);
			compositeMaterial.SetFloat("particleGlowIntensity", settings.glowIntensity);
			compositeMaterial.SetFloat("particleGlowPower", settings.glowPower);
			compositeMaterial.SetFloat("particleTransmissionIntensity", settings.transmissionIntensity);
			compositeMaterial.SetFloat("particleTransmissionPower", settings.transmissionPower);
			compositeMaterial.SetFloat("particleEdgeDarkening", settings.edgeDarkening);
			compositeMaterial.SetFloat("particleEdgeDarkeningPower", settings.edgeDarkeningPower);
			compositeMaterial.SetColor("particleSubsurfaceColor", settings.subsurfaceColor);
			compositeMaterial.SetFloat("particleSubsurfaceIntensity", settings.subsurfaceIntensity);
			compositeMaterial.SetFloat("particleSubsurfacePower", settings.subsurfacePower);
			compositeMaterial.SetFloat("particleSubsurfaceThickness", settings.subsurfaceThickness);
			compositeMaterial.SetFloat("particleSubsurfaceEdgeBoost", settings.subsurfaceEdgeBoost);
			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);
		}

		void BuildCommandBuffer(ParticleDisplay2D display, Camera cam)
		{
			commandBuffer.Clear();
			commandBuffer.SetRenderTarget(combinedAccumulationTexture);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			commandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 0, display.argsBuffer);
			commandBuffer.SetRenderTarget(normalAccumulationTexture);
			commandBuffer.ClearRenderTarget(false, true, Color.clear);
			commandBuffer.DrawMeshInstancedIndirect(display.mesh, 0, metaballMaterial, 1, display.argsBuffer);

			commandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			commandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			commandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			commandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			commandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			commandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);

			if (ShouldRenderCaustics(display.metaballs))
			{
				BuildCaustics(display, cam);
			}

			if (display.metaballs.bloomEnabled)
			{
				int bloomLevels = GetCustomBloomLevelCount(display);
				commandBuffer.SetRenderTarget(bloomDownTextures[0]);
				commandBuffer.ClearRenderTarget(false, true, Color.clear);
				commandBuffer.Blit(null, bloomDownTextures[0], compositeMaterial, 1);
				for (int i = 1; i < bloomLevels; i++)
				{
					commandBuffer.Blit(bloomDownTextures[i - 1], bloomDownTextures[i], compositeMaterial, 3);
				}

				RenderTexture lastBloom = bloomDownTextures[bloomLevels - 1];
				for (int i = bloomLevels - 2; i >= 0; i--)
				{
					commandBuffer.SetGlobalTexture("BloomTex", bloomDownTextures[i]);
					commandBuffer.Blit(lastBloom, bloomUpTextures[i], compositeMaterial, 4);
					lastBloom = bloomUpTextures[i];
				}

				commandBuffer.SetGlobalTexture("BloomTex", lastBloom);
			}

			commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0);

			display.AppendVectorFieldDraw(commandBuffer);
		}

		void BuildCaustics(ParticleDisplay2D display, Camera cam)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			ComputeShader compute = settings.causticsComputeShader;
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = causticAccumulationRTexture.width;
			int height = causticAccumulationRTexture.height;
			Vector3 lightDirection = settings.LightDirection;

			commandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			commandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			commandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			commandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			commandBuffer.SetComputeIntParam(compute, "causticsRaySteps", settings.causticsRaySteps);
			commandBuffer.SetComputeIntParam(compute, "causticsRayStride", settings.causticsRayStride);
			commandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", settings.causticsRaysPerPixel);
			commandBuffer.SetComputeIntParam(compute, "causticsUseSoftSplat", settings.causticsUseSoftSplat ? 1 : 0);
			commandBuffer.SetComputeFloatParam(compute, "densityThreshold", settings.densityThreshold);
			commandBuffer.SetComputeFloatParam(compute, "causticsStepPixels", settings.causticsStepPixels);
			commandBuffer.SetComputeFloatParam(compute, "causticsNormalSampleRadius", settings.causticsNormalSampleRadius);
			commandBuffer.SetComputeFloatParam(compute, "causticsIndexOfRefraction", settings.causticsIndexOfRefraction);
			commandBuffer.SetComputeFloatParam(compute, "causticsPhase0IndexOfRefraction", settings.causticsPhase0IndexOfRefraction);
			commandBuffer.SetComputeFloatParam(compute, "causticsPhase1IndexOfRefraction", settings.causticsPhase1IndexOfRefraction);
			commandBuffer.SetComputeFloatParam(compute, "causticsSurfaceTransmittance", settings.causticsSurfaceTransmittance);
			commandBuffer.SetComputeFloatParam(compute, "causticsFresnelStrength", settings.causticsFresnelStrength);
			commandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", settings.causticsStochasticReflection ? 1 : 0);
			commandBuffer.SetComputeIntParam(compute, "causticsStochasticDispersion", settings.causticsStochasticDispersion ? 1 : 0);
			commandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", settings.causticsDispersionStrength);
			commandBuffer.SetComputeFloatParam(compute, "causticsLightAngularRadius", settings.causticsLightAngularRadiusDegrees * Mathf.Deg2Rad);
			commandBuffer.SetComputeFloatParam(compute, "causticsAbsorption", settings.causticsAbsorption);
			commandBuffer.SetComputeFloatParam(compute, "causticsBackgroundAbsorption", settings.causticsBackgroundAbsorption);
			commandBuffer.SetComputeFloatParam(compute, "causticsColorAbsorption", settings.causticsColorAbsorption);
			commandBuffer.SetComputeFloatParam(compute, "causticsBackgroundColorAbsorption", settings.causticsBackgroundColorAbsorption);
			commandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", settings.causticsRayBrightness);
			commandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", settings.causticsTemporalEnabled ? settings.causticsTemporalJitterPixels : 0f);
			commandBuffer.SetComputeFloatParam(compute, "causticsTemporalIorJitter", settings.causticsTemporalEnabled ? settings.causticsTemporalIorJitter : 0f);
			commandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", causticFrameIndex++);
			commandBuffer.SetComputeVectorParam(compute, "causticsLightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f));
			commandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds && settings.causticsUseAnalyticBoundary ? 1 : 0);
			commandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			commandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			commandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 currentWorldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			Vector2 currentWorldSize = new Vector2(worldWidth, worldHeight);
			commandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			commandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));

			BindCausticAccumulationTextures(compute, clearKernel);
			commandBuffer.SetComputeTextureParam(compute, clearKernel, "CausticResult", causticResolvedTexture);
			DispatchCaustics(compute, clearKernel, width, height);

			commandBuffer.SetComputeTextureParam(compute, traceKernel, "CombinedTex", combinedAccumulationTexture);
			commandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap", display.gradientTexture);
			commandBuffer.SetComputeTextureParam(compute, traceKernel, "ColourMap2", display.gradientTexture2);
			BindCausticAccumulationTextures(compute, traceKernel);
			DispatchCausticTrace(compute, traceKernel, width, height, lightDirection, Mathf.Max(1, settings.causticsRaysPerPixel));

			BindCausticAccumulationTextures(compute, resolveKernel);
			commandBuffer.SetComputeTextureParam(compute, resolveKernel, "CausticResult", causticResolvedTexture);
			DispatchCaustics(compute, resolveKernel, width, height);

			if (settings.causticsBlurRadius > 0.001f)
			{
				causticBlurMaterial.SetFloat("blurRadius", settings.causticsBlurRadius);
				commandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				commandBuffer.Blit(causticResolvedTexture, causticBlurTexture, causticBlurMaterial);
				commandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				commandBuffer.Blit(causticBlurTexture, causticResolvedTexture, causticBlurMaterial);
			}

			if (settings.causticsTemporalEnabled)
			{
				if (clearCausticHistory)
				{
					commandBuffer.SetRenderTarget(causticHistoryTexture);
					commandBuffer.ClearRenderTarget(false, true, Color.clear);
					clearCausticHistory = false;
					hasPreviousCausticCamera = false;
				}

				Vector2 historyWorldCenter = hasPreviousCausticCamera ? previousCausticWorldCenter : currentWorldCenter;
				Vector2 historyWorldSize = hasPreviousCausticCamera ? previousCausticWorldSize : currentWorldSize;
				commandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
				commandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				commandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(historyWorldCenter.x, historyWorldCenter.y, 0f, 0f));
				commandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(historyWorldSize.x, historyWorldSize.y, 0f, 0f));
				commandBuffer.SetGlobalTexture("CausticHistoryTex", causticHistoryTexture);
				commandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, compositeMaterial, 5);
				commandBuffer.Blit(causticTemporalTexture, causticHistoryTexture);

				previousCausticWorldCenter = currentWorldCenter;
				previousCausticWorldSize = currentWorldSize;
				hasPreviousCausticCamera = true;
			}
			else
			{
				hasPreviousCausticCamera = false;
			}
		}

		void DispatchCaustics(ComputeShader compute, int kernel, int width, int height)
		{
			commandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchCausticTrace(ComputeShader compute, int kernel, int width, int height, Vector3 lightDirection, int raysPerPixel)
		{
			bool horizontal = Mathf.Abs(lightDirection.x) >= Mathf.Abs(lightDirection.y);
			int traceWidth = horizontal ? width : width * raysPerPixel;
			int traceHeight = horizontal ? height * raysPerPixel : height;
			DispatchCaustics(compute, kernel, traceWidth, traceHeight);
		}

		void BindCausticAccumulationTextures(ComputeShader compute, int kernel)
		{
			commandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumR", causticAccumulationRTexture);
			commandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumG", causticAccumulationGTexture);
			commandBuffer.SetComputeTextureParam(compute, kernel, "CausticAccumB", causticAccumulationBTexture);
		}

		int GetCustomBloomLevelCount(ParticleDisplay2D display)
		{
			return Mathf.Clamp(display.metaballs.bloomIterations, 1, MaxCustomBloomLevels);
		}

		bool ShouldRenderCaustics(ParticleDisplay2D.MetaballSettings settings)
		{
			return settings.causticsEnabled && settings.causticsComputeShader != null && settings.causticsIntensity > 0f;
		}

		void ReleaseBloomTextures()
		{
			for (int i = 0; i < MaxCustomBloomLevels; i++)
			{
				ComputeHelper.Release(bloomDownTextures[i], bloomUpTextures[i]);
				bloomDownTextures[i] = null;
				bloomUpTextures[i] = null;
			}
		}

		void RemoveCommandBuffersByName(Camera cam, CameraEvent evt)
		{
			if (cam == null)
			{
				return;
			}

			CommandBuffer[] commandBuffers = cam.GetCommandBuffers(evt);
			for (int i = 0; i < commandBuffers.Length; i++)
			{
				CommandBuffer candidate = commandBuffers[i];
				if (candidate != null && candidate.name == CommandBufferName)
				{
					cam.RemoveCommandBuffer(evt, candidate);
				}
			}
		}
	}
}
