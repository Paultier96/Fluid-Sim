using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidCausticsTrace
	{
		readonly ParticleFluidLighting2D owner;
		const int LightSlotCount = 3;
		const int MaterialSlotCount = 3;
		readonly CausticsLightParams[] lightParams = new CausticsLightParams[LightSlotCount];
		readonly Vector4[] materialParams = new Vector4[MaterialSlotCount * 2];

		[StructLayout(LayoutKind.Sequential)]
		readonly struct CausticsLightParams
		{
			public readonly Vector4 directionAndEnabled;
			public readonly Vector4 multiplierAndType;
			public readonly Vector4 point;
			public readonly Vector4 optics;
			public readonly Vector4 ray;
			public readonly Vector4 colour;

			public CausticsLightParams(Vector3 direction, bool enabled, Vector3 multiplier, int type, Vector4 point, Vector4 optics, Vector4 ray, Color colour)
			{
				directionAndEnabled = new Vector4(direction.x, direction.y, direction.z, enabled ? 1f : 0f);
				multiplierAndType = new Vector4(multiplier.x, multiplier.y, multiplier.z, type);
				this.point = point;
				this.optics = optics;
				this.ray = ray;
				this.colour = colour;
			}
		}

		readonly struct CausticsComputePassState
		{
			public readonly ComputeShader compute;
			public readonly int clearKernel;
			public readonly int traceKernel;
			public readonly int resolveKernel;
			public readonly int width;
			public readonly int height;
			public readonly int totalRayBudget;
			public readonly int raysPerPixel;

			public CausticsComputePassState(ComputeShader compute, int clearKernel, int traceKernel, int resolveKernel, int width, int height, int totalRayBudget, int raysPerPixel)
			{
				this.compute = compute;
				this.clearKernel = clearKernel;
				this.traceKernel = traceKernel;
				this.resolveKernel = resolveKernel;
				this.width = width;
				this.height = height;
				this.totalRayBudget = totalRayBudget;
				this.raysPerPixel = raysPerPixel;
			}
		}

		public ParticleFluidCausticsTrace(ParticleFluidLighting2D owner)
		{
			this.owner = owner;
		}

		public void RecordComputeClear(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.clearKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.clearKernel, "CausticMotionResult", causticMotionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.clearKernel, state.width, state.height);
		}

		public void RecordComputeTrace(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle combinedHandle, TextureHandle velocityPhase0Handle, TextureHandle velocityPhase1Handle, TextureHandle gradientHandle, TextureHandle gradient2Handle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "CombinedTex", combinedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex0", velocityPhase0Handle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "VelocityTex1", velocityPhase1Handle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap", gradientHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.traceKernel, "ColourMap2", gradient2Handle);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.traceKernel);
			DispatchTrace(targetCommandBuffer, state.compute, state.traceKernel, state.totalRayBudget, state.raysPerPixel);
		}

		public void RecordComputeResolve(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex, TextureHandle causticResolvedHandle, TextureHandle causticMotionHandle)
		{
			CausticsComputePassState state = ApplyComputeCommonParams(context, targetCommandBuffer, combinedAccumulationTexture, frameIndex);
			BindComputeBuffers(targetCommandBuffer, state.compute, state.resolveKernel);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticResult", causticResolvedHandle);
			targetCommandBuffer.SetComputeTextureParam(state.compute, state.resolveKernel, "CausticMotionResult", causticMotionHandle);
			DispatchCompute(targetCommandBuffer, state.compute, state.resolveKernel, state.width, state.height);
		}

		CausticsComputePassState ApplyComputeCommonParams(ParticleFluidLighting2D.FrameContext context, IComputeCommandBuffer targetCommandBuffer, RenderTexture combinedAccumulationTexture, int frameIndex)
		{
			ParticleDisplay2D display = context.display;
			Vector2 currentWorldCenter = context.renderLayout.Caustic.WorldCenter;
			Vector2 currentWorldSize = context.renderLayout.Caustic.WorldSize;
			float analyticBoundaryExpansion = context.analyticBoundaryExpansion;
			ParticleDisplay2D.MetaballSettings surface = display.metaballs;
			ComputeShader compute = owner.computeShader;
			bool renderCausticMotion = owner.lightingMode == ParticleFluidLighting2D.LightingMode.FullCaustics && (owner.temporalMotionSource == ParticleFluidLighting2D.TemporalMotionSource.CausticMotion || owner.debugMode == ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
			int clearKernel = compute.FindKernel("Clear");
			int traceKernel = compute.FindKernel("Trace");
			int resolveKernel = compute.FindKernel("Resolve");

			int width = owner.causticResolvedTexture.width;
			int height = owner.causticResolvedTexture.height;
			ParticleFluidLight2D[] lights = owner.lightSlots;
			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = owner.PhaseMaterials;
			Vector3[] lightDirections = new Vector3[lights.Length];
			bool[] lightEnabled = new bool[lights.Length];
			float[] lightAngularRadiusDegrees = new float[lights.Length];
			float[] lightRayStartOffsets = new float[lights.Length];
			int[] lightRangeRayCounts = new int[lights.Length];
			float[] lightPointAngleStarts = new float[lights.Length];
			float[] lightPointAngleRanges = new float[lights.Length];
			float[] lightWeights = new float[lights.Length];
			for (int i = 0; i < lights.Length; i++)
			{
				ParticleFluidLight2D light = lights[i];
				ParticleFluidDirectionalLight2D directionalLight = light as ParticleFluidDirectionalLight2D;
				lightDirections[i] = directionalLight != null ? directionalLight.Direction : Vector3.down;
				lightEnabled[i] = light != null && light.SupportsCausticRaymarch();
				lightAngularRadiusDegrees[i] = GetDirectionalAngularRadiusDegrees(light);
				GetCausticRayRange(context, width, height, lightDirections[i], lightAngularRadiusDegrees[i], out lightRayStartOffsets[i], out lightRangeRayCounts[i]);
				if (light is ParticleFluidPointLight2D pointLight)
				{
					pointLight.GetCausticRaySpan(context.display, owner.pointLightBoundaryAngles, out lightPointAngleStarts[i], out lightPointAngleRanges[i]);
					lightRangeRayCounts[i] = pointLight.GetCausticPointRayCount(currentWorldSize, width, height);
					lightRayStartOffsets[i] = 0f;
				}

				lightWeights[i] = lightEnabled[i] && light != null ? light.GetCausticSampleWeight() : 0f;
			}
			int raysPerPixel = Mathf.Max(1, owner.raysPerPixel);
			int maxRayCount = Mathf.Max(1, ParticleFluidLighting2D.MaxCausticTraceThreads / raysPerPixel);
			int enabledRangeRayCount = Mathf.Max(
				lightWeights[0] > 0f ? lightRangeRayCounts[0] : 0,
				lightWeights[1] > 0f ? lightRangeRayCounts[1] : 0,
				lightWeights[2] > 0f ? lightRangeRayCounts[2] : 0
			);
			int totalRayBudget = enabledRangeRayCount > 0 ? Mathf.Max(1, Mathf.Min(enabledRangeRayCount, maxRayCount)) : 1;
			GetLightRayShares(lightWeights[0], lightWeights[1], lightWeights[2], out float primaryShare, out float secondaryShare, out float tertiaryShare);
			float[] lightShares = { primaryShare, secondaryShare, tertiaryShare };
			int[] lightSubRaysPerPixel = new int[lights.Length];
			lightSubRaysPerPixel[1] = lightShares[1] > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * lightShares[1]) : 0;
			lightSubRaysPerPixel[1] = Mathf.Clamp(lightSubRaysPerPixel[1], 0, raysPerPixel);
			lightSubRaysPerPixel[2] = lightShares[2] > 0f && raysPerPixel > 1 ? Mathf.RoundToInt(raysPerPixel * lightShares[2]) : 0;
			lightSubRaysPerPixel[2] = Mathf.Clamp(lightSubRaysPerPixel[2], 0, raysPerPixel - lightSubRaysPerPixel[1]);
			if (owner.AnyEnabledCausticPointLight())
			{
				lightSubRaysPerPixel[1] = 0;
				lightSubRaysPerPixel[2] = 0;
			}
			lightSubRaysPerPixel[0] = lightShares[0] > 0f ? Mathf.Max(0, raysPerPixel - lightSubRaysPerPixel[1] - lightSubRaysPerPixel[2]) : 0;
			bool splitBySubRay = lightSubRaysPerPixel[1] > 0 || lightSubRaysPerPixel[2] > 0;
			int[] lightRayBudgets = new int[lights.Length];
			lightRayBudgets[1] = lightShares[1] > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * lightShares[1]) : 0;
			lightRayBudgets[1] = Mathf.Clamp(lightRayBudgets[1], 0, Mathf.Min(totalRayBudget, lightRangeRayCounts[1]));
			lightRayBudgets[2] = lightShares[2] > 0f && !splitBySubRay ? Mathf.RoundToInt(totalRayBudget * lightShares[2]) : 0;
			lightRayBudgets[2] = Mathf.Clamp(lightRayBudgets[2], 0, Mathf.Min(totalRayBudget - lightRayBudgets[1], lightRangeRayCounts[2]));
			lightRayBudgets[0] = lightShares[0] > 0f ? splitBySubRay ? totalRayBudget : Mathf.Max(0, totalRayBudget - lightRayBudgets[1] - lightRayBudgets[2]) : 0;
			lightRayBudgets[0] = Mathf.Clamp(lightRayBudgets[0], 0, lightRangeRayCounts[0]);
			float[] lightRaySpacings = new float[lights.Length];
			for (int i = 0; i < lights.Length; i++)
			{
				int spacingRayCount = i == 0 || !splitBySubRay ? lightRayBudgets[i] : totalRayBudget;
				lightRaySpacings[i] = lightRangeRayCounts[i] > 1 && spacingRayCount > 1 ? (lightRangeRayCounts[i] - 1f) / (spacingRayCount - 1f) : 1f;
			}

			targetCommandBuffer.SetComputeIntParam(compute, "combinedWidth", combinedAccumulationTexture.width);
			targetCommandBuffer.SetComputeIntParam(compute, "combinedHeight", combinedAccumulationTexture.height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticWidth", width);
			targetCommandBuffer.SetComputeIntParam(compute, "causticHeight", height);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayCount", totalRayBudget);
			for (int i = 0; i < lights.Length; i++)
			{
				ParticleFluidLight2D light = lights[i];
				ParticleFluidPointLight2D pointLight = light as ParticleFluidPointLight2D;
				lightParams[i] = new CausticsLightParams(
					lightDirections[i],
					i == 0 || lightSubRaysPerPixel[i] > 0 || lightRayBudgets[i] > 0,
					lightWeights[i] > 0f && light != null ? light.GetCausticMultiplier() : Vector4.zero,
					pointLight != null ? 1 : 0,
					pointLight != null ? pointLight.GetPointLightVector() : Vector4.zero,
					new Vector4(
						pointLight != null ? pointLight.falloff : 0f,
						lightAngularRadiusDegrees[i] * Mathf.Deg2Rad,
						lightPointAngleStarts[i],
						lightPointAngleRanges[i]
					),
					new Vector4(
						lightRayBudgets[i],
						lightSubRaysPerPixel[i],
						lightRayStartOffsets[i],
						lightRaySpacings[i]
					),
					new Color(light != null ? light.temperatureKelvin : 6500f, light != null ? GetSaturationDispersionScale(light.color) : 1f, 0f, 0f)
				);
			}
			owner.causticLightParamsBuffer.SetData(lightParams);
			for (int i = 0; i < MaterialSlotCount; i++)
			{
				ParticleFluidLighting2D.PhaseMaterialSettings material = materials[i];
				int offset = i * 2;
				materialParams[offset] = new Vector4(material.indexOfRefraction, material.reflectance, material.metallic, material.absorption);
				Color diffuseTint = material.diffuseLightTint;
				materialParams[offset + 1] = new Vector4(diffuseTint.r, diffuseTint.g, diffuseTint.b, material.absorptionDiffuseTintBlend);
			}
			owner.causticMaterialParamsBuffer.SetData(materialParams);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaySteps", owner.extraRayTravelSteps);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRayStride", owner.rayStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsRaysPerPixel", raysPerPixel);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsColourSampleStride", owner.colourSampleStride);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsMotionEnabled", renderCausticMotion ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "densityThreshold", surface.densityThreshold);
			targetCommandBuffer.SetComputeFloatParam(compute, "phase0RenderBias", surface.phase0RenderBias);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsStochasticReflection", owner.stochasticReflection ? 1 : 0);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionStrength", owner.dispersionStrength);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDispersionRotation", owner.dispersionRotation);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoBrightnessInfluence", owner.absorptionAlbedoBrightnessInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsAbsorptionAlbedoSaturationInfluence", owner.absorptionAlbedoSaturationInfluence);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsRayBrightness", owner.rayBrightness);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsTemporalJitterPixels", owner.temporalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsSurfaceNormalJitterPixels", owner.surfaceNormalJitterPixels);
			targetCommandBuffer.SetComputeFloatParam(compute, "causticsDeltaTime", display.sim.CurrentSimulationDeltaTime);
			targetCommandBuffer.SetComputeIntParam(compute, "causticsFrameIndex", frameIndex);
			targetCommandBuffer.SetComputeIntParam(compute, "useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeFloatParam(compute, "obstacleY", display.sim.obstacleY);
			targetCommandBuffer.SetComputeFloatParam(compute, "analyticBoundaryExpansion", analyticBoundaryExpansion);
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldCenter", new Vector4(currentWorldCenter.x, currentWorldCenter.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsWorldSize", new Vector4(currentWorldSize.x, currentWorldSize.y, 0f, 0f));
			targetCommandBuffer.SetComputeVectorParam(compute, "causticsSourceUvRect", new Vector4(0f, 0f, 1f, 1f));

			return new CausticsComputePassState(compute, clearKernel, traceKernel, resolveKernel, width, height, totalRayBudget, raysPerPixel);
		}

		void BindComputeBuffers(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel)
		{
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticAccum", owner.causticAccumulationBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticMotionAccum", owner.causticMotionAccumulationBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsLights", owner.causticLightParamsBuffer);
			targetCommandBuffer.SetComputeBufferParam(compute, kernel, "CausticsMaterials", owner.causticMaterialParamsBuffer);
		}

		static float GetDirectionalAngularRadiusDegrees(ParticleFluidLight2D light)
		{
			return light is ParticleFluidDirectionalLight2D directionalLight ? directionalLight.angularRadiusDegrees : 0f;
		}

		static float GetSaturationDispersionScale(Color color)
		{
			float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
			float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
			float saturation = maxChannel > 0.000001f ? (maxChannel - minChannel) / maxChannel : 0f;
			return 1f - Mathf.InverseLerp(0.6f, 0.8f, saturation);
		}

		static void GetLightRayShares(float primaryWeight, float secondaryWeight, float tertiaryWeight, out float primaryShare, out float secondaryShare, out float tertiaryShare)
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

		static void GetCausticRayRange(ParticleFluidLighting2D.FrameContext context, int width, int height, Vector3 lightDirection, float angularRadiusDegrees, out float startOffset, out int rayCount)
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

		static void IncludeCausticLaunchPoint(Vector2 world, Vector2 worldCenter, Vector2 worldSize, int width, int height, Vector2 tangent, ref float minOffset, ref float maxOffset)
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

		static void DispatchCompute(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int width, int height)
		{
			targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
		}

		void DispatchTrace(IComputeCommandBuffer targetCommandBuffer, ComputeShader compute, int kernel, int rayCount, int raysPerPixel)
		{
			int totalWidth = Mathf.Min(Mathf.Max(rayCount, 0) * Mathf.Max(raysPerPixel, 1), ParticleFluidLighting2D.MaxCausticTraceThreads);
			if (totalWidth > 0)
			{
				targetCommandBuffer.DispatchCompute(compute, kernel, Mathf.CeilToInt(totalWidth / (float)ParticleFluidLighting2D.CausticTraceThreadGroupSize), 1, 1);
			}
		}
	}
}
