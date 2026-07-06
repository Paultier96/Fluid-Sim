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
			public readonly Vector4 directionAndEnabled;
			public readonly Vector4 multiplierAndType;
			public readonly Vector4 point;
			public readonly Vector4 optics;
			public readonly Vector4 ray;
			public readonly Vector4 colour;

			public CausticsLightGpuData(Vector3 direction, bool enabled, Vector3 multiplier, int type, Vector4 point, Vector4 optics, Vector4 ray, Color colour)
			{
				directionAndEnabled = new Vector4(direction.x, direction.y, direction.z, enabled ? 1f : 0f);
				multiplierAndType = new Vector4(multiplier.x, multiplier.y, multiplier.z, type);
				this.point = point;
				this.optics = optics;
				this.ray = ray;
				this.colour = colour;
			}
		}

		internal readonly float[] pointLightBoundaryAngles = new float[PointLightBoundaryEllipseSamples + PointLightBoundaryCutSamples + 2];
		internal ParticleFluidLight2D[] LightSlots => lightSlots;
		[SerializeField] ParticleFluidLighting2D owner;

		void Awake()
		{
			ResolveOwner();
			RefreshLightSlots();
		}

		void OnValidate()
		{
			ResolveOwner();
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

		internal void ApplyLightingMaterialParams(Material material, ParticleFluidAnalyticBoundary2D analyticBoundary)
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
				lightDirections[i] = directionalLight != null ? directionalLight.GetDirectLightingDirection(owner, analyticBoundary) : Vector3.down;
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
			Vector3[] lightDirections = new Vector3[lightSlots.Length];
			bool[] lightEnabled = new bool[lightSlots.Length];
			float[] lightAngularRadiusDegrees = new float[lightSlots.Length];
			float[] lightRayStartOffsets = new float[lightSlots.Length];
			int[] lightRangeRayCounts = new int[lightSlots.Length];
			float[] lightPointAngleStarts = new float[lightSlots.Length];
			float[] lightPointAngleRanges = new float[lightSlots.Length];
			float[] lightWeights = new float[lightSlots.Length];
			for (int i = 0; i < lightSlots.Length; i++)
			{
				ParticleFluidLight2D light = lightSlots[i];
				lightEnabled[i] = light != null && light.SupportsCausticRaymarch();
				if (light is ParticleFluidDirectionalLight2D directionalLight)
				{
					lightDirections[i] = directionalLight.Direction;
					lightAngularRadiusDegrees[i] = directionalLight.angularRadiusDegrees;
					directionalLight.GetCausticRayRange(context, resolution, out lightRayStartOffsets[i], out lightRangeRayCounts[i]);
				}
				else if (light is ParticleFluidPointLight2D pointLight)
				{
					lightDirections[i] = Vector3.zero;
					lightAngularRadiusDegrees[i] = 0f;
					pointLight.GetCausticRaySpan(context.display.sim.analyticBoundary, pointLightBoundaryAngles, out lightPointAngleStarts[i], out lightPointAngleRanges[i]);
					lightRangeRayCounts[i] = pointLight.GetCausticPointRayCount(currentWorldSize, resolution);
					lightRayStartOffsets[i] = 0f;
				}
				else
				{
					lightDirections[i] = Vector3.zero;
					lightAngularRadiusDegrees[i] = 0f;
					lightRayStartOffsets[i] = 0f;
					lightRangeRayCounts[i] = 1;
				}
				lightWeights[i] = lightEnabled[i] && light != null ? light.GetCausticSampleWeight() : 0f;
			}

			int maxRayCount = Mathf.Max(1, ParticleFluidLighting2D.MaxCausticTraceThreads / raysPerPixel);
			int enabledRangeRayCount = Mathf.Max(
				lightWeights[0] > 0f ? lightRangeRayCounts[0] : 0,
				lightWeights[1] > 0f ? lightRangeRayCounts[1] : 0,
				lightWeights[2] > 0f ? lightRangeRayCounts[2] : 0
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
			lightRayBudgets[1] = Mathf.Clamp(lightRayBudgets[1], 0, Mathf.Min(totalRayBudget, lightRangeRayCounts[1]));
			lightRayBudgets[2] = lightShares[2] > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * lightShares[2]) : 0;
			lightRayBudgets[2] = Mathf.Clamp(lightRayBudgets[2], 0, Mathf.Min(totalRayBudget - lightRayBudgets[1], lightRangeRayCounts[2]));
			lightRayBudgets[0] = lightShares[0] > 0f ? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - lightRayBudgets[1] - lightRayBudgets[2]) : 0;
			lightRayBudgets[0] = Mathf.Clamp(lightRayBudgets[0], 0, lightRangeRayCounts[0]);
			float[] lightRaySpacings = new float[lightSlots.Length];
			for (int i = 0; i < lightSlots.Length; i++)
			{
				int spacingRayCount = i == 0 || !splitBySubRay ? lightRayBudgets[i] : totalRayBudget;
				lightRaySpacings[i] = lightRangeRayCounts[i] > 1 && spacingRayCount > 1 ? (lightRangeRayCounts[i] - 1f) / (spacingRayCount - 1f) : 1f;
			}

			for (int i = 0; i < LightSlotCount; i++)
			{
				ParticleFluidLight2D light = lightSlots[i];
				ParticleFluidPointLight2D pointLight = light as ParticleFluidPointLight2D;
				causticsLightGpuData[i] = new CausticsLightGpuData(
					lightDirections[i],
					i == 0 || lightSubRaysPerPixel[i] > 0 || lightRayBudgets[i] > 0,
					lightWeights[i] > 0f && light != null ? light.GetCausticMultiplier() : Vector4.zero,
					pointLight != null ? 1 : 0,
					pointLight != null ? pointLight.GetPointLightVector() : Vector4.zero,
					new Vector4(pointLight != null ? pointLight.falloff : 0f, lightAngularRadiusDegrees[i] * Mathf.Deg2Rad, lightPointAngleStarts[i], lightPointAngleRanges[i]),
					new Vector4(lightRayBudgets[i], lightSubRaysPerPixel[i], lightRayStartOffsets[i], lightRaySpacings[i]),
					new Color(light != null ? light.temperatureKelvin : 6500f, light != null ? GetSaturationDispersionScale(light.color) : 1f, 0f, 0f)
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

		void ResolveOwner()
		{
			if (owner != null)
			{
				return;
			}

			owner = GetComponent<ParticleFluidLighting2D>();
			if (owner != null)
			{
				return;
			}

			if (transform.parent != null)
			{
				owner = transform.parent.GetComponentInChildren<ParticleFluidLighting2D>(true);
			}

			owner ??= FindAnyObjectByType<ParticleFluidLighting2D>();
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
