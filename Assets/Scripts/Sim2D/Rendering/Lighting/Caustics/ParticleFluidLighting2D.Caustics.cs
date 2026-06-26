using System;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleFluidLighting2D
	{
		internal float GetRayTextureBlurScale(ParticleDisplay2D.MetaballSettings surface)
		{
			return Mathf.Max(surface.renderTextureScale * textureScale, 0.0001f);
		}

		internal static Vector4 GetCausticMultiplier(Color color, float intensity)
		{
			float clampedIntensity = Mathf.Max(intensity, 0f);
			return new Vector4(
				color.r * clampedIntensity,
				color.g * clampedIntensity,
				color.b * clampedIntensity,
				0f
			);
		}

		public int ReserveCausticsFrameIndex()
		{
			return causticFrameIndex++;
		}

		public Texture GetSharpCausticsTexture()
		{
			if (!ShouldRenderCaustics())
			{
				return Texture2D.blackTexture;
			}

			Texture texture = denoisingEnabled ? (Texture)causticTemporalTexture : causticResolvedTexture;
			return texture != null ? texture : Texture2D.blackTexture;
		}

		internal void GetCausticPointRaySpan(FrameContext context, FluidLightSettings light, out float angleStart, out float angleRange)
		{
			ParticleDisplay2D display = context.display;
			angleStart = 0f;
			angleRange = TwoPi;
			if (display == null
			    || light == null
			    || light.type != FluidLightSettings.LightType.Point
			    || !display.sim.useEllipticalBounds)
			{
				return;
			}

			Vector2 point = light.point.position;
			float expansion = MetaballRenderer2D.GetAnalyticBoundaryExpansion(display);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			float cutY = display.sim.obstacleY - expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f || IsInsideAnalyticBoundary(point, center, radii, cutY))
			{
				return;
			}

			int angleCount = 0;
			for (int i = 0; i < PointLightBoundaryEllipseSamples; i++)
			{
				float t = i / (float)PointLightBoundaryEllipseSamples * TwoPi;
				Vector2 boundaryPoint = center + new Vector2(Mathf.Cos(t) * radii.x, Mathf.Sin(t) * radii.y);
				if (boundaryPoint.y >= cutY)
				{
					AddPointLightBoundaryAngle(point, boundaryPoint, ref angleCount);
				}
			}

			float cutRelY = cutY - center.y;
			if (Mathf.Abs(cutRelY) <= radii.y)
			{
				float cutHalfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY / (radii.y * radii.y)));
				for (int i = 0; i < PointLightBoundaryCutSamples; i++)
				{
					float t = PointLightBoundaryCutSamples > 1 ? i / (float)(PointLightBoundaryCutSamples - 1) : 0.5f;
					AddPointLightBoundaryAngle(point, new Vector2(center.x + Mathf.Lerp(-cutHalfWidth, cutHalfWidth, t), cutY), ref angleCount);
				}
			}

			if (angleCount < 2)
			{
				return;
			}

			System.Array.Sort(pointLightBoundaryAngles, 0, angleCount);
			float largestGap = -1f;
			int largestGapIndex = 0;
			for (int i = 0; i < angleCount; i++)
			{
				float current = pointLightBoundaryAngles[i];
				float next = i == angleCount - 1 ? pointLightBoundaryAngles[0] + TwoPi : pointLightBoundaryAngles[i + 1];
				float gap = next - current;
				if (gap > largestGap)
				{
					largestGap = gap;
					largestGapIndex = i;
				}
			}

			float padding = 2f * Mathf.Deg2Rad;
			angleStart = Mathf.Repeat(pointLightBoundaryAngles[(largestGapIndex + 1) % angleCount] - padding, TwoPi);
			angleRange = Mathf.Clamp(TwoPi - largestGap + padding * 2f, 0.0001f, TwoPi);
		}

		void AddPointLightBoundaryAngle(Vector2 lightPoint, Vector2 boundaryPoint, ref int angleCount)
		{
			if (angleCount >= pointLightBoundaryAngles.Length)
			{
				return;
			}

			Vector2 delta = boundaryPoint - lightPoint;
			if (delta.sqrMagnitude <= 0.000001f)
			{
				return;
			}

			pointLightBoundaryAngles[angleCount++] = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x), TwoPi);
		}

		static bool IsInsideAnalyticBoundary(Vector2 point, Vector2 center, Vector2 radii, float cutY)
		{
			if (point.y < cutY)
			{
				return false;
			}

			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			return ellipseValue <= 1f;
		}
		
		void ApplyTemporalSettings(FrameContext context, RenderTexture combinedAccumulationTexture, Texture velocityPhase0AccumulationTexture, Texture velocityPhase1AccumulationTexture)
		{
			if (temporalMaterial == null)
			{
				return;
			}
			ParticleDisplay2D display = context.display;
			ParticleDisplay2D.MetaballSettings settings = display.metaballs;
			temporalMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			temporalMaterial.SetTexture("VelocityTex0", velocityPhase0AccumulationTexture != null ? velocityPhase0AccumulationTexture : Texture2D.blackTexture);
			temporalMaterial.SetTexture("VelocityTex1", velocityPhase1AccumulationTexture != null ? velocityPhase1AccumulationTexture : Texture2D.blackTexture);
			temporalMaterial.SetTexture("CausticMotionTex", causticMotionTexture != null ? causticMotionTexture : Texture2D.blackTexture);
			temporalMaterial.SetFloat("densityThreshold", settings.densityThreshold);
			temporalMaterial.SetFloat("phaseBlendWidth", settings.phaseBlendWidth);
			temporalMaterial.SetFloat("phase0RenderBias", settings.phase0RenderBias);
			temporalMaterial.SetInt("debugMode", MetaballRenderer2D.GetDebugShaderMode(display, this));
			temporalMaterial.SetFloat("motionDebugDeltaTime", display.sim.CurrentSimulationDeltaTime);
			temporalMaterial.SetFloat("causticTemporalMotionVelocityThreshold", motionVelocityThreshold);
			temporalMaterial.SetFloat("causticTemporalHistoryWeight", display.sim.IsPaused ? 0.99f : temporalHistoryWeight);
			temporalMaterial.SetFloat("causticTemporalHistoryClampStrength", temporalHistoryClampStrength);
			temporalMaterial.SetFloat("causticTemporalClampRejection", temporalClampRejection);
			temporalMaterial.SetFloat("causticTemporalRejectedSpatialFilter", temporalRejectedSpatialFilter);
			temporalMaterial.SetInt("causticTemporalMotionSource", (int)temporalMotionSource);
		}

		public float GetCausticDebugExposure()
		{
			float exposure = 0f;
			foreach (FluidLightSettings light in lights)
			{
				if (SupportsCausticRaymarch(light))
				{
					exposure += Luminance(light.EffectiveColor) * Mathf.Max(light.intensity, 0f);
				}
			}
			return Mathf.Max(exposure, 0.0001f);
		}

		static float Luminance(Color colour)
		{
			return colour.r * 0.2126f + colour.g * 0.7152f + colour.b * 0.0722f;
		}

		internal static float GetSaturationDispersionScale(Color color)
		{
			float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
			float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}

		internal static void GetLightRayShares(float primaryWeight, float secondaryWeight, float tertiaryWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare)
		{
			float totalWeight = primaryWeight + secondaryWeight + tertiaryWeight;
			if (totalWeight > 0.0001f)
			{
				primaryShare = primaryWeight / totalWeight;
				secondaryShare = secondaryWeight / totalWeight;
				tertiaryShare = tertiaryWeight / totalWeight;
				return;
			}

			primaryShare = 0f;
			secondaryShare = 0f;
			tertiaryShare = 0f;
		}

		internal static float LightSampleWeight(FluidLightSettings light)
		{
			if (!SupportsCausticRaymarch(light))
			{
				return 0f;
			}
			return Mathf.Max(0f, Luminance(light.EffectiveColor) * light.intensity * light.sampleBias);
		}

		internal static bool SupportsCausticRaymarch(FluidLightSettings light)
		{
			return light != null
			       && light.enabled
			       && light.intensity > 0f;
		}

		internal bool AnyEnabledCausticPointLight()
		{
			foreach (FluidLightSettings light in lights)
			{
				if (IsWeightedPointLight(light))
				{
					return true;
				}
			}

			return false;
		}

		static bool IsWeightedPointLight(FluidLightSettings light)
		{
			return light != null
			       && light.type == FluidLightSettings.LightType.Point
			       && LightSampleWeight(light) > 0f;
		}

		internal void GetCausticRayRange(FrameContext context, int width, int height, Vector3 lightDirection, float angularRadiusDegrees, out float startOffset, out int rayCount)
		{
			ParticleDisplay2D display = context.display;
			Vector2 worldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 worldSize = context.renderLayout.Caustic.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			Vector2 lightXY = new Vector2(-lightDirection.x, -lightDirection.y);
			Vector2 rayDir = lightXY.sqrMagnitude > 0.0001f ? lightXY.normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(width * width + height * height);
			float screenMinOffset = -fullSpan * 0.5f;
			float screenMaxOffset = fullSpan * 0.5f;
			startOffset = screenMinOffset;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!display.sim.useEllipticalBounds)
			{
				return;
			}

			float minOffset = float.PositiveInfinity;
			float maxOffset = float.NegativeInfinity;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * analyticBoundaryExpansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return;
			}

			for (int i = 0; i < 128; i++)
			{
				float angle = i * Mathf.PI * 2f / 128f;
				Vector2 world = display.sim.ellipseBoundsCenter + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
				if (world.y >= display.sim.obstacleY - analyticBoundaryExpansion)
				{
					IncludeCausticLaunchPoint(world, worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				}
			}

			float expandedObstacleY = display.sim.obstacleY - analyticBoundaryExpansion;
			float cutRelY = (expandedObstacleY - display.sim.ellipseBoundsCenter.y) / radii.y;
			if (Mathf.Abs(cutRelY) <= 1f)
			{
				float cutX = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY));
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x - cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
				IncludeCausticLaunchPoint(new Vector2(display.sim.ellipseBoundsCenter.x + cutX, expandedObstacleY), worldCenter, worldSize, width, height, tangent, ref minOffset, ref maxOffset);
			}

			if (float.IsNaN(minOffset) || float.IsInfinity(minOffset) || float.IsNaN(maxOffset) || float.IsInfinity(maxOffset))
			{
				return;
			}

			float angularPadding = Mathf.Sin(angularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(4f + angularPadding, 2f);
			float clippedMinOffset = Mathf.Max(minOffset - padding, screenMinOffset);
			float clippedMaxOffset = Mathf.Min(maxOffset + padding, screenMaxOffset);
			if (clippedMaxOffset <= clippedMinOffset)
			{
				rayCount = 0;
				return;
			}

			startOffset = Mathf.Floor(clippedMinOffset);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(clippedMaxOffset - startOffset));
		}

		void IncludeCausticLaunchPoint(Vector2 world, Vector2 worldCenter, Vector2 worldSize, int width, int height, Vector2 tangent, ref float minOffset, ref float maxOffset)
		{
			Vector2 uv = new Vector2(
				(world.x - worldCenter.x) / Mathf.Max(worldSize.x, 0.0001f) + 0.5f,
				(world.y - worldCenter.y) / Mathf.Max(worldSize.y, 0.0001f) + 0.5f
			);
			Vector2 pixel = new Vector2(uv.x * width, uv.y * height);
			Vector2 centredPixel = pixel - new Vector2(width, height) * 0.5f;
			float offset = Vector2.Dot(centredPixel, tangent);
			minOffset = Mathf.Min(minOffset, offset);
			maxOffset = Mathf.Max(maxOffset, offset);
		}
		
		internal static int GetCausticPointRayCount(FluidLightSettings light, Vector2 worldSize, int width, int height)
		{
			if (light == null)
			{
				return 0;
			}

			float pixelsPerWorldUnit = Mathf.Max(
				width / Mathf.Max(worldSize.x, 0.0001f),
				height / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(light.point.range, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2 * radiusPixels));
		}
		
		public void Release()
		{
			if (lightingMaterial != null)
			{
				DestroyImmediate(lightingMaterial);
				lightingMaterial = null;
			}

			if (radianceCascadeMaterial != null)
			{
				DestroyImmediate(radianceCascadeMaterial);
				radianceCascadeMaterial = null;
			}
			if (radianceCascadeSdfMaterial != null)
			{
				DestroyImmediate(radianceCascadeSdfMaterial);
				radianceCascadeSdfMaterial = null;
			}
			if (temporalMaterial != null)
			{
				DestroyImmediate(temporalMaterial);
				temporalMaterial = null;
			}
			if (causticBlurMaterial != null)
			{
				DestroyImmediate(causticBlurMaterial);
				causticBlurMaterial = null;
			}

			if (causticMotionBlurMaterial != null)
			{
				DestroyImmediate(causticMotionBlurMaterial);
				causticMotionBlurMaterial = null;
			}

			ComputeHelper.Release(causticAccumulationBuffer, causticMotionAccumulationBuffer, projectedShadowMapBuffer);
			causticAccumulationBuffer = null;
			causticMotionAccumulationBuffer = null;
			projectedShadowMapBuffer = null;
			ComputeHelper.Release(causticResolvedTexture, causticBlurTexture, causticMotionTexture, causticMotionDilatedTexture, causticMotionDilationScratchTexture, causticHistoryTexture, causticTemporalTexture, projectedShadowMapTexture, projectedShadowMapHistoryTexture);
			projectedShadowMapTexture = null;
			projectedShadowMapHistoryTexture = null;
			previousProjectedShadowDirection = Vector2.zero;
			ReleasePhaseDiffuseLightTextures();
		}
		
		void ReleasePhaseDiffuseLightTextures()
		{
			ComputeHelper.Release(softLightTexture0, softLightTexture1, radianceCascadeSdfSeedA, radianceCascadeSdfSeedB, radianceCascadeSdfPayloadA, radianceCascadeSdfPayloadB, radianceCascadeSdfNormalA, radianceCascadeSdfNormalB);
			softLightTexture0 = null;
			softLightTexture1 = null;
			radianceCascadeSdfSeedA = null;
			radianceCascadeSdfSeedB = null;
			radianceCascadeSdfPayloadA = null;
			radianceCascadeSdfPayloadB = null;
			radianceCascadeSdfNormalA = null;
			radianceCascadeSdfNormalB = null;
		}

	}
}
