using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidProjectedShadow
	{
		internal ComputeBuffer projectedShadowMapBuffer;
		internal RenderTexture projectedShadowMapTexture;
		internal RenderTexture projectedShadowMapHistoryTexture;

		internal readonly struct RecordParams
		{
			public readonly ComputeShader compute;
			public readonly ComputeBuffer projectedShadowMapBuffer;
			public readonly RenderTexture projectedShadowMapTexture;
			public readonly Texture combinedSourceTexture;
			public readonly int projectedShadowMapBins;
			public readonly float densityThreshold;
			public readonly float phase0RenderBias;
			public readonly Vector3 effectiveLightDirection;
			public readonly ParticleFluidRenderRegion2D sourceRegion;
			public readonly ParticleFluidRenderRegion2D shadowRegion;

			public RecordParams(
				ComputeShader compute,
				ComputeBuffer projectedShadowMapBuffer,
				RenderTexture projectedShadowMapTexture,
				Texture combinedSourceTexture,
				int projectedShadowMapBins,
				float densityThreshold,
				float phase0RenderBias,
				Vector3 effectiveLightDirection,
				ParticleFluidRenderRegion2D sourceRegion,
				ParticleFluidRenderRegion2D shadowRegion)
			{
				this.compute = compute;
				this.projectedShadowMapBuffer = projectedShadowMapBuffer;
				this.projectedShadowMapTexture = projectedShadowMapTexture;
				this.combinedSourceTexture = combinedSourceTexture;
				this.projectedShadowMapBins = projectedShadowMapBins;
				this.densityThreshold = densityThreshold;
				this.phase0RenderBias = phase0RenderBias;
				this.effectiveLightDirection = effectiveLightDirection;
				this.sourceRegion = sourceRegion;
				this.shadowRegion = shadowRegion;
			}
		}

		public bool RecordCurrentShadowMap(CommandBuffer targetCommandBuffer, in RecordParams parameters, out Vector2 shadowDirection)
		{
			shadowDirection = Vector2.zero;
			if (parameters.compute == null
				|| parameters.projectedShadowMapBuffer == null
				|| parameters.projectedShadowMapTexture == null
				|| parameters.combinedSourceTexture == null)
			{
				return false;
			}

			Vector2 lightXY = new Vector2(-parameters.effectiveLightDirection.x, -parameters.effectiveLightDirection.y);
			if (lightXY.sqrMagnitude <= 0.000001f)
			{
				return false;
			}

			shadowDirection = lightXY.normalized;
			ComputeShader compute = parameters.compute;
			int clearKernel = compute.FindKernel("ClearProjectedShadowMap");
			int buildKernel = compute.FindKernel("BuildProjectedShadowMap");
			int resolveKernel = compute.FindKernel("ResolveProjectedShadowMap");
			int binCount = parameters.projectedShadowMapBins;
			ParticleFluidRenderRegion2D sourceRegion = parameters.sourceRegion;
			ParticleFluidRenderRegion2D shadowRegion = parameters.shadowRegion;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", sourceRegion.PixelWidth);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", sourceRegion.PixelHeight);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", parameters.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", parameters.phase0RenderBias);
			targetCommandBuffer.SetComputeIntParam(compute, "projectedShadowMapBinCount", binCount);
			targetCommandBuffer.SetComputeVectorParam(compute, "projectedShadowMapDirection", new Vector4(shadowDirection.x, shadowDirection.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "projectedShadowSourceWorldCenter", new Vector4(sourceRegion.WorldCenter.x, sourceRegion.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "projectedShadowSourceWorldSize", new Vector4(sourceRegion.WorldSize.x, sourceRegion.WorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "projectedShadowWorldCenter", new Vector4(shadowRegion.WorldCenter.x, shadowRegion.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "projectedShadowWorldSize", new Vector4(shadowRegion.WorldSize.x, shadowRegion.WorldSize.y, 0f, 0f));

			targetCommandBuffer.SetComputeBufferParam(compute, clearKernel, "ProjectedShadowMapAccum", parameters.projectedShadowMapBuffer);
			targetCommandBuffer.DispatchCompute(compute, clearKernel, Mathf.CeilToInt(binCount / 64f), 1, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, buildKernel, "CombinedTex", parameters.combinedSourceTexture);
			targetCommandBuffer.SetComputeBufferParam(compute, buildKernel, "ProjectedShadowMapAccum", parameters.projectedShadowMapBuffer);
			targetCommandBuffer.DispatchCompute(
				compute,
				buildKernel,
				Mathf.CeilToInt(sourceRegion.PixelWidth / 16f),
				Mathf.CeilToInt(sourceRegion.PixelHeight / 16f),
				1);

			targetCommandBuffer.SetComputeBufferParam(compute, resolveKernel, "ProjectedShadowMapAccum", parameters.projectedShadowMapBuffer);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "ProjectedShadowMapResult", parameters.projectedShadowMapTexture);
			targetCommandBuffer.DispatchCompute(compute, resolveKernel, Mathf.CeilToInt(binCount / 64f), 1, 1);
			return true;
		}

		internal void EnsureResources(bool enabled, int binCount)
		{
			if (enabled)
			{
				ComputeHelper.CreateStructuredBuffer<uint>(ref projectedShadowMapBuffer, binCount);
				ComputeHelper.CreateRenderTexture(ref projectedShadowMapTexture, binCount, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Projected Shadow Map");
				ComputeHelper.CreateRenderTexture(ref projectedShadowMapHistoryTexture, binCount, 1, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Projected Shadow Map History");
				return;
			}

			ComputeHelper.Release(projectedShadowMapBuffer);
			ComputeHelper.Release(projectedShadowMapTexture, projectedShadowMapHistoryTexture);
			projectedShadowMapBuffer = null;
			projectedShadowMapTexture = null;
			projectedShadowMapHistoryTexture = null;
		}

		internal void Release()
		{
			EnsureResources(false, 0);
		}

		internal bool ShouldRender(ParticleFluidDirectLight owner)
		{
			if (owner == null)
			{
				return false;
			}

			ParticleFluidLight2D light = owner.Owner.lightManager.LightSlots[0];
			return owner.lightingMode == ParticleFluidLighting2D.LightingMode.Shadows
			       && owner.projectedShadowCompute != null
			       && light is ParticleFluidDirectionalLight2D directionalLight
			       && directionalLight.isActiveAndEnabled;
		}

		internal void ApplyToMaterial(Material material, bool enabled, Vector2 direction, float offset, float expansion)
		{
			if (material == null)
			{
				return;
			}

			material.SetInt("particleFluidProjectedShadowEnabled", enabled ? 1 : 0);
			material.SetTexture("ProjectedShadowTex", enabled && projectedShadowMapTexture != null ? projectedShadowMapTexture : Texture2D.blackTexture);
			material.SetVector("particleFluidProjectedShadowDirection", new Vector4(direction.x, direction.y, 0f, 0f));
			material.SetFloat("particleFluidProjectedShadowOffset", offset);
			material.SetFloat("particleFluidProjectedShadowExpansion", expansion);
		}
	}
}
