using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidAnalyticBoundaryBindings
	{
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

	internal static class ParticleFluidRasterLayoutBindings
	{
		internal static void ApplyMetaballGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRegion)
		{
			commandBuffer.SetGlobalVector("metaballWorldCenter", domainRegion.WorldCenter);
			commandBuffer.SetGlobalVector("metaballWorldSize", domainRegion.WorldSize);
		}

		internal static void ApplyJumpFloodGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRenderRegion)
		{
			commandBuffer.SetGlobalVector("jumpFloodWorldCenter", domainRenderRegion.WorldCenter);
			commandBuffer.SetGlobalVector("jumpFloodWorldSize", domainRenderRegion.WorldSize);
		}

		internal static void ApplySoftLightGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRenderRegion)
		{
			commandBuffer.SetGlobalVector("softLightWorldCenter", domainRenderRegion.WorldCenter);
			commandBuffer.SetGlobalVector("softLightWorldSize", domainRenderRegion.WorldSize);
		}

		internal static void ApplyLightingGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRenderRegion)
		{
			commandBuffer.SetGlobalVector("particleFluidWorldCenter", domainRenderRegion.WorldCenter);
			commandBuffer.SetGlobalVector("particleFluidWorldSize", domainRenderRegion.WorldSize);
		}

		internal static void ApplyCausticCurrentGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRenderRegion)
		{
			commandBuffer.SetGlobalVector("causticCurrentWorldCenter", domainRenderRegion.WorldCenter);
			commandBuffer.SetGlobalVector("causticCurrentWorldSize", domainRenderRegion.WorldSize);
		}

		internal static void ApplyCausticHistoryGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D domainRegion, Vector2 historyWorldCenter, Vector2 historyWorldSize)
		{
			ApplyCausticCurrentGlobals(commandBuffer, domainRegion);
			commandBuffer.SetGlobalVector("causticHistoryWorldCenter", historyWorldCenter);
			commandBuffer.SetGlobalVector("causticHistoryWorldSize", historyWorldSize);
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
		internal static void ApplyGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting, float effectiveNormalStrength)
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
}
