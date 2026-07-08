using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTemporal
	{
		private readonly ParticleFluidCausticsTrace _trace;
		private bool _clearCausticHistory;
		private bool _hasPreviousCausticCamera;
		private int _causticTemporalFrameCount;
		private Vector2 _previousCausticWorldCenter;
		private Vector2 _previousCausticWorldSize;
		private Vector2 _previousProjectedShadowDirection;
		private Material _temporalMaterial;
		private Material _causticBlurMaterial;
		private Material _causticMotionBlurMaterial;
		internal RenderTexture causticBlurTexture;
		internal RenderTexture causticMotionDilatedTexture;
		internal RenderTexture causticMotionDilationScratchTexture;
		internal RenderTexture causticHistoryTexture;
		internal RenderTexture causticTemporalTexture;
		private readonly ParticleFluidDirectLight.CausticsTemporalSettings _temporalSettings;

		public ParticleFluidCausticsTemporal(ParticleFluidCausticsTrace trace)
		{
			_trace = trace;
			ParticleFluidDirectLight owner = _trace.owner;
			_temporalSettings = owner.temporalSettings;
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _temporalMaterial, _trace.owner.temporalShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _causticBlurMaterial, _trace.owner.blurShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _causticMotionBlurMaterial, _trace.owner.blurShader);
		}

		private void EnsureResources(int width, int height, bool denoisingEnabled)
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

		internal void EnsureTemporalResources(int causticWidth, int causticHeight, Bounds domainRegion, bool denoisingEnabled, bool useProjectedShadowMap)
		{
			bool hadCausticHistory = causticHistoryTexture != null && causticHistoryTexture.IsCreated();
			bool causticHistoryResize = hadCausticHistory
				&& (causticHistoryTexture.width != causticWidth || causticHistoryTexture.height != causticHeight);
			EnsureResources(causticWidth, causticHeight, denoisingEnabled);
			if (denoisingEnabled)
			{
				bool canMigrateHistory = _hasPreviousCausticCamera && _previousCausticWorldSize.x > 0f && _previousCausticWorldSize.y > 0f;
				RenderTexture causticHistoryBackup = canMigrateHistory && causticHistoryResize
					? CreateHistoryBackup(causticHistoryTexture, "Particle2D Caustic History Backup")
					: null;

				bool historyChanged = !hadCausticHistory || causticHistoryResize;
				bool migratedHistory = false;
				if (causticHistoryBackup != null)
				{
					migratedHistory = TryMigrateHistoryOnResize(causticHistoryBackup, causticHistoryTexture, _previousCausticWorldCenter, _previousCausticWorldSize, domainRegion);
				}

				ComputeHelper.Release(causticHistoryBackup);

				bool historyMigrationFailed = historyChanged && !migratedHistory;
				if (migratedHistory)
				{
					_previousCausticWorldCenter = domainRegion.center;
					_previousCausticWorldSize = domainRegion.size;
				}
				_clearCausticHistory |= historyMigrationFailed;
			}
			else
			{
				EnsureResources(causticWidth, causticHeight, false);
				_trace.owner.projectedShadow.Release();
				_previousProjectedShadowDirection = Vector2.zero;
				_clearCausticHistory = true;
				_hasPreviousCausticCamera = false;
				_causticTemporalFrameCount = 0;
			}

			if (!useProjectedShadowMap)
			{
				_previousProjectedShadowDirection = Vector2.zero;
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
			_hasPreviousCausticCamera = false;
			_causticTemporalFrameCount = 0;
			_previousCausticWorldCenter = Vector2.zero;
			_previousCausticWorldSize = Vector2.zero;
			_previousProjectedShadowDirection = Vector2.zero;
			_clearCausticHistory = false;
			ParticleFluidRenderUtils.DestroyMaterial(ref _temporalMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _causticBlurMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _causticMotionBlurMaterial);
		}

		public void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityPhase0AccumulationTexture, Texture velocityPhase1AccumulationTexture)
		{
			if (_temporalMaterial == null)
			{
				return;
			}

			ParticleFluidLighting2D lightingOwner = _trace.owner.Owner;
			ParticleDisplay2D display = context.display;
			_temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture != null ? velocityPhase0AccumulationTexture : Texture2D.blackTexture);
			_temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture != null ? velocityPhase1AccumulationTexture : Texture2D.blackTexture);
			_temporalMaterial.SetTexture("CausticMotionTex", _trace.causticMotionTexture != null ? _trace.causticMotionTexture : Texture2D.blackTexture);
			_temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, lightingOwner));
			_temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			_temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : _temporalSettings.temporalHistoryWeight);
			_temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", _temporalSettings.temporalHistoryClampStrength);
			_temporalMaterial.SetFloat("causticTemporalClampRejection", _temporalSettings.temporalClampRejection);
			_temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", _temporalSettings.temporalRejectedSpatialFilter);
			_temporalMaterial.SetInt("causticTemporalMotionSource", (int)_temporalSettings.temporalMotionSource);
			_temporalMaterial.SetTexture("MaterialTransportTex", lightingOwner.materialTransportTexture != null ? lightingOwner.materialTransportTexture : Texture2D.blackTexture);
		}

		private bool TryMigrateHistoryOnResize(RenderTexture oldHistory, RenderTexture newHistory, Vector2 oldWorldCenter, Vector2 oldWorldSize, Bounds domainRegion)
		{
			if (_temporalMaterial == null
				|| oldHistory == null
				|| newHistory == null
				|| !_hasPreviousCausticCamera
				|| oldHistory.width <= 0
				|| oldHistory.height <= 0)
			{
				return false;
			}

			CommandBuffer commandBuffer = CommandBufferPool.Get("Particle2D Reproject History");
			ParticleFluidPassBindings.ApplyCausticTemporalGlobals(commandBuffer, _trace.owner.Owner.Display, domainRegion, oldWorldCenter, oldWorldSize);
			commandBuffer.Blit(oldHistory, newHistory, _temporalMaterial, 2);
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
		


		public void RecordBlurAndMotion(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleFluidDirectLight owner = _trace.owner;
			ParticleFluidDirectLight.CausticsTraceSettings traceSettings = owner.traceSettings;
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			float rayTextureBlurScale = surface.renderTextureScale * owner.textureScale;
			float causticBlurRadius = traceSettings.blur * rayTextureBlurScale;
			float temporalMotionBlurRadius = _temporalSettings.temporalMotionBlur * rayTextureBlurScale;
			if (causticBlurRadius > 0.001f)
			{
				ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, causticBlurRadius, _causticBlurMaterial,_trace.causticResolvedTexture, causticBlurTexture);
			}

			RenderTexture temporalMotionTexture = _trace.causticMotionTexture;
			bool useCausticMotion = _temporalMaterial != null && _temporalSettings.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (useCausticMotion && _temporalSettings.temporalMotionDilationRadius > 0.001f && causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null)
			{
				int dilationIterations = Mathf.Max(1, _temporalSettings.temporalMotionDilationIterations);
				float motionDilationRadius = _temporalSettings.temporalMotionDilationRadius * rayTextureBlurScale / dilationIterations;
				ParticleFluidLayoutBindings.ApplyDomainGlobals(targetCommandBuffer, context.renderRegion);
				RenderTexture dilationSource = _trace.causticMotionTexture;
				RenderTexture dilationTarget = causticMotionDilatedTexture;
				for (int i = 0; i < dilationIterations; i++)
				{
					_temporalMaterial.SetFloat("causticMotionDilationRadius", motionDilationRadius);
					targetCommandBuffer.Blit(dilationSource, dilationTarget, _temporalMaterial, 1);
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
				ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, temporalMotionBlurRadius, _causticMotionBlurMaterial,temporalMotionTexture,motionBlurScratch);
			}
			_temporalMaterial?.SetTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : Texture2D.blackTexture);
			targetCommandBuffer.SetGlobalTexture("CausticMotionTex", temporalMotionTexture != null ? temporalMotionTexture : _trace.causticMotionTexture);
		}

		public RenderTexture GetTemporalMotionTextureAfterBlur()
		{
			bool useCausticMotion = _temporalMaterial != null && _temporalSettings.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion;
			if (!useCausticMotion)
			{
				return _trace.causticMotionTexture;
			}

			bool hasDilationTargets = causticMotionDilatedTexture != null && causticMotionDilationScratchTexture != null;
			if (_temporalSettings.temporalMotionDilationRadius > 0.001f && hasDilationTargets)
			{
				int dilationIterations = Mathf.Max(1, _temporalSettings.temporalMotionDilationIterations);
				return dilationIterations % 2 == 1 ? causticMotionDilatedTexture : causticMotionDilationScratchTexture;
			}

			return _trace.causticMotionTexture;
		}

		public void RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, RenderTexture temporalMotionTexture)
		{
			const int copyWithValidAlphaPass = 3;
			ParticleFluidDirectLight owner = _trace.owner;
			ParticleFluidDirectLight.ProjectedShadowSettings projectedShadowSettings = owner.projectedShadowSettings;
			ParticleFluidLighting2D lightingOwner = owner.Owner;
			ParticleDisplay2D display = context.display;
			bool wantsProjectedShadowMap = _temporalSettings.projectedShadowHistoryRejection || _temporalSettings.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.ProjectedShadow;
			Vector2 projectedShadowDirection = Vector2.zero;
			bool useProjectedShadowMap = false;
			_temporalMaterial?.SetTexture("MaterialTransportTex", lightingOwner.materialTransportTexture != null ? lightingOwner.materialTransportTexture : Texture2D.blackTexture);
			if (wantsProjectedShadowMap && lightingOwner.lightManager.GetMainDirectionalLight() is { } directionalLight)
			{
				Vector3 effectiveLightDirection = directionalLight.GetBoundaryRefractedDirection(lightingOwner.PhaseMaterials[1].indexOfRefraction, context.display.sim.analyticBoundary);
				ParticleFluidProjectedShadow.RecordParams projectedShadowParams = new(
					owner.projectedShadowCompute,
					owner.projectedShadow.projectedShadowMapBuffer,
					owner.projectedShadow.projectedShadowMapTexture,
					lightingOwner.materialTransportTexture,
					projectedShadowSettings.mapBins,
					effectiveLightDirection,
					context.renderRegion,
					context.sourceSize);
				useProjectedShadowMap = owner.projectedShadow.RecordCurrentShadowMap(targetCommandBuffer, projectedShadowParams, out projectedShadowDirection);
			}

			if (_temporalSettings.denoisingEnabled && _temporalMaterial != null)
			{
				if (_clearCausticHistory || !_hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(_trace.causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(_trace.causticResolvedTexture, causticHistoryTexture, _temporalMaterial, copyWithValidAlphaPass);
					_clearCausticHistory = false;
					_causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(_causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.IsPaused ? 0.99f : _temporalSettings.temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					_temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					_temporalMaterial.SetInt("causticTemporalMotionSource", (int)_temporalSettings.temporalMotionSource);
					ParticleFluidPassBindings.ApplyCausticTemporalGlobals(targetCommandBuffer, display, context.renderRegion, _previousCausticWorldCenter, _previousCausticWorldSize);
					_temporalMaterial.SetFloat("causticProjectedShadowOffset", projectedShadowSettings.offset);
					_temporalMaterial.SetFloat("causticProjectedShadowExpansion", projectedShadowSettings.expansion);
					_temporalMaterial.SetInt("causticProjectedShadowMapEnabled", useProjectedShadowMap ? 1 : 0);
					_temporalMaterial.SetVector("causticProjectedShadowDirection", projectedShadowDirection);
					_temporalMaterial.SetVector("causticProjectedShadowHistoryDirection", _previousProjectedShadowDirection);
					_temporalMaterial.SetTexture("CausticProjectedShadowMapTex", owner.projectedShadow.projectedShadowMapTexture);
					_temporalMaterial.SetTexture("CausticProjectedShadowHistoryTex", owner.projectedShadow.projectedShadowMapHistoryTexture);
					_temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					_temporalMaterial.SetTexture("CausticMotionTex", temporalMotionTexture);
					targetCommandBuffer.Blit(_trace.causticResolvedTexture, causticTemporalTexture, _temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture, _temporalMaterial, copyWithValidAlphaPass);
					_causticTemporalFrameCount = nextFrameCount;
				}

				if (useProjectedShadowMap)
				{
					targetCommandBuffer.Blit(owner.projectedShadow.projectedShadowMapTexture, owner.projectedShadow.projectedShadowMapHistoryTexture);
				}

				_previousCausticWorldCenter = context.renderRegion.center;
				_previousCausticWorldSize = context.renderRegion.size;
				_previousProjectedShadowDirection = useProjectedShadowMap ? projectedShadowDirection : Vector2.zero;
				_hasPreviousCausticCamera = true;
				return;
			}

			_hasPreviousCausticCamera = false;
			_causticTemporalFrameCount = 0;
			_previousProjectedShadowDirection = Vector2.zero;
		}
	}
}

