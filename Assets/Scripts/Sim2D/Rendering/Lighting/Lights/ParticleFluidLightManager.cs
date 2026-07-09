using System;
using System.Runtime.InteropServices;
using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	internal sealed class ParticleFluidLightManager : MonoBehaviour
	{
		const int LightSlotCount = 3;
		const int PointLightBoundaryEllipseSamples = 128;
		const int PointLightBoundaryCutSamples = 31;
		readonly ParticleFluidLight2D[] lightSlots = new ParticleFluidLight2D[LightSlotCount];
		readonly CausticsLightGpuData[] causticsLightGpuData = new CausticsLightGpuData[LightSlotCount];

		[StructLayout(LayoutKind.Sequential)]
		readonly struct CausticsLightGpuData
		{
			public readonly Vector4 launchDirectionAndEnabled;
			public readonly Vector4 multiplierAndLaunchMode;
			public readonly Vector4 launchAnchor;
			public readonly Vector4 launchSpan;
			public readonly Vector4 rayAllocation;
			public readonly Vector4 lightExtra;

			public CausticsLightGpuData(Vector3 launchDirection, bool enabled, Vector3 multiplier, int launchMode, Vector4 launchAnchor, Vector4 launchSpan, Vector4 rayAllocation, Color lightExtra)
			{
				launchDirectionAndEnabled = new Vector4(launchDirection.x, launchDirection.y, launchDirection.z, enabled ? 1f : 0f);
				multiplierAndLaunchMode = new Vector4(multiplier.x, multiplier.y, multiplier.z, launchMode);
				this.launchAnchor = launchAnchor;
				this.launchSpan = launchSpan;
				this.rayAllocation = rayAllocation;
				this.lightExtra = lightExtra;
			}
		}

		internal readonly float[] pointLightBoundaryAngles = new float[PointLightBoundaryEllipseSamples + PointLightBoundaryCutSamples + 2];
		internal ParticleFluidLight2D[] LightSlots => lightSlots;

		void Awake()
		{
			RefreshLightSlots();
		}

		void OnValidate()
		{
			RefreshLightSlots();
		}

		void OnTransformChildrenChanged()
		{
			RefreshLightSlots();
		}

		internal void RefreshLightSlots()
		{
			Array.Clear(lightSlots, 0, lightSlots.Length);
			ParticleFluidLight2D[] discoveredLights = GetComponentsInChildren<ParticleFluidLight2D>(true);
			Array.Copy(discoveredLights, lightSlots, Math.Min(discoveredLights.Length, lightSlots.Length));
		}

		internal bool AnyEnabledCausticPointLight()
		{
			foreach (ParticleFluidLight2D light in lightSlots)
			{
				if (light is ParticleFluidPointLight2D pointLight && pointLight.GetCausticSampleWeight() > 0f)
				{
					return true;
				}
			}
			return false;
		}

		internal ParticleFluidDirectionalLight2D GetMainDirectionalLight()
		{
			ParticleFluidDirectionalLight2D directionalLight = null;
			float brightestExposure = 0f;

			foreach (ParticleFluidLight2D light in lightSlots)
			{
				if (light is not ParticleFluidDirectionalLight2D candidate
				    || !candidate.isActiveAndEnabled
				    || candidate.intensity <= 0f)
				{
					continue;
				}

				Color effectiveColor = candidate.EffectiveColor;
				float exposure = (effectiveColor.r * 0.2126f + effectiveColor.g * 0.7152f + effectiveColor.b * 0.0722f) * candidate.intensity;
				if (directionalLight == null || exposure > brightestExposure)
				{
					directionalLight = candidate;
					brightestExposure = exposure;
				}
			}

			return directionalLight;
		}

		internal void ApplyLightingMaterialParams(Material material, ParticleFluidAnalyticBoundary2D analyticBoundary, float ior)
		{
			Vector4[] lightBaseDirections = new Vector4[LightSlotCount];
			Vector4[] lightDirections = new Vector4[LightSlotCount];
			Vector4[] lightPoints = new Vector4[LightSlotCount];
			Vector4[] lightColors = new Vector4[LightSlotCount];
			Vector4[] lightData = new Vector4[LightSlotCount];

			for (int i = 0; i < LightSlotCount; i++)
			{
				ParticleFluidLight2D light = lightSlots[i];
				ParticleFluidDirectionalLight2D directionalLight = light as ParticleFluidDirectionalLight2D;
				bool lightEnabled = light != null && light.isActiveAndEnabled && light.intensity > 0f;
				lightBaseDirections[i] = directionalLight != null ? directionalLight.Direction : Vector3.down;
				lightDirections[i] = directionalLight != null ? directionalLight.GetBoundaryRefractedDirection(ior, analyticBoundary) : Vector3.down;
				ParticleFluidPointLight2D pointLight = light as ParticleFluidPointLight2D;
				lightPoints[i] = pointLight != null ? pointLight.GetPointLightVector() : Vector4.zero;
				lightColors[i] = light != null ? light.EffectiveColor : Vector4.zero;
				lightData[i] = new Vector4(
					lightEnabled ? 1f : 0f,
					pointLight != null ? 1f : 0f,
					pointLight != null ? pointLight.falloff : 0f,
					i == 0 ? (lightEnabled && light != null ? light.intensity : 0f) : (light != null ? light.intensity : 0f)
				);
			}

			material.SetVectorArray("particleLightBaseDirections", lightBaseDirections);
			material.SetVectorArray("particleLightDirections", lightDirections);
			material.SetVectorArray("particleLightPoints", lightPoints);
			material.SetVectorArray("particleLightColors", lightColors);
			material.SetVectorArray("particleLightData", lightData);
		}

		internal int UploadCausticsLightGpuData(ComputeBuffer buffer, ParticleFluidLighting2D.FrameContext context, Vector2Int resolution, Vector2 currentWorldSize, int raysPerPixel)
		{
			Vector3[] launchDirections = new Vector3[lightSlots.Length];
			bool[] lightEnabled = new bool[lightSlots.Length];
			float[] launchAngularRadiusDegrees = new float[lightSlots.Length];
			float[] launchSpanOffsets = new float[lightSlots.Length];
			int[] launchSpanRayCounts = new int[lightSlots.Length];
			float[] launchSpanStarts = new float[lightSlots.Length];
			float[] launchSpanLengths = new float[lightSlots.Length];
			float[] lightWeights = new float[lightSlots.Length];
			for (int i = 0; i < lightSlots.Length; i++)
			{
				ParticleFluidLight2D light = lightSlots[i];
				lightEnabled[i] = light != null && light.SupportsCausticRaymarch();
				if (light is ParticleFluidDirectionalLight2D directionalLight)
				{
					launchDirections[i] = directionalLight.Direction;
					launchAngularRadiusDegrees[i] = directionalLight.angularRadiusDegrees;
					directionalLight.GetCausticRayRange(context, resolution, out launchSpanOffsets[i], out launchSpanRayCounts[i]);
				}
				else if (light is ParticleFluidPointLight2D pointLight)
				{
					launchDirections[i] = Vector3.zero;
					launchAngularRadiusDegrees[i] = 0f;
					pointLight.GetCausticRaySpan(context.display.sim.analyticBoundary, pointLightBoundaryAngles, out launchSpanStarts[i], out launchSpanLengths[i]);
					launchSpanRayCounts[i] = pointLight.GetCausticPointRayCount(currentWorldSize, resolution);
					launchSpanOffsets[i] = 0f;
				}
				else
				{
					launchDirections[i] = Vector3.zero;
					launchAngularRadiusDegrees[i] = 0f;
					launchSpanOffsets[i] = 0f;
					launchSpanRayCounts[i] = 1;
				}
				lightWeights[i] = lightEnabled[i] && light != null ? light.GetCausticSampleWeight() : 0f;
			}

			int maxRayCount = Mathf.Max(1, ParticleFluidLighting2D.MaxCausticTraceThreads / raysPerPixel);
			int enabledRangeRayCount = Mathf.Max(
				lightWeights[0] > 0f ? launchSpanRayCounts[0] : 0,
				lightWeights[1] > 0f ? launchSpanRayCounts[1] : 0,
				lightWeights[2] > 0f ? launchSpanRayCounts[2] : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0 ? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount)) : 1;
			float[] lightShares = GetLightRayShares(lightWeights);
			int[] lightSubRaysPerPixel = new int[lightSlots.Length];
			lightSubRaysPerPixel[1] = lightShares[1] > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * lightShares[1]) : 0;
			lightSubRaysPerPixel[1] = Mathf.Clamp(lightSubRaysPerPixel[1], 0, raysPerPixel);
			lightSubRaysPerPixel[2] = lightShares[2] > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * lightShares[2]) : 0;
			lightSubRaysPerPixel[2] = Mathf.Clamp(lightSubRaysPerPixel[2], 0, raysPerPixel - lightSubRaysPerPixel[1]);
			if (AnyEnabledCausticPointLight())
			{
				lightSubRaysPerPixel[1] = 0;
				lightSubRaysPerPixel[2] = 0;
			}

			lightSubRaysPerPixel[0] = lightShares[0] > 0f ? Mathf.Max(0, raysPerPixel - lightSubRaysPerPixel[1] - lightSubRaysPerPixel[2]) : 0;
			bool splitBySubRay = lightSubRaysPerPixel[1] > 0 || lightSubRaysPerPixel[2] > 0;
			int[] lightRayBudgets = new int[lightSlots.Length];
			lightRayBudgets[1] = lightShares[1] > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * lightShares[1]) : 0;
			lightRayBudgets[1] = Mathf.Clamp(lightRayBudgets[1], 0, Mathf.Min(totalRayBudget, launchSpanRayCounts[1]));
			lightRayBudgets[2] = lightShares[2] > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * lightShares[2]) : 0;
			lightRayBudgets[2] = Mathf.Clamp(lightRayBudgets[2], 0, Mathf.Min(totalRayBudget - lightRayBudgets[1], launchSpanRayCounts[2]));
			lightRayBudgets[0] = lightShares[0] > 0f ? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - lightRayBudgets[1] - lightRayBudgets[2]) : 0;
			lightRayBudgets[0] = Mathf.Clamp(lightRayBudgets[0], 0, launchSpanRayCounts[0]);
			float[] lightRaySpacings = new float[lightSlots.Length];
			for (int i = 0; i < lightSlots.Length; i++)
			{
				int spacingRayCount = i == 0 || !splitBySubRay ? lightRayBudgets[i] : totalRayBudget;
				lightRaySpacings[i] = launchSpanRayCounts[i] > 1 && spacingRayCount > 1 ? (launchSpanRayCounts[i] - 1f) / (spacingRayCount - 1f) : 1f;
			}

			for (int i = 0; i < LightSlotCount; i++)
			{
				ParticleFluidLight2D light = lightSlots[i];
				ParticleFluidPointLight2D pointLight = light as ParticleFluidPointLight2D;
				causticsLightGpuData[i] = new CausticsLightGpuData(
					launchDirections[i],
					i == 0 || lightSubRaysPerPixel[i] > 0 || lightRayBudgets[i] > 0,
					lightWeights[i] > 0f && light != null ? light.GetCausticMultiplier() : Vector4.zero,
					pointLight != null ? 1 : 0,
					pointLight != null ? pointLight.GetPointLightVector() : Vector4.zero,
					new Vector4(pointLight != null ? pointLight.falloff : 0f, launchAngularRadiusDegrees[i] * Mathf.Deg2Rad, launchSpanStarts[i], launchSpanLengths[i]),
					new Vector4(lightRayBudgets[i], lightSubRaysPerPixel[i], launchSpanOffsets[i], lightRaySpacings[i]),
					new Color(light != null ? light.temperatureKelvin : 6500f, light != null ? GetSaturationDispersionScale(light.color) : 1f, pointLight != null ? Mathf.Max(pointLight.sourceRadius, 0f) : 0f, 0f)
				);
			}

			buffer.SetData(causticsLightGpuData);
			return totalRayBudget;
		}

		internal float[] GetLightRayShares(float[] weights)
		{
			float[] lightShares = new float[weights.Length];
			float totalWeight = 0f;
			for (int i = 0; i < weights.Length; i++)
			{
				totalWeight += weights[i];
			}
			if (totalWeight > 0.0001f)
			{
				for (int i = 0; i < weights.Length; i++)
				{
					lightShares[i] = weights[i] / totalWeight;
				}
			}
			return lightShares;
		}

		static float GetSaturationDispersionScale(Color color)
		{
			float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
			float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}
		
		public float GetCausticDebugExposure()
		{
			float exposure = 0f;
			foreach (ParticleFluidLight2D light in LightSlots)
			{
				if (light != null && light.SupportsCausticRaymarch())
				{
					exposure += light.GetCausticExposure();
				}
			}
			return Mathf.Max(exposure, 0.0001f);
		}
	}
}

