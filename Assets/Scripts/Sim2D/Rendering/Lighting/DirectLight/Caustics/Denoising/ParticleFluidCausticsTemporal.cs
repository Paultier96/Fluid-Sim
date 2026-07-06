using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTemporal
	{
		readonly ParticleFluidCausticsTrace trace;
		bool clearCausticHistory;
		bool hasPreviousCausticCamera;
		int causticTemporalFrameCount;
		Vector2 previousCausticWorldCenter;
		Vector2 previousCausticWorldSize;
		Vector2 previousProjectedShadowDirection;
		Material temporalMaterial;
		Material causticBlurMaterial;
		Material causticMotionBlurMaterial;
		internal RenderTexture causticBlurTexture;
		internal RenderTexture causticMotionDilatedTexture;
		internal RenderTexture causticMotionDilationScratchTexture;
		internal RenderTexture causticHistoryTexture;
		internal RenderTexture causticTemporalTexture;

		public ParticleFluidCausticsTemporal(ParticleFluidCausticsTrace trace)
		{
			this.trace = trace;
		}

		internal void EnsureMaterials()
		{
			ParticleFluidDirectLight owner = trace.owner;
			ParticleFluidRenderUtils.EnsureMaterial(ref temporalMaterial, owner.temporalShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref causticBlurMaterial, owner.blurShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref causticMotionBlurMaterial, owner.blurShader);
		}

		internal void EnsureResources(int width, int height, bool denoisingEnabled)
		{
			ComputeHelper.CreateRenderTexture(ref causticBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
			ComputeHelper.CreateRenderTexture(ref causticMotionDilatedTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilated");
			ComputeHelper.CreateRenderTexture(ref causticMotionDilationScratchTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Motion Dilation Scratch");
			if (denoisingEnabled)
			{
				ComputeHelper.CreateRenderTexture(ref causticHistoryTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic History");
				ComputeHelper.CreateRenderTexture(ref causticTemporalTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Temporal");
			}
			else
			{
				ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture);
				causticHistoryTexture = null;
				causticTemporalTexture = null;
			}
		}

		internal void EnsureTemporalResources(int causticWidth, int causticHeight, ParticleFluidRenderRegion2D domainRegion, bool denoisingEnabled, bool useProjectedShadowMap)
		{
			bool hadCausticHistory = causticHistoryTexture != null && causticHistoryTexture.IsCreated();
			bool causticHistoryResize = hadCausticHistory
				&& (causticHistoryTexture.width != causticWidth || causticHistoryTexture.height != causticHeight);
			EnsureResources(causticWidth, causticHeight, denoisingEnabled);
			if (denoisingEnabled)
			{
				bool canMigrateHistory = hasPreviousCausticCamera && previousCausticWorldSize.x > 0f && previousCausticWorldSize.y > 0f;
				RenderTexture causticHistoryBackup = canMigrateHistory && causticHistoryResize
					? CreateHistoryBackup(causticHistoryTexture, "Particle2D Caustic History Backup")
					: null;

				bool historyChanged = !hadCausticHistory || causticHistoryResize;
				bool migratedHistory = false;
				if (causticHistoryBackup != null && historyChanged)
				{
					migratedHistory = TryMigrateHistoryOnResize(causticHistoryBackup, causticHistoryTexture, previousCausticWorldCenter, previousCausticWorldSize, domainRegion);
				}

				ComputeHelper.Release(causticHistoryBackup);

				bool historyMigrationFailed = historyChanged && !migratedHistory;
				if (migratedHistory && !historyMigrationFailed)
				{
					previousCausticWorldCenter = domainRegion.WorldCenter;
					previousCausticWorldSize = domainRegion.WorldSize;
				}
				clearCausticHistory |= historyMigrationFailed;
			}
			else
			{
				EnsureResources(causticWidth, causticHeight, false);
				trace.owner.projectedShadow.Release();
				previousProjectedShadowDirection = Vector2.zero;
				clearCausticHistory = true;
				hasPreviousCausticCamera = false;
				causticTemporalFrameCount = 0;
			}

			if (!useProjectedShadowMap)
			{
				previousProjectedShadowDirection = Vector2.zero;
			}
		}

		internal void Release()
		{
			ComputeHelper.Release(causticBlurTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture);
			causticBlurTexture = null;
			causticMotionDilatedTexture = null;
			causticMotionDilationScratchTexture = null;
			causticHistoryTexture = null;
			causticTemporalTexture = null;
			hasPreviousCausticCamera = false;
			causticTemporalFrameCount = 0;
			previousCausticWorldCenter = Vector2.zero;
			previousCausticWorldSize = Vector2.zero;
			previousProjectedShadowDirection = Vector2.zero;
			clearCausticHistory = false;
			ParticleFluidRenderUtils.DestroyMaterial(ref temporalMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref causticBlurMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref causticMotionBlurMaterial);
		}

		public void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityPhase0AccumulationTexture, Texture velocityPhase1AccumulationTexture)
		{
			if (temporalMaterial == null)
			{
				return;
			}

			ParticleFluidDirectLight owner = trace.owner;
			ParticleFluidLighting2D lightingOwner = owner.Owner;
			ParticleDisplay2D display = context.display;
			temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture != null ? velocityPhase0AccumulationTexture : Texture2D.blackTexture);
			temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture != null ? velocityPhase1AccumulationTexture : Texture2D.blackTexture);
			temporalMaterial.SetTexture("CausticMotionTex", trace.causticMotionTexture != null ? trace.causticMotionTexture : Texture2D.blackTexture);
			temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, lightingOwner));
			temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : owner.temporalHistoryWeight);
			temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", owner.temporalHistoryClampStrength);
			temporalMaterial.SetFloat("causticTemporalClampRejection", owner.temporalClampRejection);
			temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", owner.temporalRejectedSpatialFilter);
			temporalMaterial.SetInt("causticTemporalMotionSource", (int)owner.temporalMotionSource);
			temporalMaterial.SetTexture("MaterialTransportTex", lightingOwner.materialTransportTexture != null ? lightingOwner.materialTransportTexture : Texture2D.blackTexture);
		}

		public bool TryMigrateHistoryOnResize(RenderTexture oldHistory, RenderTexture newHistory, Vector2 oldWorldCenter, Vector2 oldWorldSize, ParticleFluidRenderRegion2D domainRegion)
		{
			if (temporalMaterial == null
				|| oldHistory == null
				|| newHistory == null
				|| !hasPreviousCausticCamera
				|| oldHistory.width <= 0
				|| oldHistory.height <= 0)
			{
				return false;
			}

			CommandBuffer commandBuffer = CommandBufferPool.Get("Particle2D Reproject History");
			ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(commandBuffer, trace.owner.Owner.Display.metaballs);
			ParticleFluidRasterLayoutBindings.ApplyCausticHistoryGlobals(commandBuffer, domainRegion, oldWorldCenter, oldWorldSize);
			commandBuffer.Blit(oldHistory, newHistory, temporalMaterial, 2);
			Graphics.ExecuteCommandBuffer(commandBuffer);
			CommandBufferPool.Release(commandBuffer);
			return true;
		}

		static RenderTexture CreateHistoryBackup(RenderTexture source, string name)
		{
			if (source == null || !source.IsCreated() || source.width <= 0 || source.height <= 0)
			{
				return null;
			}

			RenderTexture backup = ComputeHelper.CreateRenderTexture(source.width, source.height, source.filterMode, source.graphicsFormat, name);
			Graphics.Blit(source, backup);
			return backup;
		}
		


		public RenderTexture RecordBlurAndMotion(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleFluidDirectLight owner = trace.owner;
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			float rayTextureBlurScale = surface.renderTextureScale * owner.textureScale;
			float causticBlurRadius = owner.blur * rayTextureBlurScale;
			float temporalMotionBlurRadius = owner.temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, causticBlurRadius, causticBlurMaterial,trace.causticResolvedTexture, causticBlurTexture);
			}

			RenderTexture temporalMotionTexture = trace.causticMotionTexture;
			bool useCausticMotion = temporalMaterial != null && owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && owner.temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, owner.temporalMotionDilationIterations);
				float motionDilationRadius = owner.temporalMotionDilationRadius * rayTextureBlurScale / dilationIterations;
				ParticleFluidRasterLayoutBindings.ApplyCausticCurrentGlobals(targetCommandBuffer, context.renderLayout.CausticRegion);
				RenderTexture dilationSource = trace.causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, temporalMaterial, 1);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == causticMotionDilatedTexture ? causticMotionDilationScratchTexture : causticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && causticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == causticMotionDilationScratchTexture
					? causticMotionDilatedTexture
					: causticMotionDilationScratchTexture;
				ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, temporalMotionBlurRadius, causticMotionBlurMaterial,temporalMotionTexture,motionBlurScratch);
			}
			temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : trace.causticMotionTexture);
			return temporalMotionTexture;
		}

		public RenderTexture GetTemporalMotionTextureAfterBlur()
		{
			ParticleFluidDirectLight owner = trace.owner;
			bool useCausticMotion = temporalMaterial != null && owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (!useCausticMotion)
			{
				return trace.causticMotionTexture;
			}

			bool hasDilationTargets = causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null;
			if (owner.temporalMotionDilationRadius > 0.001f && hasDilationTargets)
			{
				int dilationIterations = Mathf.Max(1, owner.temporalMotionDilationIterations);
				return dilationIterations % 2 == 1 ? causticMotionDilatedTexture : causticMotionDilationScratchTexture;
			}

			return trace.causticMotionTexture;
		}

		public Texture RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture temporalMotionTexture)
		{
			const int CopyWithValidAlphaPass = 3;
			ParticleFluidDirectLight owner = trace.owner;
			ParticleFluidLighting2D lightingOwner = owner.Owner;
			ParticleDisplay2D display = context.display;
			bool wantsProjectedShadowMap =
				(owner.projectedShadowHistoryRejection || owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.ProjectedShadow);
			Vector2 projectedShadowDirection = Vector2.zero;
			bool useProjectedShadowMap = false;
			temporalMaterial?.SetTexture("MaterialTransportTex", lightingOwner.materialTransportTexture != null ? lightingOwner.materialTransportTexture : Texture2D.blackTexture);
			if (wantsProjectedShadowMap
			    && lightingOwner.lightManager.GetMainDirectionalLight() is ParticleFluidDirectionalLight2D directionalLight)
			{
				Vector3 effectiveLightDirection = directionalLight.GetDirectLightingDirection(lightingOwner, context.display.sim.analyticBoundary);
				ParticleFluidProjectedShadow.RecordParams projectedShadowParams = new(
					owner.projectedShadowCompute,
					owner.projectedShadow.projectedShadowMapBuffer,
					owner.projectedShadow.projectedShadowMapTexture,
					lightingOwner.materialTransportTexture,
					owner.projectedShadowMapBins,
					effectiveLightDirection,
					context.renderLayout.SourceRegion,
					context.renderLayout.CausticRegion);
				useProjectedShadowMap = owner.projectedShadow.RecordCurrentShadowMap(targetCommandBuffer, projectedShadowParams, out projectedShadowDirection);
			}

			if (owner.denoisingEnabled && temporalMaterial != null)
			{
				ParticleFluidAnalyticBoundaryBindings.ApplyPhaseSplitGlobals(targetCommandBuffer, display.metaballs);
				if (clearCausticHistory || !hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(trace.causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(trace.causticResolvedTexture, causticHistoryTexture, temporalMaterial, CopyWithValidAlphaPass);
					clearCausticHistory = false;
					causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : owner.temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					temporalMaterial.SetInt("causticTemporalMotionSource", (int)owner.temporalMotionSource);
					ParticleFluidRasterLayoutBindings.ApplyCausticHistoryGlobals(targetCommandBuffer, context.renderLayout.CausticRegion, previousCausticWorldCenter, previousCausticWorldSize);
					temporalMaterial.SetFloat("causticProjectedShadowOffset", owner.projectedShadowOffset);
					temporalMaterial.SetFloat("causticProjectedShadowExpansion", owner.projectedShadowExpansion);
					temporalMaterial.SetInt("causticProjectedShadowMapEnabled", useProjectedShadowMap ? 1 : 0);
					temporalMaterial.SetVector("causticProjectedShadowDirection", projectedShadowDirection);
					temporalMaterial.SetVector("causticProjectedShadowHistoryDirection", previousProjectedShadowDirection);
					temporalMaterial.SetTexture("CausticProjectedShadowMapTex", owner.projectedShadow.projectedShadowMapTexture);
					temporalMaterial.SetTexture("CausticProjectedShadowHistoryTex", owner.projectedShadow.projectedShadowMapHistoryTexture);
					temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(trace.causticResolvedTexture, causticTemporalTexture, temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture, temporalMaterial, CopyWithValidAlphaPass);
					causticTemporalFrameCount = nextFrameCount;
				}

				if (useProjectedShadowMap)
				{
					targetCommandBuffer.Blit(owner.projectedShadow.projectedShadowMapTexture, owner.projectedShadow.projectedShadowMapHistoryTexture);
				}

				previousCausticWorldCenter = context.renderLayout.CausticRegion.WorldCenter;
				previousCausticWorldSize = context.renderLayout.CausticRegion.WorldSize;
				previousProjectedShadowDirection = useProjectedShadowMap ? projectedShadowDirection : Vector2.zero;
				hasPreviousCausticCamera = true;
				return causticTemporalTexture;
			}

			hasPreviousCausticCamera = false;
			causticTemporalFrameCount = 0;
			previousProjectedShadowDirection = Vector2.zero;
			return trace.causticResolvedTexture;
		}
	}
}
