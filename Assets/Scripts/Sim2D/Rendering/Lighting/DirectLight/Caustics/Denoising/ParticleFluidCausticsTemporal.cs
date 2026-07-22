using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[System.Serializable]
	public sealed class ParticleFluidCausticsTemporal
	{
		public enum TemporalMotionSource
		{
			Static,
			ParticleMotion
		}

		public bool denoisingEnabled = true;
		[Range(0f, 0.99f)] public float temporalHistoryWeight = 0.95f;
		[Range(0f, 1f)] public float temporalHistoryClampStrength = 0.6f;
		[Min(0f)] public float temporalClampRejection = 0.5f;
		[Range(0f, 1f)] public float temporalRejectedSpatialFilter = 1f;
		public TemporalMotionSource temporalMotionSource = TemporalMotionSource.Static;
		[Min(0)] public float motionBlurRadius = 6;
		
		public Shader temporalShader;
		private const int TemporalResolvePass = 0;
		private const int ReprojectHistoryPass = 1;
		private const int CopyWithValidAlphaPass = 2;
		private bool _clearCausticHistory;
		private bool _hasPreviousCausticCamera;
		private int _causticTemporalFrameCount;
		private Vector2 _previousCausticWorldCenter;
		private Vector2 _previousCausticWorldSize;
		private Material _temporalMaterial;
		internal RenderTexture causticHistoryTexture;
		internal RenderTexture causticTemporalTexture;

		internal void EnsureMaterials()
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _temporalMaterial, temporalShader);
		}

		private void EnsureResources(int width, int height)
		{
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

		internal void EnsureTemporalResources(int causticWidth, int causticHeight, Bounds domainRegion)
		{
			bool hadCausticHistory = causticHistoryTexture != null && causticHistoryTexture.IsCreated();
			bool causticHistoryResize = hadCausticHistory
				&& (causticHistoryTexture.width != causticWidth || causticHistoryTexture.height != causticHeight);
			bool canMigrateHistory = denoisingEnabled
			                         && causticHistoryResize
			                         && _hasPreviousCausticCamera
			                         && _previousCausticWorldSize.x > 0f
			                         && _previousCausticWorldSize.y > 0f;
			RenderTexture causticHistoryBackup = canMigrateHistory
				? CreateHistoryBackup(causticHistoryTexture, "Particle2D Caustic History Backup")
				: null;

			EnsureResources(causticWidth, causticHeight);
			if (denoisingEnabled)
			{
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
				_clearCausticHistory = true;
				_hasPreviousCausticCamera = false;
				_causticTemporalFrameCount = 0;
			}
		}

		internal void Release()
		{
			ComputeHelper.Release(causticHistoryTexture, causticTemporalTexture);
			causticHistoryTexture = null;
			causticTemporalTexture = null;
			_hasPreviousCausticCamera = false;
			_causticTemporalFrameCount = 0;
			_previousCausticWorldCenter = Vector2.zero;
			_previousCausticWorldSize = Vector2.zero;
			_clearCausticHistory = false;
			ParticleFluidRenderUtils.DestroyMaterial(ref _temporalMaterial);
		}

		public void ApplyTemporalSettings(ParticleFluidLighting2D.FrameContext context, Texture velocityTexture)
		{
			if (_temporalMaterial == null)
			{
				return;
			}

			ParticleDisplay2D display = context.display;
			_temporalMaterial.SetTexture("VelocityTex", velocityTexture != null ? velocityTexture : Texture2D.blackTexture);
			_temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			_temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.isPaused ? 0.99f : temporalHistoryWeight);
			_temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", temporalHistoryClampStrength);
			_temporalMaterial.SetFloat("causticTemporalClampRejection", temporalClampRejection);
			_temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", temporalRejectedSpatialFilter);
			_temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
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
			ParticleFluidRenderBindings.ApplyDomainGlobals(commandBuffer, domainRegion);
			commandBuffer.SetGlobalVector("causticHistoryWorldCenter", oldWorldCenter);
			commandBuffer.SetGlobalVector("causticHistoryWorldSize", oldWorldSize);
			commandBuffer.Blit(oldHistory, newHistory, _temporalMaterial, ReprojectHistoryPass);
			Graphics.ExecuteCommandBuffer(commandBuffer);
			CommandBufferPool.Release(commandBuffer);
			return true;
		}

		private static RenderTexture CreateHistoryBackup(RenderTexture source, string name)
		{
			if (source == null || !source.IsCreated() || source.width <= 0 || source.height <= 0)
			{
				return null;
			}

			RenderTexture backup = ComputeHelper.CreateRenderTexture(source.width, source.height, source.filterMode, source.graphicsFormat, name);
			Graphics.Blit(source, backup);
			return backup;
		}
		
		public void RecordTemporal(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, Texture causticResolvedTexture)
		{
			ParticleDisplay2D display = context.display;

			if (denoisingEnabled && _temporalMaterial != null)
			{
				if (_clearCausticHistory || !_hasPreviousCausticCamera)
				{
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticHistoryTexture, _temporalMaterial, CopyWithValidAlphaPass);
					_clearCausticHistory = false;
					_causticTemporalFrameCount = 1;
				}
				else
				{
					int nextFrameCount = Mathf.Max(_causticTemporalFrameCount + 1, 2);
					float targetHistoryWeight = display.sim.isPaused ? 0.99f : temporalHistoryWeight;
					float warmupHistoryWeight = (nextFrameCount - 1f) / nextFrameCount;
					_temporalMaterial.SetFloat("causticTemporalHistoryWeight", Mathf.Min(targetHistoryWeight, warmupHistoryWeight));
					_temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
					ParticleFluidRenderBindings.ApplyDomainGlobals(targetCommandBuffer, context.renderRegion);
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldCenter", _previousCausticWorldCenter);
					targetCommandBuffer.SetGlobalVector("causticHistoryWorldSize", _previousCausticWorldSize);
					_temporalMaterial.SetTexture("CausticHistoryTex", causticHistoryTexture);
					targetCommandBuffer.Blit(causticResolvedTexture, causticTemporalTexture, _temporalMaterial, TemporalResolvePass);
					targetCommandBuffer.Blit(causticTemporalTexture, causticHistoryTexture, _temporalMaterial, CopyWithValidAlphaPass);
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
		
		public bool ShouldRenderVelocityTextures(ParticleDisplay2D display)
		{
			return display.debugMode == ParticleDisplay2D.DebugVisualization.ParticleMotion
			       || denoisingEnabled
			           && temporalMotionSource == TemporalMotionSource.ParticleMotion;
		}
	}
}
