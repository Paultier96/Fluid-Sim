using Seb.Helpers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

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
		private Vector2Int _currentCausticSize;

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

			PrepareResources(display, cam);
			return true;
		}

		public void RecordCompositeWithPreparedLightingAndMaterialMaps(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			RecordPreparedLightingDisplay(display, cam, targetCommandBuffer, finalTarget);
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

		internal RenderTexture SeedA => seedA;
		internal RenderTexture SeedB => seedB;
		internal RenderTexture PayloadA => payloadA;
		internal RenderTexture PayloadB => payloadB;
		internal RenderTexture NormalPayloadA => normalPayloadA;
		internal RenderTexture NormalPayloadB => normalPayloadB;
		internal ParticleFluidMaterialMapSet MaterialMaps => materialMaps;
		internal Bounds CurrentRenderRegion => _currentRenderRegion;
		internal Vector2Int CurrentSourceSize => _currentSourceSize;

		void PrepareResources(ParticleDisplay2D display, Camera cam)
		{
			Bounds cropRegion = GetRenderRegion(display, cam, out Vector2Int cropResolution);
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			float sourceScale = Mathf.Max(display.metaballs.renderTextureScale, 0.0001f);
			float materialScale = lighting != null ? lighting.materialMapTextureScale : 1f;
			float causticScale = lighting != null ? lighting.directLight.textureScale : sourceScale;
			_currentRenderRegion = cropRegion;
			_currentSourceSize = ParticleFluidRenderBounds2D.ScaleSize(cropResolution, sourceScale);
			_currentMaterialSize = ParticleFluidRenderBounds2D.ScaleSize(cropResolution, materialScale);
			_currentCausticSize = ParticleFluidRenderBounds2D.ScaleSize(cropResolution, causticScale);
			EnsureRenderTextures();
			if (lighting != null)
			{
				lighting.EnsureLightingResources(_currentRenderRegion, _currentCausticSize);
			}
		}

		void ApplyJumpFloodComputeParams(ParticleDisplay2D display, Camera cam, IComputeCommandBuffer computeCommandBuffer, ComputeShader compute)
		{
			int width = _currentSourceSize.x;
			int height = _currentSourceSize.y;
			Matrix4x4 viewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;
			computeCommandBuffer.SetComputeIntParam(compute, "_Width", width);
			computeCommandBuffer.SetComputeIntParam(compute, "_Height", height);
			computeCommandBuffer.SetComputeMatrixParam(compute, "_VP", viewProjection);
			computeCommandBuffer.SetComputeVectorParam(compute, "_RenderWorldCenter", _currentRenderRegion.center);
			computeCommandBuffer.SetComputeVectorParam(compute, "_RenderWorldSize", _currentRenderRegion.size);
			computeCommandBuffer.SetComputeFloatParam(compute, "_TempMin", display.sim.ambientTemperature);
			computeCommandBuffer.SetComputeFloatParam(compute, "_TempMax", display.sim.HeatSourceTemperature);
			computeCommandBuffer.SetComputeIntParam(compute, "_ParticleCount", display.sim.positionBuffer.count);
			computeCommandBuffer.SetComputeIntParam(compute, "debugMode", (int)display.debugMode);
			computeCommandBuffer.SetComputeIntParam(compute, "debugShowClipping", display.debugShowClipping ? 1 : 0);
			computeCommandBuffer.SetComputeIntParam(compute, "useLinearColorSpace", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
			computeCommandBuffer.SetComputeFloatParam(compute, "debugGradientMax", display.debugGradientMax);
			computeCommandBuffer.SetComputeFloatParam(compute, "debugCurvatureMax", display.sim.MaxDebugCurvature);
			computeCommandBuffer.SetComputeFloatParam(compute, "debugViscosityMax", display.sim.MaxDebugViscosity);
			computeCommandBuffer.SetComputeFloatParam(compute, "debugDensityMin", display.DebugDensityMin);
			computeCommandBuffer.SetComputeFloatParam(compute, "debugDensityMax", display.DebugDensityMax);
		}

		internal void RecordClear(ParticleDisplay2D display, Camera cam, IComputeCommandBuffer computeCommandBuffer, TextureHandle resultHandle, TextureHandle payloadHandle, TextureHandle normalPayloadHandle)
		{
			ComputeShader compute = display.jumpFlood.computeShader;
			int clearKernel = compute.FindKernel("Clear");
			ApplyJumpFloodComputeParams(display, cam, computeCommandBuffer, compute);
			computeCommandBuffer.SetComputeTextureParam(compute, clearKernel, "Result", resultHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultPayload", payloadHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, clearKernel, "ResultNormalPayload", normalPayloadHandle);
			computeCommandBuffer.DispatchCompute(compute, clearKernel, Mathf.CeilToInt(_currentSourceSize.x / 16f), Mathf.CeilToInt(_currentSourceSize.y / 16f), 1);
		}

		internal void RecordSeed(ParticleDisplay2D display, Camera cam, IComputeCommandBuffer computeCommandBuffer, TextureHandle resultHandle, TextureHandle payloadHandle, TextureHandle normalPayloadHandle, TextureHandle gradientHandle, TextureHandle gradient2Handle, TextureHandle debugHeatMapHandle, TextureHandle debugSignedHeatMapHandle)
		{
			ComputeShader compute = display.jumpFlood.computeShader;
			int seedKernel = compute.FindKernel("Seed");
			ApplyJumpFloodComputeParams(display, cam, computeCommandBuffer, compute);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "Positions2D", display.sim.positionBuffer);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "DensityData", display.sim.densityBuffer);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "Temperatures", display.sim.temperatureBuffer);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "DebugData", display.sim.debugDataBuffer);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "Phases", display.sim.phaseBuffer);
			computeCommandBuffer.SetComputeBufferParam(compute, seedKernel, "BlobIDs", display.sim.blobIdBuffer);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "Result", resultHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultPayload", payloadHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ResultNormalPayload", normalPayloadHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap", gradientHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "ColourMap2", gradient2Handle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "DebugHeatMap", debugHeatMapHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, seedKernel, "DebugSignedHeatMap", debugSignedHeatMapHandle);
			computeCommandBuffer.DispatchCompute(compute, seedKernel, Mathf.Max(1, Mathf.CeilToInt(display.sim.positionBuffer.count / 64.0f)), 1, 1);
		}

		internal void RecordStep(ParticleDisplay2D display, Camera cam, IComputeCommandBuffer computeCommandBuffer, int step, TextureHandle srcHandle, TextureHandle payloadSrcHandle, TextureHandle normalPayloadSrcHandle, TextureHandle dstHandle, TextureHandle payloadDstHandle, TextureHandle normalPayloadDstHandle)
		{
			ComputeShader compute = display.jumpFlood.computeShader;
			int jumpFloodKernel = compute.FindKernel("JumpFlood");
			ApplyJumpFloodComputeParams(display, cam, computeCommandBuffer, compute);
			computeCommandBuffer.SetComputeIntParam(compute, "_Step", step);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcTex", srcHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcPayloadTex", payloadSrcHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_SrcNormalPayloadTex", normalPayloadSrcHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstTex", dstHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstPayloadTex", payloadDstHandle);
			computeCommandBuffer.SetComputeTextureParam(compute, jumpFloodKernel, "_DstNormalPayloadTex", normalPayloadDstHandle);
			computeCommandBuffer.DispatchCompute(compute, jumpFloodKernel, Mathf.CeilToInt(_currentSourceSize.x / 16f), Mathf.CeilToInt(_currentSourceSize.y / 16f), 1);
		}

		internal int GetInitialJumpFloodStep()
		{
			int step = 1;
			int maxDim = Mathf.Max(_currentSourceSize.x, _currentSourceSize.y);
			while ((step << 1) < maxDim)
			{
				step <<= 1;
			}
			return step;
		}

		internal void SetResults(RenderTexture resultTexture, RenderTexture payloadTexture, RenderTexture normalPayloadTexture)
		{
			result = resultTexture;
			payloadResult = payloadTexture;
			normalPayloadResult = normalPayloadTexture;
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

		void RecordPreparedLightingDisplay(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget)
		{
			ParticleFluidLighting2D lighting = GetActiveLighting(display);
			if (TryRecordPreparedLightingLitDisplay(display, cam, targetCommandBuffer, finalTarget, lighting))
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

		bool TryRecordPreparedLightingLitDisplay(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer, RenderTargetIdentifier finalTarget, ParticleFluidLighting2D lighting)
		{
			if (lighting == null)
			{
				if (materialMapMaterial == null)
				{
					return false;
				}
				ClearFinalTarget(targetCommandBuffer, finalTarget);
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderRegion);
				return true;
			}

			Shader lightingShader = lighting.lightingShader != null
				? lighting.lightingShader
				: Shader.Find("Hidden/Particle2DParticleFluidLighting");
			lighting.EnsureMaterials(lightingShader);
			if (!(lighting.lightingMaterial != null))
			{
				if (materialMapMaterial == null)
				{
					return false;
				}
				ClearFinalTarget(targetCommandBuffer, finalTarget);
				materialMaps.RenderUnlit(targetCommandBuffer, materialMapMaterial, finalTarget, UnlitPass, _currentRenderRegion);
				return true;
			}

			ParticleFluidLightingInputSet lightingInputs = materialMaps.CreateLightingInputs(_currentRenderRegion, _currentSourceSize);
			ParticleFluidLighting2D.FrameContext lightingContext = lighting.PrepareLighting(cam, lightingInputs);
			ClearFinalTarget(targetCommandBuffer, finalTarget);
			lighting.RenderLit(targetCommandBuffer, finalTarget, cam, lightingContext, lightingInputs.transportTexture);
			return true;
		}

		internal void RecordMaterialMaps(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			if (targetCommandBuffer == null)
			{
				return;
			}

			BuildMaterialMaps(display, cam, targetCommandBuffer);
		}

		void BuildMaterialMaps(ParticleDisplay2D display, Camera cam, CommandBuffer targetCommandBuffer)
		{
			if (materialMapMaterial == null)
			{
				Shader materialShader = display.jumpFlood.materialShader != null
					? display.jumpFlood.materialShader
					: Shader.Find("Hidden/Particle2DJumpFloodMaterial");
				ParticleFluidRenderUtils.EnsureMaterial(ref materialMapMaterial, materialShader);
			}

			if (materialMapMaterial == null)
			{
				return;
			}

			ApplyMaterialMapSettings(display, cam);
			ParticleFluidLayoutBindings.ApplyLayoutGlobals(targetCommandBuffer, display.sim.analyticBoundary, _currentRenderRegion);
			materialMaps.RenderSurfaceMaps(targetCommandBuffer, materialMapMaterial, AlbedoPass, NormalPass, _currentRenderRegion, cam);
			materialMaps.RenderTransportMap(targetCommandBuffer, materialMapMaterial, TransportPass, _currentRenderRegion, cam);
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

		Bounds GetRenderRegion(ParticleDisplay2D display, Camera cam, out Vector2Int resolution)
		{
			bool crop = display != null && cam != null && display.sim.analyticBoundary.useEllipticalBounds && display.debugMode == ParticleDisplay2D.DebugVisualization.None;
			Bounds? cropBounds = crop ? display.sim.analyticBoundary.GetBounds() : null;
			return ParticleFluidRenderBounds2D.GetCameraRenderRegion(cam, cropBounds, crop, out resolution);
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

