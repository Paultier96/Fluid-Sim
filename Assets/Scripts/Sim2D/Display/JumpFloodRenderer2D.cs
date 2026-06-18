using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class JumpFloodRenderer2D
	{
		const string CommandBufferName = "Sim2D Jump Flood Render";
		const int AlbedoPass = 0;
		const int Normal0Pass = 1;
		const int Normal1Pass = 2;
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
		CommandBuffer commandBuffer;
		bool commandBufferAttached;
		ParticleFluidRenderRegion2D currentRenderRegion;

		public void Render(ParticleDisplay2D display, Camera cam)
		{
			if (display.jumpFlood.computeShader == null || display.jumpFlood.displayShader == null || cam == null)
			{
				display.MetaballRenderer?.RemoveCommandBuffer();
				RemoveCommandBuffer();
				return;
			}

			EnsureMaterial(ref displayMaterial, display.jumpFlood.displayShader);
			if (displayMaterial == null)
			{
				display.MetaballRenderer?.RemoveCommandBuffer();
				RemoveCommandBuffer();
				return;
			}

			RunJumpFlood(display, cam);
			BuildCommandBuffer(display, cam, BuiltinRenderTextureType.CameraTarget);
		}

		public void Record(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (display.jumpFlood.computeShader == null || display.jumpFlood.displayShader == null || cam == null || targetCommandBuffer == null)
			{
				return;
			}

			EnsureMaterial(ref displayMaterial, display.jumpFlood.displayShader);
			if (displayMaterial == null)
			{
				return;
			}

			RunJumpFlood(display, cam);
			RecordDisplay(display, cam, targetCommandBuffer, finalTarget);
		}

		public void RemoveCommandBuffer()
		{
			if (RenderPipelineManager.currentPipeline != null)
			{
				commandBufferAttached = false;
				return;
			}

			if (commandBuffer != null)
			{
				RemoveFromCamera(Camera.main);
			}

			commandBufferAttached = false;
		}

		public void Release()
		{
			RemoveCommandBuffer();
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

			if (commandBuffer != null)
			{
				commandBuffer.Release();
				commandBuffer = null;
			}

			if (displayMaterial != null)
			{
				Object.DestroyImmediate(displayMaterial);
				displayMaterial = null;
			}

			if (materialMapMaterial != null)
			{
				Object.DestroyImmediate(materialMapMaterial);
				materialMapMaterial = null;
			}
		}

		static void EnsureMaterial(ref Material material, Shader shader)
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

		void RunJumpFlood(ParticleDisplay2D display, Camera cam)
		{
			display.MetaballRenderer?.RemoveCommandBuffer();
			EnsureRenderTextures(display, cam);

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
			compute.SetVector("_RenderWorldCenter", new Vector4(currentRenderRegion.WorldCenter.x, currentRenderRegion.WorldCenter.y, 0f, 0f));
			compute.SetVector("_RenderWorldSize", new Vector4(currentRenderRegion.WorldSize.x, currentRenderRegion.WorldSize.y, 0f, 0f));
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

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			int fullWidth = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * display.metaballs.renderTextureScale));
			int fullHeight = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * display.metaballs.renderTextureScale));
			currentRenderRegion = GetRenderRegion(display, cam, fullWidth, fullHeight);
			int width = currentRenderRegion.PixelWidth;
			int height = currentRenderRegion.PixelHeight;

			ComputeHelper.CreateRenderTexture(ref seedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref seedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref payloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload A");
			ComputeHelper.CreateRenderTexture(ref payloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload B");
			ComputeHelper.CreateRenderTexture(ref normalPayloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload A");
			ComputeHelper.CreateRenderTexture(ref normalPayloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Normal Payload B");
			materialMaps.EnsureRenderTextures(width, height, "JFA");
			if (result != null && (result.width != width || result.height != height))
			{
				result = null;
				payloadResult = null;
				normalPayloadResult = null;
			}
		}

		void BuildCommandBuffer(ParticleDisplay2D display, Camera cam, RenderTargetIdentifier finalTarget)
		{
			EnsureCommandBuffer();
			if (commandBuffer == null)
			{
				return;
			}

			commandBuffer.Clear();
			RecordDisplay(display, cam, commandBuffer, finalTarget);
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
			displayMaterial.SetVector("jumpFloodWorldCenter", new Vector4(currentRenderRegion.WorldCenter.x, currentRenderRegion.WorldCenter.y, 0f, 0f));
			displayMaterial.SetVector("jumpFloodWorldSize", new Vector4(currentRenderRegion.WorldSize.x, currentRenderRegion.WorldSize.y, 0f, 0f));
			displayMaterial.SetInt("jumpFloodCompositeRegionEnabled", currentRenderRegion.IsCropped ? 1 : 0);
			displayMaterial.SetVector("jumpFloodCompositeUvRect", currentRenderRegion.SourceUvRect);
			displayMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			displayMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			displayMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			displayMaterial.SetVector("boundsSize", new Vector4(display.sim.boundsSize.x, display.sim.boundsSize.y, 0f, 0f));
			displayMaterial.SetFloat("obstacleY", display.sim.obstacleY);

			targetCommandBuffer.BeginSample("Jump Flood/Display Fallback");
			targetCommandBuffer.SetRenderTarget(finalTarget);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);
			targetCommandBuffer.Blit(result != null ? result : seedA, finalTarget, displayMaterial);
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

			EnsureMaterial(ref materialMapMaterial, materialShader);
			if (materialMapMaterial == null)
			{
				return false;
			}

			ApplyMaterialMapSettings(display, cam);
			ParticleFluidRenderRegion2D renderRegion = currentRenderRegion;
			targetCommandBuffer.BeginSample("Jump Flood/Material Pipeline");
			materialMaps.Render(targetCommandBuffer, materialMapMaterial, AlbedoPass, Normal0Pass, Normal1Pass);
			ClearFinalTarget(targetCommandBuffer, finalTarget);

			if (lighting == null)
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, renderRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			Shader lightingShader = lighting.lightingShader != null
				? lighting.lightingShader
				: Shader.Find("Hidden/Particle2DParticleFluidLighting");
			lighting.EnsureMaterial(lightingShader);
			if (!lighting.IsReady)
			{
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, renderRegion);
				targetCommandBuffer.EndSample("Jump Flood/Material Pipeline");
				return true;
			}

			materialMaps.BindTo(lighting, renderRegion);
			ParticleFluidLighting2D.FrameContext lightingContext = new ParticleFluidLighting2D.FrameContext(
				display,
				cam,
				renderRegion,
				renderRegion,
				display.GetZoomScale(cam),
				MetaballRenderer2D.GetAnalyticBoundaryExpansion(display)
			);
			lighting.ApplySettings(
				lightingContext,
				false,
				false,
				false,
				false,
				Texture2D.blackTexture,
				Texture2D.blackTexture
			);
			lighting.SetSoftLightTexture(Texture2D.blackTexture);
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
			materialMapMaterial.SetVector("jumpFloodWorldCenter", new Vector4(currentRenderRegion.WorldCenter.x, currentRenderRegion.WorldCenter.y, 0f, 0f));
			materialMapMaterial.SetVector("jumpFloodWorldSize", new Vector4(currentRenderRegion.WorldSize.x, currentRenderRegion.WorldSize.y, 0f, 0f));
			materialMapMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			materialMapMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			materialMapMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			materialMapMaterial.SetVector("boundsSize", new Vector4(display.sim.boundsSize.x, display.sim.boundsSize.y, 0f, 0f));
			materialMapMaterial.SetFloat("obstacleY", display.sim.obstacleY);
		}

		ParticleFluidRenderRegion2D GetRenderRegion(ParticleDisplay2D display, Camera cam, int fullWidth, int fullHeight)
		{
			ParticleFluidRenderRegion2D fullRegion = ParticleFluidRenderRegion2D.Full(cam, fullWidth, fullHeight);
			if (display == null || cam == null || !display.sim.useEllipticalBounds || display.debugMode != ParticleDisplay2D.DebugVisualization.None)
			{
				return fullRegion;
			}

			float expansion = display.metaballs.analyticBoundaryPadding;
			float cameraWorldUnitsPerPixel = fullRegion.WorldSize.y / Mathf.Max(fullHeight, 1);
			Vector2 center = display.sim.ellipseBoundsCenter;
			Vector2 radii = new Vector2(Mathf.Abs(display.sim.ellipseBoundsSize.x), Mathf.Abs(display.sim.ellipseBoundsSize.y)) + Vector2.one * expansion;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return fullRegion;
			}

			float cutY = display.sim.obstacleY - expansion;
			Vector2 cameraMin = fullRegion.WorldCenter - fullRegion.WorldSize * 0.5f;
			Vector2 cameraMax = fullRegion.WorldCenter + fullRegion.WorldSize * 0.5f;
			Vector2 boundsMin = new Vector2(center.x - radii.x, Mathf.Max(center.y - radii.y, cutY));
			Vector2 boundsMax = new Vector2(center.x + radii.x, center.y + radii.y);
			Vector2 cropMin = Vector2.Max(cameraMin, boundsMin);
			Vector2 cropMax = Vector2.Min(cameraMax, boundsMax);
			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				return new ParticleFluidRenderRegion2D(fullRegion.WorldCenter, Vector2.one * cameraWorldUnitsPerPixel, Vector4.zero, 1, 1, true);
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

			Vector4 sourceUvRect = new Vector4(
				x / (float)fullWidth,
				y / (float)fullHeight,
				pixelWidth / (float)fullWidth,
				pixelHeight / (float)fullHeight
			);
			Vector2 worldMin = cameraMin + new Vector2(sourceUvRect.x * fullRegion.WorldSize.x, sourceUvRect.y * fullRegion.WorldSize.y);
			Vector2 worldSize = new Vector2(sourceUvRect.z * fullRegion.WorldSize.x, sourceUvRect.w * fullRegion.WorldSize.y);
			Vector2 worldCenter = worldMin + worldSize * 0.5f;
			return new ParticleFluidRenderRegion2D(worldCenter, worldSize, sourceUvRect, pixelWidth, pixelHeight, true);
		}

		static ParticleFluidLighting2D GetActiveLighting(ParticleDisplay2D display)
		{
			if (display == null)
			{
				return null;
			}

			ParticleFluidLighting2D lighting = display.GetComponent<ParticleFluidLighting2D>();
			return lighting != null && lighting.isActiveAndEnabled ? lighting : null;
		}

		void EnsureCommandBuffer()
		{
			if (RenderPipelineManager.currentPipeline != null)
			{
				return;
			}

			Camera cam = Camera.main;
			if (commandBuffer == null)
			{
				commandBuffer = new CommandBuffer { name = CommandBufferName };
			}

			if (!commandBufferAttached && cam != null)
			{
				cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
				commandBufferAttached = true;
			}
		}

		void RemoveFromCamera(Camera cam)
		{
			if (RenderPipelineManager.currentPipeline != null)
			{
				return;
			}

			if (cam == null || commandBuffer == null)
			{
				return;
			}

			cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, commandBuffer);
		}
	}
}
