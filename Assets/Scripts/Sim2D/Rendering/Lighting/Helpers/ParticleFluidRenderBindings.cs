using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal static class ParticleFluidAnalyticBoundaryBindings
	{
		internal static void ApplyGlobals(CommandBuffer commandBuffer, ParticleFluidAnalyticBoundary2D boundary)
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
			commandBuffer.SetGlobalFloat("phase0RenderBias", settings.phase0RenderBias);
		}
	}

	internal static class ParticleFluidRasterLayoutBindings
	{
		internal static void ApplyMetaballGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D region, Vector4 sourceUvRect)
		{
			commandBuffer.SetGlobalVector("metaballWorldCenter", region.WorldCenter);
			commandBuffer.SetGlobalVector("metaballWorldSize", region.WorldSize);
			commandBuffer.SetGlobalVector("metaballSourceUvRect", sourceUvRect);
		}

		internal static void ApplyJumpFloodGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D region)
		{
			commandBuffer.SetGlobalVector("jumpFloodWorldCenter", region.WorldCenter);
			commandBuffer.SetGlobalVector("jumpFloodWorldSize", region.WorldSize);
			commandBuffer.SetGlobalVector("jumpFloodCompositeUvRect", region.SourceUvRect);
		}

		internal static void ApplySoftLightGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D region, Vector4 sourceUvRect)
		{
			commandBuffer.SetGlobalVector("softLightWorldCenter", region.WorldCenter);
			commandBuffer.SetGlobalVector("softLightWorldSize", region.WorldSize);
			commandBuffer.SetGlobalVector("softLightSourceUvRect", sourceUvRect);
		}

		internal static void ApplyLightingGlobals(CommandBuffer commandBuffer, ParticleFluidRenderRegion2D materialRegion, ParticleFluidRenderRegion2D causticRegion)
		{
			commandBuffer.SetGlobalVector("particleFluidWorldCenter", materialRegion.WorldCenter);
			commandBuffer.SetGlobalVector("particleFluidWorldSize", materialRegion.WorldSize);
			commandBuffer.SetGlobalVector("particleFluidCompositeUvRect", materialRegion.SourceUvRect);
			commandBuffer.SetGlobalVector("particleFluidCameraUvRect", materialRegion.SourceUvRect);
			commandBuffer.SetGlobalVector("particleFluidClipRect", materialRegion.SourceUvRect);
			commandBuffer.SetGlobalVector("particleFluidCausticUvRect", causticRegion.SourceUvRect);
		}

		internal static void ApplyCausticCurrentGlobals(CommandBuffer commandBuffer, Vector2 currentWorldCenter, Vector2 currentWorldSize)
		{
			commandBuffer.SetGlobalVector("causticCurrentWorldCenter", currentWorldCenter);
			commandBuffer.SetGlobalVector("causticCurrentWorldSize", currentWorldSize);
		}

		internal static void ApplyCausticHistoryGlobals(CommandBuffer commandBuffer, Vector2 currentWorldCenter, Vector2 currentWorldSize, Vector2 historyWorldCenter, Vector2 historyWorldSize)
		{
			ApplyCausticCurrentGlobals(commandBuffer, currentWorldCenter, currentWorldSize);
			commandBuffer.SetGlobalVector("causticHistoryWorldCenter", historyWorldCenter);
			commandBuffer.SetGlobalVector("causticHistoryWorldSize", historyWorldSize);
		}
	}

	internal static class ParticleFluidRasterTextureBindings
	{
		internal static void ApplyGradientGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display)
		{
			commandBuffer.SetGlobalTexture("ColourMap", display != null && display.gradientTexture != null ? display.gradientTexture : Texture2D.blackTexture);
			commandBuffer.SetGlobalTexture("ColourMap2", display != null && display.gradientTexture2 != null ? display.gradientTexture2 : Texture2D.blackTexture);
		}

		internal static void ApplyDebugGradientGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display)
		{
			commandBuffer.SetGlobalTexture("DebugHeatMap", display != null && display.debugHeatMapTexture != null ? display.debugHeatMapTexture : Texture2D.blackTexture);
			commandBuffer.SetGlobalTexture("DebugSignedHeatMap", display != null && display.debugSignedHeatMapTexture != null ? display.debugSignedHeatMapTexture : Texture2D.blackTexture);
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
