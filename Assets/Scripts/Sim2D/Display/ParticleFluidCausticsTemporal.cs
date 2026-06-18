using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTemporal
	{
		readonly ParticleFluidCausticsView caustics;

		public ParticleFluidCausticsTemporal(ParticleFluidCausticsView caustics)
		{
			this.caustics = caustics;
		}

		public void ClearHistory()
		{
			caustics.ClearHistory = true;
			caustics.HasPreviousCamera = false;
			caustics.TemporalFrameCount = 0;
		}

		public RenderTexture RecordBlurAndMotion(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			bool renderDirectionalLightField = caustics.ShouldRenderDirectionalLightField();
			float rayTextureBlurScale = caustics.GetRayTextureBlurScale(surface);
			float causticBlurRadius = caustics.Blur * rayTextureBlurScale;
			float directionalLightFieldBlurRadius = caustics.DirectionalLightFieldBlur * rayTextureBlurScale;
			float temporalMotionBlurRadius = caustics.TemporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				caustics.CausticBlurMaterial.SetFloat("blurRadius", causticBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(caustics.CausticResolvedTexture, caustics.CausticBlurTexture, caustics.CausticBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(caustics.CausticBlurTexture, caustics.CausticResolvedTexture, caustics.CausticBlurMaterial);
			}
			if (renderDirectionalLightField && directionalLightFieldBlurRadius > 0.001f)
			{
				caustics.LightDirectionBlurMaterial.SetFloat("blurRadius", directionalLightFieldBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(caustics.LightDirectionTexture, caustics.LightDirectionBlurTexture, caustics.LightDirectionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(caustics.LightDirectionBlurTexture, caustics.LightDirectionTexture, caustics.LightDirectionBlurMaterial);
			}

			RenderTexture temporalMotionTexture = caustics.CausticMotionTexture;
			bool useCausticMotion = caustics.TemporalMaterial != null && caustics.TemporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && caustics.TemporalMotionDilationRadius > 0.001f && caustics.CausticMotionDilatedTexture != null && caustics.CausticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, caustics.TemporalMotionDilationIterations);
				float motionDilationRadius = caustics.TemporalMotionDilationRadius * rayTextureBlurScale / dilationIterations;
				Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;
				caustics.TemporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
				RenderTexture dilationSource = caustics.CausticMotionTexture;
				RenderTexture dilationTarget = caustics.CausticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					caustics.TemporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, caustics.TemporalMaterial, 1);
					temporalMotionTexture = dilationTarget;
					dilationSource = dilationTarget;
					dilationTarget = dilationTarget == caustics.CausticMotionDilatedTexture ? caustics.CausticMotionDilationScratchTexture : caustics.CausticMotionDilatedTexture;
				}
			}
			if (useCausticMotion && temporalMotionBlurRadius > 0.001f && temporalMotionTexture != null && caustics.CausticMotionDilationScratchTexture != null)
			{
				RenderTexture motionBlurScratch = temporalMotionTexture == caustics.CausticMotionDilationScratchTexture
					? caustics.CausticMotionDilatedTexture
					: caustics.CausticMotionDilationScratchTexture;
				caustics.CausticMotionBlurMaterial.SetFloat("blurRadius", temporalMotionBlurRadius);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
				targetCommandBuffer.Blit(temporalMotionTexture, motionBlurScratch, caustics.CausticMotionBlurMaterial);
				targetCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
				targetCommandBuffer.Blit(motionBlurScratch, temporalMotionTexture, caustics.CausticMotionBlurMaterial);
			}
			caustics.TemporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : caustics.CausticMotionTexture);
			return temporalMotionTexture;
		}

		public RenderTexture GetTemporalMotionTextureAfterBlur()
		{
			bool useCausticMotion = caustics.TemporalMaterial != null && caustics.TemporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (!useCausticMotion)
			{
				return caustics.CausticMotionTexture;
			}

			bool hasDilationTargets = caustics.CausticMotionDilatedTexture != null && caustics.CausticMotionDilationScratchTexture != null;
			if (caustics.TemporalMotionDilationRadius > 0.001f && hasDilationTargets)
			{
				int dilationIterations = Mathf.Max(1, caustics.TemporalMotionDilationIterations);
				return dilationIterations % 2 == 1 ? caustics.CausticMotionDilatedTexture : caustics.CausticMotionDilationScratchTexture;
			}

			return caustics.CausticMotionTexture;
		}

		public Texture RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture temporalMotionTexture)
		{
			ParticleDisplay2D display = context.display;
			bool renderDirectionalLightField = caustics.ShouldRenderDirectionalLightField();
			Vector2 currentWorldCenter = context.causticRenderRegion.WorldCenter;
			Vector2 currentWorldSize = context.causticRenderRegion.WorldSize;

			if (caustics.DenoisingEnabled && caustics.TemporalMaterial != null)
			{
				if (caustics.ClearHistory || !caustics.HasPreviousCamera)
				{
					targetCommandBuffer.Blit(caustics.CausticResolvedTexture, caustics.CausticTemporalTexture);
					targetCommandBuffer.Blit(caustics.CausticResolvedTexture, caustics.CausticHistoryTexture);
					if (renderDirectionalLightField)
					{
						targetCommandBuffer.Blit(caustics.LightDirectionTexture, caustics.LightDirectionTemporalTexture);
						targetCommandBuffer.Blit(caustics.LightDirectionTexture, caustics.LightDirectionHistoryTexture);
					}
					caustics.ClearHistory = false;
					caustics.TemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(caustics.TemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : caustics.TemporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					caustics.TemporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					caustics.TemporalMaterial.SetInt("causticTemporalMotionSource", (int)caustics.TemporalMotionSource);
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", new Vector4(caustics.PreviousWorldCenter.x, caustics.PreviousWorldCenter.y, 0f, 0f));
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", new Vector4(caustics.PreviousWorldSize.x, caustics.PreviousWorldSize.y, 0f, 0f));
					caustics.TemporalMaterial.SetVector("causticCurrentWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
					caustics.TemporalMaterial.SetVector("causticCurrentWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
					caustics.TemporalMaterial.SetVector("causticHistoryWorldCenter", new Vector4(caustics.PreviousWorldCenter.x, caustics.PreviousWorldCenter.y, 0f, 0f));
					caustics.TemporalMaterial.SetVector("causticHistoryWorldSize", new Vector4(caustics.PreviousWorldSize.x, caustics.PreviousWorldSize.y, 0f, 0f));
					caustics.TemporalMaterial.SetTexture("CausticHistoryTex", caustics.CausticHistoryTexture);
					caustics.TemporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(caustics.CausticResolvedTexture, caustics.CausticTemporalTexture, caustics.TemporalMaterial, 0);
					targetCommandBuffer.Blit(caustics.CausticTemporalTexture, caustics.CausticHistoryTexture);
					if (renderDirectionalLightField)
					{
						caustics.TemporalMaterial.SetTexture("CausticHistoryTex", caustics.LightDirectionHistoryTexture);
						caustics.TemporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
						targetCommandBuffer.Blit(caustics.LightDirectionTexture, caustics.LightDirectionTemporalTexture, caustics.TemporalMaterial, 0);
						targetCommandBuffer.Blit(caustics.LightDirectionTemporalTexture, caustics.LightDirectionHistoryTexture);
					}
					caustics.TemporalFrameCount = nextFrameCount;
				}

				caustics.PreviousWorldCenter = currentWorldCenter;
				caustics.PreviousWorldSize = currentWorldSize;
				caustics.HasPreviousCamera = true;
				return caustics.CausticTemporalTexture;
			}

			caustics.HasPreviousCamera = false;
			caustics.TemporalFrameCount = 0;
			return caustics.CausticResolvedTexture;
		}
	}
}
