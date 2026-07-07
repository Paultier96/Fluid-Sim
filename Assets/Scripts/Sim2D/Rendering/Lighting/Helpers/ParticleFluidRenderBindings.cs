using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{

	internal static class ParticleFluidLayoutBindings
	{

		internal static void ApplyLayoutGlobals(CommandBuffer targetCommandBuffer, ParticleFluidAnalyticBoundary2D boundary, ParticleFluidRenderRegion2D domainRegion)
		{
			ApplyBoundaryGlobals(targetCommandBuffer, boundary);
			ApplyDomainGlobals(targetCommandBuffer, domainRegion);
		}

		internal static void ApplyDomainGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRegion)
		{
			commandBuffer.SetGlobalVector("domainWorldCenter", domainRegion.WorldCenter);
			commandBuffer.SetGlobalVector("domainWorldSize", domainRegion.WorldSize);
		}

		internal static void ApplyBoundaryGlobals(CommandBuffer commandBuffer, ParticleFluidAnalyticBoundary2D boundary)
		{
			commandBuffer.SetGlobalInt("useEllipticalBounds", boundary.useEllipticalBounds ? 1 : 0);
			commandBuffer.SetGlobalVector("ellipseBoundsCenter", boundary.ellipseBoundsCenter);
			commandBuffer.SetGlobalVector("ellipseBoundsSize", boundary.ellipseBoundsSize);
			commandBuffer.SetGlobalFloat("obstacleY", boundary.obstacleY);
			commandBuffer.SetGlobalFloat("analyticBoundaryExpansion", boundary.analyticBoundaryExpansion);
		}

		internal static void ApplyPhaseSplitGlobals(CommandBuffer commandBuffer, ParticleDisplay2D.MetaballSettings settings)
		{
			commandBuffer.SetGlobalFloat("densityThreshold", settings.densityThreshold);
			commandBuffer.SetGlobalFloat("edgeSoftness", settings.edgeSoftness);
			commandBuffer.SetGlobalFloat("phaseBlendWidth", settings.phaseBlendWidth);
			commandBuffer.SetGlobalFloat("transportPhaseBlendWidth", settings.transportPhaseBlendWidth);
			commandBuffer.SetGlobalFloat("phase0RenderBias", settings.phase0RenderBias);
		}
	}

	internal static class ParticleFluidRasterTextureBindings
	{
		private static readonly int ColourMap = Shader.PropertyToID("ColourMap");
		private static readonly int ColourMap2 = Shader.PropertyToID("ColourMap2");
		private static readonly int DebugHeatMap = Shader.PropertyToID("DebugHeatMap");
		private static readonly int DebugSignedHeatMap = Shader.PropertyToID("DebugSignedHeatMap");

		internal static void ApplyGradientGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display)
		{
			commandBuffer.SetGlobalTexture(ColourMap, display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture);
			commandBuffer.SetGlobalTexture(ColourMap2, display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture);
		}

		internal static void ApplyDebugGradientGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display)
		{
			commandBuffer.SetGlobalTexture(DebugHeatMap, display.debugHeatMapTexture != null ? display.debugHeatMapTexture : Texture2D.blackTexture);
			commandBuffer.SetGlobalTexture(DebugSignedHeatMap, display.debugSignedHeatMapTexture != null ? display.debugSignedHeatMapTexture : Texture2D.blackTexture);
		}
	}

	internal static class ParticleFluidMetaballScalarBindings
	{
		internal static void ApplyScalarGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting, float effectiveNormalStrength)
		{
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			commandBuffer.SetGlobalFloat("phaseBiasNormalStrength", settings.phaseBiasNormalStrength);
			commandBuffer.SetGlobalFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			commandBuffer.SetGlobalFloat("metaballRefractionStrength", (lighting != null ? lighting.refractionStrength : 0f) * display.GetZoomScale(cam));
			commandBuffer.SetGlobalFloat("metaballRefractionEdgeFade", lighting != null ? lighting.refractionEdgeFade : 0f);
			commandBuffer.SetGlobalInt("screenSpaceRefractionCanCrossPhases", lighting != null && lighting.screenSpaceRefractionCanCrossPhases ? 1 : 0);
			commandBuffer.SetGlobalFloat("particleNormalStrength", effectiveNormalStrength);
			commandBuffer.SetGlobalFloat("particleNormalProfileCurve", settings.normalProfileCurve);
		}
	}

	internal static class ParticleFluidPassBindings
	{
		internal static void ApplyMetaballMaterialGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting, ParticleFluidRenderRegion2D domainRegion, float effectiveNormalStrength)
		{
			ParticleFluidLayoutBindings.ApplyPhaseSplitGlobals(commandBuffer, display.metaballs);
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(commandBuffer, display.sim.analyticBoundary, domainRegion);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(commandBuffer, display);
			ParticleFluidMetaballScalarBindings.ApplyScalarGlobals(commandBuffer, display, cam, lighting, effectiveNormalStrength);
		}

		internal static void ApplyMetaballDebugGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting, float effectiveNormalStrength)
		{
			ParticleFluidLayoutBindings.ApplyBoundaryGlobals(commandBuffer, display.sim.analyticBoundary);
			ParticleFluidLayoutBindings.ApplyPhaseSplitGlobals(commandBuffer, display.metaballs);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(commandBuffer, display);
			ParticleFluidRasterTextureBindings.ApplyDebugGradientGlobals(commandBuffer, display);
			ParticleFluidMetaballScalarBindings.ApplyScalarGlobals(commandBuffer, display, cam, lighting, effectiveNormalStrength);
		}

		internal static void ApplyFinalLightingGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, ParticleFluidRenderRegion2D domainRegion)
		{
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(commandBuffer, display.sim.analyticBoundary, domainRegion);
			ParticleFluidLayoutBindings.ApplyPhaseSplitGlobals(commandBuffer, display.metaballs);
			ParticleFluidRasterTextureBindings.ApplyGradientGlobals(commandBuffer, display);
		}

		internal static void ApplyCausticTemporalGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, ParticleFluidRenderRegion2D domainRegion, Vector2 historyWorldCenter, Vector2 historyWorldSize)
		{
			ParticleFluidLayoutBindings.ApplyPhaseSplitGlobals(commandBuffer, display.metaballs);
			ParticleFluidLayoutBindings.ApplyDomainGlobals(commandBuffer, domainRegion);
			commandBuffer.SetGlobalVector("causticHistoryWorldCenter", historyWorldCenter);
			commandBuffer.SetGlobalVector("causticHistoryWorldSize", historyWorldSize);
		}
	}
}
