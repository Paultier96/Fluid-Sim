using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTemporal
	{
		readonly ParticleFluidLighting2D owner;

		public ParticleFluidCausticsTemporal(ParticleFluidLighting2D owner)
		{
			this.owner = owner;
		}

		public bool TryMigrateHistoryOnResize(RenderTexture oldHistory, RenderTexture newHistory, Vector2 oldWorldCenter, Vector2 oldWorldSize, Vector2 newWorldCenter, Vector2 newWorldSize)
		{
			if (owner.temporalMaterial == null
				|| oldHistory == null
				|| newHistory == null
				|| !owner.hasPreviousCausticCamera
				|| oldHistory.width <= 0
				|| oldHistory.height <= 0)
			{
				return false;
			}

			CommandBuffer commandBuffer = CommandBufferPool.Get("Particle2D Reproject History");
			owner.temporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(newWorldCenter.x, newWorldCenter.y, 0f, 0f));
			owner.temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(newWorldSize.x, newWorldSize.y, 0f, 0f));
			owner.temporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(oldWorldCenter.x, oldWorldCenter.y, 0f, 0f));
			owner.temporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(oldWorldSize.x, oldWorldSize.y, 0f, 0f));
			commandBuffer.Blit(oldHistory, newHistory, owner.temporalMaterial, 2);
			Graphics.ExecuteCommandBuffer(commandBuffer);
			CommandBufferPool.Release(commandBuffer);
			return true;
		}

		public RenderTexture RecordBlurAndMotion(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			float rayTextureBlurScale = owner.GetRayTextureBlurScale(surface);
			float causticBlurRadius = owner.blur * rayTextureBlurScale;
			float temporalMotionBlurRadius = owner.temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				owner.causticBlurMaterial.SetFloat("blurRadius", causticBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(owner.causticResolvedTexture, owner.causticBlurTexture, owner.causticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(owner.causticBlurTexture, owner.causticResolvedTexture, owner.causticBlurMaterial);
			}

			RenderTexture temporalMotionTexture = owner.causticMotionTexture;
			bool useCausticMotion = owner.temporalMaterial != null && owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && owner.temporalMotionDilationRadius > 0.001f && owner.causticMotionDilatedTexture != null && owner.causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, owner.temporalMotionDilationIterations);
				float motionDilationRadius = owner.temporalMotionDilationRadius * rayTextureBlurScale / dilationIterations;
				Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
				owner.temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = owner.causticMotionTexture;
				RenderTexture dilationTarget = owner.causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					owner.temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, owner.temporalMaterial, 1);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == owner.causticMotionDilatedTexture ? owner.causticMotionDilationScratchTexture : owner.causticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && owner.causticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == owner.causticMotionDilationScratchTexture
					? owner.causticMotionDilatedTexture
					: owner.causticMotionDilationScratchTexture;
				owner.causticMotionBlurMaterial.SetFloat("blurRadius", temporalMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(temporalMotionTexture, motionBlurScratch, owner.causticMotionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(motionBlurScratch, temporalMotionTexture, owner.causticMotionBlurMaterial);
			}
			owner.temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : owner.causticMotionTexture);
			return temporalMotionTexture;
		}

		public RenderTexture GetTemporalMotionTextureAfterBlur()
		{
			bool useCausticMotion = owner.temporalMaterial != null && owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (!useCausticMotion)
			{
				return owner.causticMotionTexture;
			}

			bool hasDilationTargets = owner.causticMotionDilatedTexture != null && owner.causticMotionDilationScratchTexture != null;
			if (owner.temporalMotionDilationRadius > 0.001f && hasDilationTargets)
			{
				int dilationIterations = Mathf.Max(1, owner.temporalMotionDilationIterations);
				return dilationIterations % 2 == 1 ? owner.causticMotionDilatedTexture : owner.causticMotionDilationScratchTexture;
			}

			return owner.causticMotionTexture;
		}

		public Texture RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture temporalMotionTexture)
		{
			const int CopyWithValidAlphaPass = 3;
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			bool wantsProjectedShadowMap =
				(owner.projectedShadowHistoryRejection || owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.ProjectedShadow);
			Vector2 projectedShadowDirection = Vector2.zero;
			bool useProjectedShadowMap = false;
			if (wantsProjectedShadowMap
			    && owner.lightSlots[0] is ParticleFluidDirectionalLight2D directionalLight
			    && directionalLight.isActiveAndEnabled)
			{
				Vector3 effectiveLightDirection = directionalLight.GetDirectLightingDirection(owner, context.display);
				ParticleFluidProjectedShadow.RecordParams projectedShadowParams = new(
					owner.projectedShadowCompute,
					owner.projectedShadowMapBuffer,
					owner.projectedShadowMapTexture,
					owner.combinedSourceTexture,
					owner.projectedShadowMapBins,
					display.metaballs.densityThreshold,
					display.metaballs.phase0RenderBias,
					effectiveLightDirection,
					context.renderLayout.Source,
					context.renderLayout.Caustic);
				useProjectedShadowMap = owner.projectedShadow.RecordCurrentShadowMap(targetCommandBuffer, projectedShadowParams, out projectedShadowDirection);
			}

			if (owner.denoisingEnabled && owner.temporalMaterial != null)
			{
				if (owner.clearCausticHistory || !owner.hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(owner.causticResolvedTexture, owner.causticTemporalTexture);
					targetCommandBuffer.Blit(owner.causticResolvedTexture, owner.causticHistoryTexture, owner.temporalMaterial, CopyWithValidAlphaPass);
					owner.clearCausticHistory = false;
					owner.causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(owner.causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : owner.temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					owner.temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					owner.temporalMaterial.SetInt("causticTemporalMotionSource", (int)owner.temporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(owner.previousCausticWorldCenter.x, owner.previousCausticWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(owner.previousCausticWorldSize.x, owner.previousCausticWorldSize.y, 0f, 0f));
					owner.temporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					owner.temporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					owner.temporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(owner.previousCausticWorldCenter.x, owner.previousCausticWorldCenter.y, 0f, 0f));
					owner.temporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(owner.previousCausticWorldSize.x, owner.previousCausticWorldSize.y, 0f, 0f));
					owner.temporalMaterial.SetFloat("causticProjectedShadowOffset", owner.projectedShadowOffset);
					owner.temporalMaterial.SetFloat("causticProjectedShadowExpansion", owner.projectedShadowExpansion);
					owner.temporalMaterial.SetInt("causticProjectedShadowMapEnabled", useProjectedShadowMap ? 1 : 0);
					owner.temporalMaterial.SetVector("causticProjectedShadowDirection", new Vector4(projectedShadowDirection.x, projectedShadowDirection.y, 0f, 0f));
					owner.temporalMaterial.SetVector("causticProjectedShadowHistoryDirection", new Vector4(owner.previousProjectedShadowDirection.x, owner.previousProjectedShadowDirection.y, 0f, 0f));
					owner.temporalMaterial.SetTexture("CausticProjectedShadowMapTex", owner.projectedShadowMapTexture);
					owner.temporalMaterial.SetTexture("CausticProjectedShadowHistoryTex", owner.projectedShadowMapHistoryTexture);
					owner.temporalMaterial.SetTexture("CausticHistoryTex", owner.causticHistoryTexture);
					owner.temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(owner.causticResolvedTexture, owner.causticTemporalTexture, owner.temporalMaterial, 0);
					targetCommandBuffer.Blit(owner.causticTemporalTexture, owner.causticHistoryTexture, owner.temporalMaterial, CopyWithValidAlphaPass);
					owner.causticTemporalFrameCount = nextFrameCount;
				}

				if (useProjectedShadowMap)
				{
					targetCommandBuffer.Blit(owner.projectedShadowMapTexture, owner.projectedShadowMapHistoryTexture);
				}

				owner.previousCausticWorldCenter = currentWorldCenter;
				owner.previousCausticWorldSize = currentWorldSize;
				owner.previousProjectedShadowDirection = useProjectedShadowMap ? projectedShadowDirection : Vector2.zero;
				owner.hasPreviousCausticCamera = true;
				return owner.causticTemporalTexture;
			}

			owner.hasPreviousCausticCamera = false;
			owner.causticTemporalFrameCount = 0;
			owner.previousProjectedShadowDirection = Vector2.zero;
			return owner.causticResolvedTexture;
		}
	}
}
