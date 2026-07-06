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
		readonly ParticleFluidMaterialMapSet materialMaps = new ParticleFluidMaterialMapSet();
		private ParticleFluidRenderLayout2D _currentRenderLayout;

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
			EnsureRenderTextures(GetRenderLayout(display, cam));

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
			compute.SetVector("_RenderWorldCenter", _currentRenderLayout.domainRegion.WorldCenter);
			compute.SetVector("_RenderWorldSize", _currentRenderLayout.domainRegion.WorldSize);
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

				RenderTexture tmp = src;
				src = dst;
				dst = tmp;
				RenderTexture payloadTmp = payloadSrc;
				payloadSrc = payloadDst;
				payloadDst = payloadTmp;
				RenderTexture normalPayloadTmp = normalPayloadSrc;
				normalPayloadSrc = normalPayloadDst;
				normalPayloadDst = normalPayloadTmp;
			}

			result = src;
			payloadResult = payloadSrc;
			normalPayloadResult = normalPayloadSrc;
		}

		void EnsureRenderTextures(ParticleFluidRenderLayout2D currentRenderLayout)
		{
			int width = currentRenderLayout.sourceSize.x;
			int height = currentRenderLayout.sourceSize.y;

			ComputeHelper.CreateRenderTexture(ref seedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref seedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref payloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload A");
			ComputeHelper.CreateRenderTexture(ref payloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload B");
			ComputeHelper.CreateRenderTexture(ref normalPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload A");
			ComputeHelper.CreateRenderTexture(ref normalPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload B");
			materialMaps.EnsureRenderTextures(_currentRenderLayout.materialSize.x, _currentRenderLayout.materialSize.y, currentRenderLayout.sourceSize.x, currentRenderLayout.sourceSize.y, "JFA");
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
			ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
			ParticleFluidRasterLayoutBindings.ApplyJumpFloodGlobals(targetCommandBuffer, _currentRenderLayout.domainRegion);
			targetCommandBuffer.SetRenderTarget(finalTarget);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);
			displayMaterial.SetTexture("_ResultTex", result != null ? result : seedA);
			targetCommandBuffer.DrawMesh(ParticleFluidRenderUtils.GetQuadMesh(), _currentRenderLayout.domainRegion.CreateRegionMatrix(), displayMaterial, 0, 0);
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
			ParticleFluidAnalyticBoundaryBindings.ApplyBoundaryGlobals(targetCommandBuffer, display.sim != null ? display.sim.analyticBoundary : null);
			ParticleFluidRasterLayoutBindings.ApplyJumpFloodGlobals(targetCommandBuffer, _currentRenderLayout.domainRegion);
			materialMaps.RenderSurfaceMaps(targetCommandBuffer, materialMapMaterial, AlbedoPass, NormalPass, _currentRenderLayout.MaterialRegion, cam);
			materialMaps.RenderTransportMap(targetCommandBuffer, materialMapMaterial, TransportPass, _currentRenderLayout.SourceRegion, cam);
			ClearFinalTarget(targetCommandBuffer, finalTarget);

			if (lighting == null)
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderLayout.domainRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			Shader lightingShader = lighting.lightingShader != null
				? lighting.lightingShader
				: Shader.Find("Hidden/Particle2DParticleFluidLighting");
			lighting.EnsureMaterials(lightingShader);
			if (!(lighting.lightingMaterial != null))
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderLayout.domainRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			materialMaps.BindTo(lighting);
			ParticleFluidLighting2D.FrameContext lightingContext = new ParticleFluidLighting2D.FrameContext(
				display,
				cam,
				_currentRenderLayout,
				display.GetZoomScale(cam)
			);
			Vector2 projectedShadowDirection = Vector2.zero;
			ParticleFluidProjectedShadow.RecordParams projectedShadowParams = default;
			if (lighting.directLight.projectedShadow.ShouldRender(lighting.directLight)
			    && lighting.lightManager.GetMainDirectionalLight() is ParticleFluidDirectionalLight2D directionalLight)
			{
				Vector3 effectiveLightDirection = directionalLight.GetDirectLightingDirection(lighting, display.sim.analyticBoundary);
				projectedShadowParams = new ParticleFluidProjectedShadow.RecordParams(
					lighting.directLight.projectedShadowCompute,
					lighting.directLight.projectedShadow.projectedShadowMapBuffer,
					lighting.directLight.projectedShadow.projectedShadowMapTexture,
					lighting.materialTransportTexture,
					lighting.directLight.projectedShadowMapBins,
					effectiveLightDirection,
					lightingContext.renderLayout.SourceRegion,
					lightingContext.renderLayout.CausticRegion);
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

		ParticleFluidRenderLayout2D GetRenderLayout(ParticleDisplay2D display, Camera cam)
		{
			ParticleFluidRenderRegion2D cropRegion = GetRenderRegion(display, cam);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			ParticleFluidRenderRegion2D domainRegion = new ParticleFluidRenderRegion2D(
				cropRegion.WorldCenter,
				cropRegion.WorldSize,
				cropRegion.pixelSize);
			return new ParticleFluidRenderLayout2D(
				domainRegion,
				cropRegion.ScaledSize(sourceScale),
				cropRegion.ScaledSize(materialScale),
				cropRegion.ScaledSize(sourceScale));
		}

		ParticleFluidRenderRegion2D GetRenderRegion(ParticleDisplay2D display, Camera cam)
		{			
			int fullWidth = Mathf.Max(cam.pixelWidth, 1);
			int fullHeight = Mathf.Max(cam.pixelHeight, 1); 
			ParticleFluidRenderRegion2D fullRegion = ParticleFluidRenderRegion2D.Full(cam);
			if (display == null || cam == null || !display.sim.analyticBoundary.useEllipticalBounds || display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return fullRegion;
			}

			float cameraWorldUnitsPerPixel = fullRegion.WorldSize.y / Mathf.Max(fullHeight, 1);

			Vector2 cameraMin = fullRegion.worldBounds.min;
			Vector2 cameraMax = fullRegion.worldBounds.max;
			Vector2 cropMin = Vector2.Max(cameraMin, display.sim.analyticBoundary.BoundsMin);
			Vector2 cropMax = Vector2.Min(cameraMax, display.sim.analyticBoundary.BoundsMax);
			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				return new ParticleFluidRenderRegion2D(new Bounds(fullRegion.worldBounds.center, new Vector3(cameraWorldUnitsPerPixel, cameraWorldUnitsPerPixel, 0f)), 1, 1);
			}

			float uvMinX = Mathf.Clamp01((cropMin.x - cameraMin.x) / fullRegion.WorldSize.x);
			float uvMinY = Mathf.Clamp01((cropMin.y - cameraMin.y) / fullRegion.WorldSize.y);
			float uvMaxX = Mathf.Clamp01((cropMax.x - cameraMin.x) / fullRegion.WorldSize.x);
			float uvMaxY = Mathf.Clamp01((cropMax.y - cameraMin.y) / fullRegion.WorldSize.y);
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

			Vector4 compositeUvRect = new Vector4(
				x / (float)fullWidth,
				y / (float)fullHeight,
				pixelWidth / (float)fullWidth,
				pixelHeight / (float)fullHeight
			);
			Vector2 worldMin = cameraMin + Vector2.Scale(new Vector2(compositeUvRect.x, compositeUvRect.y), fullRegion.WorldSize);
			Vector2 worldSize = Vector2.Scale(new Vector2(compositeUvRect.z, compositeUvRect.w), fullRegion.WorldSize);
			return new ParticleFluidRenderRegion2D(new Bounds(worldMin + worldSize * 0.5f, worldSize), pixelWidth, pixelHeight);
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
