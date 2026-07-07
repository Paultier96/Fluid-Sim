using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class JumpFloodRenderer2D
	{
		const int AlbedoPass = 0;
		const int NormalPass = 1;
		const int TransportPass = 2;
		const int UnlitPass = 3;

		Material displayMaterial;
		Material materialMapMaterial;
		RenderTexture seedA;
		RenderTexture seedB;
		RenderTexture payloadA;
		RenderTexture payloadB;
		RenderTexture normalPayloadA;
		RenderTexture normalPayloadB;
		RenderTexture result;
		RenderTexture payloadResult;
		RenderTexture normalPayloadResult;
		readonly ParticleFluidMaterialMapSet materialMaps = new();
		private Bounds _currentRenderRegion;
		private Vector2Int _currentSourceSize;
		private Vector2Int _currentMaterialSize;

		public bool PrepareForRender(ParticleDisplay2D display, Camera cam)
		{
			if (display == null || cam == null || display.jumpFlood.computeShader == null || display.jumpFlood.displayShader == null)
			{
				return false;
			}

			ParticleFluidRenderUtils.EnsureMaterial(ref displayMaterial, display.jumpFlood.displayShader);
			if (displayMaterial == null)
			{
				return false;
			}

			RunJumpFlood(display, cam);
			return true;
		}

		public void RecordComposite(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			RecordDisplay(display, cam, targetCommandBuffer, finalTarget);
		}

		public void Release()
		{
			ComputeHelper.Release(seedA, seedB, payloadA, payloadB);
			ComputeHelper.Release(normalPayloadA, normalPayloadB);
			materialMaps.Release();
			seedA = null;
			seedB = null;
			payloadA = null;
			payloadB = null;
			normalPayloadA = null;
			normalPayloadB = null;
			result = null;
			payloadResult = null;
			normalPayloadResult = null;

			ParticleFluidRenderUtils.DestroyMaterial(ref displayMaterial);
			ParticleFluidRenderUtils.DestroyMaterial(ref materialMapMaterial);
		}

		void RunJumpFlood(ParticleDisplay2D display, Camera cam)
		{
			Bounds cropRegion = GetRenderRegion(display, cam, out Vector2Int cropResolution);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			_currentRenderRegion = cropRegion;
			_currentSourceSize = ScaleSize(cropResolution, sourceScale);
			_currentMaterialSize = ScaleSize(cropResolution, materialScale);
			EnsureRenderTextures();

			int width = seedA.width;
			int height = seedA.height;
			ComputeShader compute = display.jumpFlood.computeShader;

			int clearKernel = compute.FindKernel("Clear");
			int seedKernel = compute.FindKernel("Seed");
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			Matrix4x4 viewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;

			compute.SetInt("_Width", width);
			compute.SetInt("_Height", height);
			compute.SetMatrix("_VP", viewProjection);
			compute.SetVector("_RenderWorldCenter", _currentRenderRegion.center);
			compute.SetVector("_RenderWorldSize", _currentRenderRegion.size);
			compute.SetFloat("_TempMin", display.sim.ambientTemperature);
			compute.SetFloat("_TempMax", display.sim.HeatSourceTemperature);
			compute.SetInt("_ParticleCount", display.sim.positionBuffer.count);
			compute.SetInt("debugMode", (int)display.debugMode);
			compute.SetInt("debugShowClipping", display.debugShowClipping ? 1 : 0);
			compute.SetInt("useLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
			compute.SetFloat("debugGradientMax", display.debugGradientMax);
			compute.SetFloat("debugCurvatureMax", display.sim.MaxDebugCurvature);
			compute.SetFloat("debugViscosityMax", display.sim.MaxDebugViscosity);
			compute.SetFloat("debugDensityMin", display.DebugDensityMin);
			compute.SetFloat("debugDensityMax", display.DebugDensityMax);

			compute.SetTexture(clearKernel, "Result", seedA);
			compute.SetTexture(clearKernel, "ResultPayload", payloadA);
			compute.SetTexture(clearKernel, "ResultNormalPayload", normalPayloadA);
			int gx = (width + 15) / 16;
			int gy = (height + 15) / 16;
			compute.Dispatch(clearKernel, gx, gy, 1);

			compute.SetBuffer(seedKernel, "Positions2D", display.sim.positionBuffer);
			compute.SetBuffer(seedKernel, "DensityData", display.sim.densityBuffer);
			compute.SetBuffer(seedKernel, "Temperatures", display.sim.temperatureBuffer);
			compute.SetBuffer(seedKernel, "DebugData", display.sim.debugDataBuffer);
			compute.SetBuffer(seedKernel, "Phases", display.sim.phaseBuffer);
			compute.SetBuffer(seedKernel, "BlobIDs", display.sim.blobIdBuffer);
			compute.SetTexture(seedKernel, "Result", seedA);
			compute.SetTexture(seedKernel, "ResultPayload", payloadA);
			compute.SetTexture(seedKernel, "ResultNormalPayload", normalPayloadA);
			compute.SetTexture(seedKernel, "ColourMap", display.gradientTexture);
			compute.SetTexture(seedKernel, "ColourMap2", display.gradientTexture2);
			compute.SetTexture(seedKernel, "DebugHeatMap", display.debugHeatMapTexture);
			compute.SetTexture(seedKernel, "DebugSignedHeatMap", display.debugSignedHeatMapTexture);
			int seedGroups = Mathf.CeilToInt(display.sim.positionBuffer.count / 64.0f);
			compute.Dispatch(seedKernel, Mathf.Max(1, seedGroups), 1, 1);

			RenderTexture src = seedA;
			RenderTexture dst = seedB;
			RenderTexture payloadSrc = payloadA;
			RenderTexture payloadDst = payloadB;
			RenderTexture normalPayloadSrc = normalPayloadA;
			RenderTexture normalPayloadDst = normalPayloadB;
			int maxDim = Mathf.Max(width, height);
			int step = 1;
			while ((step << 1) < maxDim)
			{
				step <<= 1;
			}

			for (int s = step; s >= 1; s >>= 1)
			{
				if (src == dst)
				{
					Debug.LogError("READ/WRITE SAME TEXTURE!");
				}

				compute.SetInt("_Step", s);
				compute.SetTexture(jumpFloodKernel, "_SrcTex", src);
				compute.SetTexture(jumpFloodKernel, "_SrcPayloadTex", payloadSrc);
				compute.SetTexture(jumpFloodKernel, "_SrcNormalPayloadTex", normalPayloadSrc);
				compute.SetTexture(jumpFloodKernel, "_DstTex", dst);
				compute.SetTexture(jumpFloodKernel, "_DstPayloadTex", payloadDst);
				compute.SetTexture(jumpFloodKernel, "_DstNormalPayloadTex", normalPayloadDst);
				compute.Dispatch(jumpFloodKernel, gx, gy, 1);

				(src, dst) = (dst, src);
				(payloadSrc, payloadDst) = (payloadDst, payloadSrc);
				(normalPayloadSrc, normalPayloadDst) = (normalPayloadDst, normalPayloadSrc);
			}

			result = src;
			payloadResult = payloadSrc;
			normalPayloadResult = normalPayloadSrc;
		}

		void EnsureRenderTextures()
		{
			int width = _currentSourceSize.x;
			int height = _currentSourceSize.y;

			ComputeHelper.CreateRenderTexture(ref seedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref seedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref payloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload A");
			ComputeHelper.CreateRenderTexture(ref payloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload B");
			ComputeHelper.CreateRenderTexture(ref normalPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload A");
			ComputeHelper.CreateRenderTexture(ref normalPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload B");
			materialMaps.EnsureRenderTextures(_currentMaterialSize.x, _currentMaterialSize.y, _currentSourceSize.x, _currentSourceSize.y, "JFA");
			if (result != null && (result.width != width || result.height != height))
			{
				result = null;
				payloadResult = null;
				normalPayloadResult = null;
			}
		}

		void RecordDisplay(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (TryRecordLitDisplay(display, cam, targetCommandBuffer, finalTarget, lighting))
			{
				targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
				display.AppendVectorFieldDraw(targetCommandBuffer);
				targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
				return;
			}

			displayMaterial.SetTexture("_PayloadTex", payloadResult != null ? payloadResult : payloadA);
			displayMaterial.SetMatrix("_InverseViewProjection", (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
			displayMaterial.SetVector("boundsSize",display.sim.boundsSize);

			targetCommandBuffer.BeginSample("Jump Flood/Display Fallback");
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(targetCommandBuffer, display.sim.analyticBoundary, _currentRenderRegion);
			targetCommandBuffer.SetRenderTarget(finalTarget);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);
			displayMaterial.SetTexture("_ResultTex", result != null ? result : seedA);
			targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderRegion.CreateRegionMatrix(), displayMaterial, 0, 0);
			targetCommandBuffer.EndSample("Jump Flood/Display Fallback");
			targetCommandBuffer.BeginSample("Particle Fluid/Vector Field");
			display.AppendVectorFieldDraw(targetCommandBuffer);
			targetCommandBuffer.EndSample("Particle Fluid/Vector Field");
		}

		bool TryRecordLitDisplay(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget, ParticleFluidLighting2D lighting)
		{
			Shader materialShader = display.jumpFlood.materialShader != null
				? display.jumpFlood.materialShader
				: Shader.Find("Hidden/Particle2DJumpFloodMaterial");
			if (materialShader == null || !materialMaps.IsAllocated)
			{
				return false;
			}

			ParticleFluidRenderUtils.EnsureMaterial(ref materialMapMaterial, materialShader);
			if (materialMapMaterial == null)
			{
				return false;
			}

			ApplyMaterialMapSettings(display, cam);
			targetCommandBuffer.BeginSample("Jump Flood/Material Pipeline");
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(targetCommandBuffer, display.sim.analyticBoundary, _currentRenderRegion);
			materialMaps.RenderSurfaceMaps(targetCommandBuffer, materialMapMaterial, AlbedoPass, NormalPass, _currentRenderRegion, cam);
			materialMaps.RenderTransportMap(targetCommandBuffer, materialMapMaterial, TransportPass, _currentRenderRegion, cam);
			ClearFinalTarget(targetCommandBuffer, finalTarget);

			if (lighting == null)
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			Shader lightingShader = lighting.lightingShader != null
				? lighting.lightingShader
				: Shader.Find("Hidden/Particle2DParticleFluidLighting");
			lighting.EnsureMaterials(lightingShader);
			if (!(lighting.lightingMaterial != null))
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			materialMaps.BindTo(lighting);
			ParticleFluidLighting2D.FrameContext lightingContext = new ParticleFluidLighting2D.FrameContext(
				display,
				cam,
				_currentRenderRegion,
				_currentSourceSize
			);
			Vector2 projectedShadowDirection = Vector2.zero;
			ParticleFluidProjectedShadow.RecordParams projectedShadowParams = default;
			if (lighting.directLight.projectedShadow.ShouldRender(lighting.directLight)
			    && lighting.lightManager.GetMainDirectionalLight() is { } directionalLight)
			{
				Vector3 effectiveLightDirection = directionalLight.GetBoundaryRefractedDirection(lighting.PhaseMaterials[1].indexOfRefraction, display.sim.analyticBoundary);
				projectedShadowParams = new ParticleFluidProjectedShadow.RecordParams(
					lighting.directLight.projectedShadowCompute,
					lighting.directLight.projectedShadow.projectedShadowMapBuffer,
					lighting.directLight.projectedShadow.projectedShadowMapTexture,
					lighting.materialTransportTexture,
					lighting.directLight.projectedShadowMapBins,
					effectiveLightDirection,
					lightingContext.renderRegion,
					lightingContext.sourceSize);
			}
			bool useProjectedShadow = lighting.directLight.projectedShadow.ShouldRender(lighting.directLight)
			                          && lighting.directLight.projectedShadow.RecordCurrentShadowMap(targetCommandBuffer, projectedShadowParams, out projectedShadowDirection);
			lighting.ApplySettings(
				lightingContext,
				false,
				false,
				Texture2D.blackTexture
			);
			lighting.directLight.projectedShadow.ApplyToMaterial(lighting.lightingMaterial, useProjectedShadow, projectedShadowDirection, lighting.directLight.projectedShadowOffset, lighting.directLight.projectedShadowExpansion);
			lighting.lightingMaterial.SetTexture("SoftLightTex", Texture2D.blackTexture);
			lighting.lightingMaterial.SetTexture("SoftLightTexPhase1", Texture2D.blackTexture);
			lighting.Render(targetCommandBuffer, finalTarget, cam);
			targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
			return true;
		}

		static void ClearFinalTarget(CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			targetCommandBuffer.SetRenderTarget(finalTarget);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);
		}

		void ApplyMaterialMapSettings(ParticleDisplay2D display, Camera cam)
		{
			RenderTexture resultTexture = result != null ? result : seedA;
			RenderTexture payloadTexture = payloadResult != null ? payloadResult : payloadA;
			RenderTexture normalPayloadTexture = normalPayloadResult != null ? normalPayloadResult : normalPayloadA;
			materialMapMaterial.SetTexture("_ResultTex", resultTexture);
			materialMapMaterial.SetTexture("_PayloadTex", payloadTexture);
			materialMapMaterial.SetTexture("_NormalPayloadTex", normalPayloadTexture);
			materialMapMaterial.SetVector("_ResultTex_TexelSize", new Vector4(
				1f / Mathf.Max(resultTexture.width, 1),
				1f / Mathf.Max(resultTexture.height, 1),
				resultTexture.width,
				resultTexture.height
			));
			materialMapMaterial.SetMatrix("_InverseViewProjection", (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
			materialMapMaterial.SetVector("boundsSize", display.sim.boundsSize);
		}

		void GetRenderLayout(ParticleDisplay2D display, Camera cam, out Bounds renderRegion, out Vector2Int sourceSize, out Vector2Int materialSize)
		{
			Bounds cropRegion = GetRenderRegion(display, cam, out Vector2Int cropResolution);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			renderRegion = cropRegion;
			sourceSize = ScaleSize(cropResolution, sourceScale);
			materialSize = ScaleSize(cropResolution, materialScale);
		}

		Bounds GetRenderRegion(ParticleDisplay2D display, Camera cam, out Vector2Int resolution)
		{			
			int fullWidth = Mathf.Max(cam.pixelWidth, 1);
			int fullHeight = Mathf.Max(cam.pixelHeight, 1);
			resolution = new Vector2Int(fullWidth, fullHeight);
			Bounds fullRegion = ParticleFluidRenderRegion2D.Full(cam);
			if (display == null || cam == null || !display.sim.analyticBoundary.useEllipticalBounds || display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return fullRegion;
			}

			float cameraWorldUnitsPerPixel = fullRegion.size.y / Mathf.Max(fullHeight, 1);

			Vector2 cameraMin = fullRegion.min;
			Vector2 cameraMax = fullRegion.max;
			Vector2 cropMin = Vector2.Max(cameraMin, display.sim.analyticBoundary.BoundsMin);
			Vector2 cropMax = Vector2.Min(cameraMax, display.sim.analyticBoundary.BoundsMax);
			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				resolution = Vector2Int.one;
				return new Bounds(fullRegion.center, new Vector3(cameraWorldUnitsPerPixel, cameraWorldUnitsPerPixel, 0f));
			}

			float uvMinX = Mathf.Clamp01((cropMin.x - cameraMin.x) / fullRegion.size.x);
			float uvMinY = Mathf.Clamp01((cropMin.y - cameraMin.y) / fullRegion.size.y);
			float uvMaxX = Mathf.Clamp01((cropMax.x - cameraMin.x) / fullRegion.size.x);
			float uvMaxY = Mathf.Clamp01((cropMax.y - cameraMin.y) / fullRegion.size.y);
			int x = Mathf.Clamp(Mathf.FloorToInt(uvMinX * fullWidth), 0, Mathf.Max(fullWidth - 1, 0));
			int y = Mathf.Clamp(Mathf.FloorToInt(uvMinY * fullHeight), 0, Mathf.Max(fullHeight - 1, 0));
			int xMax = Mathf.Clamp(Mathf.CeilToInt(uvMaxX * fullWidth), x + 1, fullWidth);
			int yMax = Mathf.Clamp(Mathf.CeilToInt(uvMaxY * fullHeight), y + 1, fullHeight);
			int pixelWidth = Mathf.Max(1, xMax - x);
			int pixelHeight = Mathf.Max(1, yMax - y);
			if (pixelWidth >= fullWidth - 1 && pixelHeight >= fullHeight - 1)
			{
				return fullRegion;
			}
			resolution = new Vector2Int(pixelWidth, pixelHeight);

			Vector4 compositeUvRect = new Vector4(
				x / (float)fullWidth,
				y / (float)fullHeight,
				pixelWidth / (float)fullWidth,
				pixelHeight / (float)fullHeight
			);
			Vector2 worldMin = cameraMin + Vector2.Scale(new Vector2(compositeUvRect.x, compositeUvRect.y), fullRegion.size);
			Vector2 worldSize = Vector2.Scale(new Vector2(compositeUvRect.z, compositeUvRect.w), fullRegion.size);
			return new Bounds(worldMin + worldSize * 0.5f, worldSize);
		}

		static Vector2Int ScaleSize(Vector2Int baseSize, float scale)
		{
			Vector2 size = new Vector2(Mathf.Max(baseSize.x, 1), Mathf.Max(baseSize.y, 1));
			return Vector2Int.Max(Vector2Int.one, Vector2Int.RoundToInt(size * scale));
		}

		static ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			if (display == null)
			{
				return null;
			}

			ParticleFluidLighting2D lighting = display.Lighting;
			return lighting != null && lighting.isActiveAndEnabled ? lighting : null;
		}
	}
}

