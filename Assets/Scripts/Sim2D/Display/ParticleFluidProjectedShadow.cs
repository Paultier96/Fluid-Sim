using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidProjectedShadow
	{
		readonly ParticleFluidCausticsView caustics;

		public ParticleFluidProjectedShadow(ParticleFluidCausticsView caustics)
		{
			this.caustics = caustics;
		}

		public bool RecordCurrentShadowMap(ParticleFluidLighting2D.FrameContext context, CommandBuffer targetCommandBuffer, out Vector2 shadowDirection)
		{
			shadowDirection = Vector2.zero;
			if (caustics.ComputeShader == null
				|| caustics.ReactiveShadowMapBuffer == null
				|| caustics.ReactiveShadowMapTexture == null
				|| caustics.CombinedSourceTexture == null
				|| !caustics.PrimaryLight.enabled
				|| caustics.PrimaryLight.type != ParticleFluidLighting2D.DirectionalLightSettings.LightType.Directional)
			{
				return false;
			}

			Vector3 effectiveLightDirection = caustics.GetDirectLightingDirection(context.display, caustics.PrimaryLight.Direction);
			Vector2 lightXY = new Vector2(-effectiveLightDirection.x, -effectiveLightDirection.y);
			if (lightXY.sqrMagnitude <= 0.000001f)
			{
				return false;
			}

			shadowDirection = lightXY.normalized;
			ComputeShader compute = caustics.ComputeShader;
			int clearKernel = compute.FindKernel("ClearReactiveShadowMap");
			int buildKernel = compute.FindKernel("BuildReactiveShadowMap");
			int resolveKernel = compute.FindKernel("ResolveReactiveShadowMap");
			int binCount = caustics.ReactiveShadowMapBins;
			ParticleFluidRenderRegion2D sourceRegion = context.sourceRenderRegion;
			ParticleFluidRenderRegion2D reactiveRegion = context.causticRenderRegion;
			ParticleDisplay2D.MetaballSettings surface = context.display.metaballs;

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", sourceRegion.PixelWidth);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", sourceRegion.PixelHeight);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeIntParam(compute, "reactiveShadowMapBinCount", binCount);
			targetCommandBuffer.SetComputeVectorParam(compute, "reactiveShadowMapDirection", new Vector4(shadowDirection.x, shadowDirection.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "reactiveShadowSourceWorldCenter", new Vector4(sourceRegion.WorldCenter.x, sourceRegion.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "reactiveShadowSourceWorldSize", new Vector4(sourceRegion.WorldSize.x, sourceRegion.WorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "reactiveShadowWorldCenter", new Vector4(reactiveRegion.WorldCenter.x, reactiveRegion.WorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "reactiveShadowWorldSize", new Vector4(reactiveRegion.WorldSize.x, reactiveRegion.WorldSize.y, 0f, 0f));

			targetCommandBuffer.SetComputeBufferParam(compute, clearKernel, "ReactiveShadowMapAccum", caustics.ReactiveShadowMapBuffer);
			targetCommandBuffer.DispatchCompute(compute, clearKernel, Mathf.CeilToInt(binCount / 64f), 1, 1);

			targetCommandBuffer.SetComputeTextureParam(compute, buildKernel, "CombinedTex", caustics.CombinedSourceTexture);
			targetCommandBuffer.SetComputeBufferParam(compute, buildKernel, "ReactiveShadowMapAccum", caustics.ReactiveShadowMapBuffer);
			targetCommandBuffer.DispatchCompute(
				compute,
				buildKernel,
				Mathf.CeilToInt(sourceRegion.PixelWidth / 16f),
				Mathf.CeilToInt(sourceRegion.PixelHeight / 16f),
				1);

			targetCommandBuffer.SetComputeBufferParam(compute, resolveKernel, "ReactiveShadowMapAccum", caustics.ReactiveShadowMapBuffer);
			targetCommandBuffer.SetComputeTextureParam(compute, resolveKernel, "ReactiveShadowMapResult", caustics.ReactiveShadowMapTexture);
			targetCommandBuffer.DispatchCompute(compute, resolveKernel, Mathf.CeilToInt(binCount / 64f), 1, 1);
			return true;
		}
	}
}
