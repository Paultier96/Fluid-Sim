using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	internal sealed class JumpFloodRenderer2D
	{
		const string CommandBufferName = "Sim2D Jump Flood Render";

		Material displayMaterial;
		RenderTexture seedA;
		RenderTexture seedB;
		RenderTexture payloadA;
		RenderTexture payloadB;
		RenderTexture result;
		RenderTexture payloadResult;
		CommandBuffer commandBuffer;
		bool commandBufferAttached;

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
			seedA = null;
			seedB = null;
			payloadA = null;
			payloadB = null;
			result = null;
			payloadResult = null;

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
				compute.SetTexture(jumpFloodKernel, "_DstTex", dst);
				compute.SetTexture(jumpFloodKernel, "_DstPayloadTex", payloadDst);
				compute.Dispatch(jumpFloodKernel, gx, gy, 1);

				RenderTexture tmp = src;
				src = dst;
				dst = tmp;
				RenderTexture payloadTmp = payloadSrc;
				payloadSrc = payloadDst;
				payloadDst = payloadTmp;
			}

			result = src;
			payloadResult = payloadSrc;
		}

		void EnsureRenderTextures(ParticleDisplay2D display, Camera cam)
		{
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * display.metaballs.renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * display.metaballs.renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref seedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref seedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref payloadA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload A");
			ComputeHelper.CreateRenderTexture(ref payloadB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Payload B");
			if (result != null && (result.width != width || result.height != height))
			{
				result = null;
				payloadResult = null;
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
			displayMaterial.SetTexture("_PayloadTex", payloadResult != null ? payloadResult : payloadA);
			displayMaterial.SetMatrix("_InverseViewProjection", (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
			displayMaterial.SetInt("useEllipticalBounds", display.sim.useEllipticalBounds ? 1 : 0);
			displayMaterial.SetVector("ellipseBoundsCenter", new Vector4(display.sim.ellipseBoundsCenter.x, display.sim.ellipseBoundsCenter.y, 0f, 0f));
			displayMaterial.SetVector("ellipseBoundsSize", new Vector4(display.sim.ellipseBoundsSize.x, display.sim.ellipseBoundsSize.y, 0f, 0f));
			displayMaterial.SetVector("boundsSize", new Vector4(display.sim.boundsSize.x, display.sim.boundsSize.y, 0f, 0f));
			displayMaterial.SetFloat("obstacleY", display.sim.obstacleY);

			targetCommandBuffer.SetRenderTarget(finalTarget);
			targetCommandBuffer.ClearRenderTarget(false, true, Color.black);
			targetCommandBuffer.Blit(result != null ? result : seedA, finalTarget, displayMaterial);
			display.AppendVectorFieldDraw(targetCommandBuffer);
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
