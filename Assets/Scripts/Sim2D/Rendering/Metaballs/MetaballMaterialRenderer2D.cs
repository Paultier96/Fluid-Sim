using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class MetaballMaterialRenderer2D
	{
		const int AlbedoPass = 0;
		const int NormalPass = 1;
		const int UnlitPass = 2;

		Material material;
		readonly ParticleFluidMaterialMapSet materialMaps = new ();

		public ParticleFluidMaterialMapSet MaterialMaps => materialMaps;
		public bool IsReady => material != null && materialMaps.IsAllocated;

		public void EnsureMaterial(Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			if (material != null)
			{
				Object.DestroyImmediate(material);
			}

			material = new Material(shader);
		}

		public void EnsureRenderTextures(ParticleFluidRenderRegion2D renderRegion)
		{
			materialMaps.EnsureRenderTextures(renderRegion.PixelWidth, renderRegion.PixelHeight, "Particle2D");
		}

		public void ApplySettings(
			ParticleDisplay2D display,
			Camera cam,
			RenderTexture combinedTexture,
			RenderTexture normalTexture,
			ParticleFluidRenderRegion2D renderRegion,
			float analyticBoundaryExpansion,
			float effectiveNormalStrength,
			ParticleFluidLighting2D lighting)
		{
			if (material == null)
			{
				return;
			}

			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			material.SetTexture("CombinedTex", combinedTexture);
			material.SetTexture("NormalTex", normalTexture);
			material.SetTexture("ColourMap", display.gradientTexture);
			material.SetTexture("ColourMap2", display.gradientTexture2);
			material.SetFloat("densityThreshold", settings.densityThreshold);
			material.SetFloat("edgeSoftness", settings.edgeSoftness);
			material.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			material.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			material.SetFloat("phaseBiasNormalStrength", settings.phaseBiasNormalStrength);
			material.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			material.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			material.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			material.SetFloat("obstacleY", display.sim.obstacleY);
			material.SetVector("metaballWorldCenter", new Vector4(renderRegion.WorldCenter.x, renderRegion.WorldCenter.y, 0f, 0f));
			material.SetVector("metaballWorldSize", new Vector4(renderRegion.WorldSize.x, renderRegion.WorldSize.y, 0f, 0f));
			material.SetVector("metaballSourceUvRect", new Vector4(0f, 0f, 1f, 1f));
			material.SetFloat("analyticBoundaryExpansion", analyticBoundaryExpansion);
			material.SetFloat("metaballGhostBoundaryNormalStrength", settings.ghostBoundaryNormalStrength);
			material.SetFloat("metaballRefractionStrength", (lighting != null ? lighting.refractionStrength : 0f) * display.GetZoomScale(cam));
			material.SetFloat("metaballRefractionEdgeFade", lighting != null ? lighting.refractionEdgeFade : 0f);
			material.SetInt("screenSpaceRefractionCanCrossPhases", lighting != null && lighting.screenSpaceRefractionCanCrossPhases ? 1 : 0);
			material.SetFloat("particleNormalStrength", effectiveNormalStrength);
			material.SetFloat("particleNormalProfileCurve", settings.normalProfileCurve);
		}

		public void Render(CommandBuffer commandBuffer)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}
			materialMaps.Render(commandBuffer, material, AlbedoPass, NormalPass);
		}

		public void RenderUnlit(CommandBuffer commandBuffer, RenderTargetIdentifier finalTarget, ParticleFluidRenderRegion2D renderRegion)
		{
			if (!IsReady || commandBuffer == null)
			{
				return;
			}
			materialMaps.RenderUnlit(commandBuffer, material, finalTarget, UnlitPass, renderRegion);
		}

		public void Release()
		{
			materialMaps.Release();
			if (material != null)
			{
				Object.DestroyImmediate(material);
				material = null;
			}
		}
	}
}
