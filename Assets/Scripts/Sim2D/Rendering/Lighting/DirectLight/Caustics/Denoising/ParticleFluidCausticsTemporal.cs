using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	public sealed class ParticleFluidCausticsTemporal
	{
		public enum TemporalMotionSource
		{
			Static,
			ParticleMotion
		}
		
		private readonly ParticleFluidDirectLight _directLight;
		private bool _clearCausticHistory;
		private bool _hasPreviousCausticCamera;
		private int _causticTemporalFrameCount;
		private Vector2 _previousCausticWorldCenter;
		private Vector2 _previousCausticWorldSize;
		private Material _temporalMaterial;
		private Material _causticBlurMaterial;
		internal RenderTexture causticBlurTexture;
		internal RenderTexture causticHistoryTexture;
		internal RenderTexture causticTemporalTexture;
		private readonly ParticleFluidDirectLight.CausticsTemporalSettings _temporalSettings;

		public ParticleFluidCausticsTemporal(ParticleFluidDirectLight directLight)
		{
			_directLight = directLight;
			_temporalSettings = directLight.temporalSettings;
		}

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _temporalMaterial, _directLight.temporalShader);
			ParticleFluidRenderUtils.EnsureMaterial(ref _causticBlurMaterial, _directLight.blurShader);
		}

		private void EnsureResources(int width, int height, bool denoisingEnabled)
		{
			ComputeHelper.CreateRenderTexture(ref causticBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Caustic Blur");
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

		internal void EnsureTemporalResources(int causticWidth, int causticHeight, Bounds domainRegion, bool denoisingEnabled)
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
				_clearCausticHistory = true;
				_hasPreviousCausticCamera = false;
				_causticTemporalFrameCount = 0;
			}
		}

		internal void Release()
		{
			ComputeHelper.Release(causticBlurTexture, causticHistoryTexture, causticTemporalTexture);
			causticBlurTexture = null;
			causticHistoryTexture = null;
			causticTemporalTexture = null;
			_hasPreviousCausticCamera = false;
			_causticTemporalFrameCount = 0;
			_previousCausticWorldCenter = Vector2.zero;
			_previousCausticWorldSize = Vector2.zero;
			_clearCausticHistory = false;
			ParticleFluidRenderUtils.DestroyMaterial(ref _temporalMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref _causticBlurMaterial);
		}

		public void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityTexture)
		{
			if (_temporalMaterial == null)
			{
				return;
			}

			ParticleFluidLighting2D lighting = _directLight.particleFluidLighting2D;
			ParticleDisplay2D display = context.display;
			_temporalMaterial.SetTexture("VelocityTex", velocityTexture != null ? velocityTexture : Texture2D.blackTexture);
			_temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, lighting));
			_temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			_temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.isPaused ? 0.99f : _temporalSettings.temporalHistoryWeight);
			_temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", _temporalSettings.temporalHistoryClampStrength);
			_temporalMaterial.SetFloat("causticTemporalClampRejection", _temporalSettings.temporalClampRejection);
			_temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", _temporalSettings.temporalRejectedSpatialFilter);
			_temporalMaterial.SetInt("causticTemporalMotionSource", (int)_temporalSettings.temporalMotionSource);
			_temporalMaterial.SetTexture("MaterialTransportTex", lighting.materialTransportTexture);
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
			ParticleFluidPassBindings.ApplyCausticTemporalGlobals(commandBuffer, _directLight.particleFluidLighting2D.Display, domainRegion, oldWorldCenter, oldWorldSize);
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
		


		public void RecordBlur(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			ParticleFluidDirectLight.CausticsTraceSettings traceSettings = _directLight.traceSettings;
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;
			float rayTextureBlurScale = surface.renderTextureScale * _directLight.textureScale;
			float causticBlurRadius = traceSettings.blur * rayTextureBlurScale;
			ParticleFluidRenderUtils.GaussianBlur(targetCommandBuffer, causticBlurRadius, _causticBlurMaterial,_directLight.causticResolvedTexture, causticBlurTexture, "recordBlur");
		}

		public void RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer)
		{
			const int copyWithValidAlphaPass = 2;
			ParticleFluidLighting2D lightingOwner = _directLight.particleFluidLighting2D;
			ParticleDisplay2D display = context.display;
			_temporalMaterial?.SetTexture("MaterialTransportTex", lightingOwner.materialTransportTexture != null ? lightingOwner.materialTransportTexture : Texture2D.blackTexture);

			if (_temporalSettings.denoisingEnabled && _temporalMaterial != null)
			{
				if (_clearCausticHistory || !_hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(this._directLight.causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(this._directLight.causticResolvedTexture, causticHistoryTexture, _temporalMaterial, copyWithValidAlphaPass);
					_clearCausticHistory = false;
					_causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(_causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.isPaused ? 0.99f : _temporalSettings.temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					_temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					_temporalMaterial.SetInt("causticTemporalMotionSource", (int)_temporalSettings.temporalMotionSource);
					ParticleFluidPassBindings.ApplyCausticTemporalGlobals(targetCommandBuffer, display, context.renderRegion, _previousCausticWorldCenter, _previousCausticWorldSize);
					_temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					targetCommandBuffer.Blit(this._directLight.causticResolvedTexture, causticTemporalTexture, _temporalMaterial, 0);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture, _temporalMaterial, copyWithValidAlphaPass);
					_causticTemporalFrameCount = nextFrameCount;
				}

				_previousCausticWorldCenter = context.renderRegion.center;
				_previousCausticWorldSize = context.renderRegion.size;
				_hasPreviousCausticCamera = true;
				return;
			}

			_hasPreviousCausticCamera = false;
			_causticTemporalFrameCount = 0;
		}
	}
}
