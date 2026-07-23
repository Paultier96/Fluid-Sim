using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{

	internal static class ParticleFluidRenderBindings
	{
		private static readonly int DomainWorldCenter = Shader.PropertyToID("domainWorldCenter");
		private static readonly int DomainWorldSize = Shader.PropertyToID("domainWorldSize");
		private static readonly int UseEllipticalBounds = Shader.PropertyToID("useEllipticalBounds");
		private static readonly int EllipseBoundsCenter = Shader.PropertyToID("ellipseBoundsCenter");
		private static readonly int EllipseBoundsSize = Shader.PropertyToID("ellipseBoundsSize");
		private static readonly int ObstacleY = Shader.PropertyToID("obstacleY");
		private static readonly int AnalyticBoundaryExpansion = Shader.PropertyToID("analyticBoundaryExpansion");
		private static readonly int DensityThreshold = Shader.PropertyToID("densityThreshold");
		private static readonly int EdgeSoftness = Shader.PropertyToID("edgeSoftness");
		private static readonly int PhaseBlendWidth = Shader.PropertyToID("phaseBlendWidth");
		private static readonly int TransportPhaseBlendWidth = Shader.PropertyToID("transportPhaseBlendWidth");
		private static readonly int Phase0RenderBias = Shader.PropertyToID("phase0RenderBias");
		private static readonly int PhaseBiasNormalStrength = Shader.PropertyToID("phaseBiasNormalStrength");
		private static readonly int MetaballGhostBoundaryNormalStrength = Shader.PropertyToID("metaballGhostBoundaryNormalStrength");
		private static readonly int MetaballRefractionStrength = Shader.PropertyToID("metaballRefractionStrength");
		private static readonly int MetaballRefractionEdgeFade = Shader.PropertyToID("metaballRefractionEdgeFade");
		private static readonly int ScreenSpaceRefractionCanCrossPhases = Shader.PropertyToID("screenSpaceRefractionCanCrossPhases");
		private static readonly int ParticleNormalStrength = Shader.PropertyToID("particleNormalStrength");
		private static readonly int ParticleNormalProfileCurve = Shader.PropertyToID("particleNormalProfileCurve");
		private static readonly int GradientAtlas = Shader.PropertyToID("GradientAtlas");

		internal static void ApplyLayoutGlobals(CommandBuffer targetCommandBuffer, ParticleFluidAnalyticBoundary2D boundary, Bounds domainRegion)
		{
			ApplyBoundaryGlobals(targetCommandBuffer, boundary);
			ApplyDomainGlobals(targetCommandBuffer, domainRegion);
		}

		internal static void ApplyDomainGlobals(CommandBuffer commandBuffer, Bounds domainRegion)
		{
			commandBuffer.SetGlobalVector(DomainWorldCenter, domainRegion.center);
			commandBuffer.SetGlobalVector(DomainWorldSize, domainRegion.size);
		}

		internal static void ApplyBoundaryGlobals(CommandBuffer commandBuffer, ParticleFluidAnalyticBoundary2D boundary)
		{
			commandBuffer.SetGlobalInt(UseEllipticalBounds, boundary.useEllipticalBounds ? 1 : 0);
			commandBuffer.SetGlobalVector(EllipseBoundsCenter, boundary.BoundsCenter);
			commandBuffer.SetGlobalVector(EllipseBoundsSize, boundary.boundsSize);
			commandBuffer.SetGlobalFloat(ObstacleY, boundary.obstacleY);
			commandBuffer.SetGlobalFloat(AnalyticBoundaryExpansion, boundary.analyticBoundaryExpansion);
		}

		internal static void ApplyPhaseSplitGlobals(CommandBuffer commandBuffer, MetaballRenderer2D settings)
		{
			commandBuffer.SetGlobalFloat(DensityThreshold, settings.densityThreshold);
			commandBuffer.SetGlobalFloat(EdgeSoftness, settings.edgeSoftness);
			commandBuffer.SetGlobalFloat(PhaseBlendWidth, settings.phaseBlendWidth);
			commandBuffer.SetGlobalFloat(TransportPhaseBlendWidth, settings.transportPhaseBlendWidth);
			commandBuffer.SetGlobalFloat(Phase0RenderBias, settings.renderBias);
		}

		internal static void ApplyMetaballMaterialGlobals(CommandBuffer commandBuffer,  ParticleFluidLighting2D lighting, ParticleFluidLighting2D.FrameContext frameContext)
		{			
			ApplyLayoutGlobals(commandBuffer, frameContext.display.sim.analyticBoundary, frameContext.renderRegion);
			ApplyPhaseSplitGradientAndScalarGlobals(commandBuffer, frameContext.display, frameContext.cam, lighting);
		}

		internal static void ApplyMetaballDebugGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting)
		{
			ApplyBoundaryGlobals(commandBuffer, display.sim.analyticBoundary);
			ApplyPhaseSplitGradientAndScalarGlobals(commandBuffer, display, cam, lighting);
		}

		private static void ApplyPhaseSplitGradientAndScalarGlobals(CommandBuffer commandBuffer, ParticleDisplay2D display, Camera cam, ParticleFluidLighting2D lighting)
		{
			ApplyPhaseSplitGlobals(commandBuffer, display.metaballs);
			commandBuffer.SetGlobalTexture(GradientAtlas, display.gradientAtlasTexture != null ? display.gradientAtlasTexture : Texture2D.blackTexture);
			MetaballRenderer2D settings = display.metaballs;
			commandBuffer.SetGlobalFloat(PhaseBiasNormalStrength, settings.phaseBiasNormalStrength);
			commandBuffer.SetGlobalFloat(MetaballGhostBoundaryNormalStrength, settings.ghostBoundaryNormalStrength);
			commandBuffer.SetGlobalFloat(MetaballRefractionStrength, lighting.refractionStrength * display.GetZoomScale(cam));
			commandBuffer.SetGlobalFloat(MetaballRefractionEdgeFade, lighting.refractionEdgeFade);
			commandBuffer.SetGlobalInt(ScreenSpaceRefractionCanCrossPhases, lighting != null && lighting.screenSpaceRefractionCanCrossPhases ? 1 : 0);
			commandBuffer.SetGlobalFloat(ParticleNormalStrength, display.EffectiveNormalStrength);
			commandBuffer.SetGlobalFloat(ParticleNormalProfileCurve, settings.normalProfileCurve);
		}
	}
}
