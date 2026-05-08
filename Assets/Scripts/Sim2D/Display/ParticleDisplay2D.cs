using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Seb.Fluid2D.Rendering
{
	public class ParticleDisplay2D : MonoBehaviour
	{
		public enum RenderMode
		{
			DirectParticles,
			Metaballs,
			JumpFlood,
		}

		public enum DebugVisualization
		{
			None = 0,
			Gradient = 1,
			Curvature = 2,
			Force = 3,
			Viscosity = 4,
			Density = 5,
			Temperature = 6,
			BlobIds = 7,
		}

		[Tooltip("The fluid simulation to visualize.")]
		public FluidSim2D sim;
		[Tooltip("Mesh used for each particle (typically a quad).")]
		public Mesh mesh;
		[Tooltip("DirectParticles renders each particle as a coloured sprite. Metaballs blends particles into a smooth fluid surface.")]
		public RenderMode renderMode = RenderMode.DirectParticles;
		[Tooltip("Shader used when rendering individual particles directly.")]
		public Shader directParticleShader;
		[Tooltip("Shader used to accumulate per-particle density and temperature into the metaball render texture.")]
		public Shader metaballShader;
		[Tooltip("World-space radius of each particle sprite.")]
		public float scale;
		private Gradient[] colourMap;
		[Tooltip("Number of pixels in the gradient lookup texture. Higher values give smoother colour transitions.")]
		public int gradientResolution;
		[Tooltip("Velocity magnitude mapped to the top of the colour gradient.")]
		public float velocityDisplayMax;

		[Header("Metaball Rendering")]
		[Tooltip("When enabled, particles are composited into a smooth fluid surface via render textures. When disabled, falls back to direct particle rendering.")]
		public bool useRenderTextureMetaballs = true;
		private Camera targetCamera;
		[Tooltip("Shader that blits the blurred accumulation texture onto the camera, applying the density threshold and colour lookup.")]
		public Shader compositeShader;
		[Tooltip("Shader used for the separable Gaussian blur applied to the accumulation texture.")]
		public Shader blurShader;
		[Tooltip("Resolution of the metaball render textures relative to the screen. Lower values improve performance at the cost of sharpness.")]
		[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
		[Tooltip("Radius in pixels (at render texture resolution) of the Gaussian blur. Larger values make particles merge at greater distances.")]
		[Min(0)] public float blurRadius = 6;
		[Tooltip("Scales blur radius with camera zoom so metaball blending stays visually consistent while zooming.")]
		public bool scaleBlurWithZoom = true;
		[Tooltip("Blurred density value at which the fluid surface appears. Increase to shrink the visible fluid; decrease to expand it.")]
		[Min(0)] public float densityThreshold = 0.18f;
		[Tooltip("Width of the density falloff around the surface threshold. Larger values give a softer, more transparent edge. Clamped so the fade never starts below zero density.")]
		[Min(0.0001f)] public float edgeSoftness = 0.06f;
		[Tooltip("Width of the crossfade zone where two fluid phases blend into each other's colour.")]
		[Min(0.0001f)] public float phaseBlendWidth = 0.02f;
		[Tooltip("Steepness of each particle's density kernel (exp(-r² × sharpness)). Higher values make particles contribute a tighter, more localised density spike.")]
		[Min(0.01f)] public float metaballSharpness = 3.5f;
		[Tooltip("Uniform scale applied to each particle's density contribution. Increase if particles are too sparse to merge.")]
		[Min(0)] public float metaballIntensity = 1.0f;

		[Header("Debug")]
		[Tooltip("Selects what to show in debug mode. Press 0-7 to switch modes at runtime.")]
		public DebugVisualization debugMode = DebugVisualization.None;
		[Tooltip("Maximum absolute value mapped in debug visualisation (used by curvature/force views).")]
		public float debugGradientMax = 1.0f;
		[Tooltip("Lower density value used for density debug colour mapping.")]
		[Min(0f)] public float debugDensityMin = 0f;
		[Tooltip("Upper density value used for density debug colour mapping.")]
		[Min(0.0001f)] public float debugDensityMax = 500f;
		
		public float edgeWidth = 0.03f;


		Material directParticleMaterial;
		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		
		// Jump Flood related fields
		public ComputeShader jumpFloodCompute; // kept for compatibility but not used when raster pass is available
		public Shader jumpFloodDisplayShader;
		public Shader jumpFloodSeedShader;
		public Shader jumpFloodPassShader;
		public bool useComputeMethod = false;

		public float jFedgeWidth;
		public float blurstrength;
		
		Material jumpFloodDisplayMaterial;
		Material jumpFloodSeedMaterial;
		Material jumpFloodPassMaterial;
		RenderTexture jfaSeedA;
		RenderTexture jfaSeedB;
		RenderTexture jfaTemp;
		RenderTexture jfaResult;
		ComputeBuffer argsBuffer;
		Bounds bounds;
		Texture2D gradientTexture;
		Texture2D gradientTexture2;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		CommandBuffer metaballCommandBuffer;
		readonly List<Camera> boundCameras = new List<Camera>(2);
		readonly List<Camera> renderCameras = new List<Camera>(2);
		float blurReferenceOrthoSize = -1f;
		bool needsUpdate;

		void Awake()
		{
			EnsureMaterials();
			needsUpdate = true;
			targetCamera = Camera.main;
			EnsureBlurZoomReference(targetCamera);
		}

		void LateUpdate()
		{
			UpdateDebugModeFromKeyboard();

			EnsureMaterials();
			CollectRenderCameras(renderCameras);
			targetCamera = GetPrimaryRenderCamera(renderCameras);
			if (targetCamera == null)
			{
				return;
			}
			EnsureBlurZoomReference(targetCamera);

			if (renderMode == RenderMode.JumpFlood)
			{
				UpdateJumpFloodRender();
				UpdateJumpFloodCommandBuffer();
				return;
			}

			if (GetActiveParticleMaterial() != null)
			{
				UpdateSettings();

				if (renderMode == RenderMode.Metaballs && useRenderTextureMetaballs && compositeShader != null && blurShader != null)
				{
					UpdateMetaballRender();
				}
				else
				{
					RemoveCommandBuffer();
					Graphics.DrawMeshInstancedIndirect(mesh, 0, directParticleMaterial, bounds, argsBuffer);
				}
			}
		}

		void UpdateDebugModeFromKeyboard()
		{
			if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
			{
				debugMode = DebugVisualization.None;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
			{
				debugMode = DebugVisualization.Gradient;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
			{
				debugMode = DebugVisualization.Curvature;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
			{
				debugMode = DebugVisualization.Force;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
			{
				debugMode = DebugVisualization.Viscosity;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
			{
				debugMode = DebugVisualization.Density;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
			{
				debugMode = DebugVisualization.Temperature;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7))
			{
				debugMode = DebugVisualization.BlobIds;
			}
		}

        public void SetPhaseColors(Gradient[] gradients)
        {
	        colourMap = gradients;
	        needsUpdate = true;
        }

		void UpdateSettings()
		{
			EnsureGradientTextures();
			Material activeMaterial = GetActiveParticleMaterial();
			if (activeMaterial == null)
			{
				return;
			}

			BindSimulationBuffers(activeMaterial);

            ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);
			bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

			if (needsUpdate)
			{
				needsUpdate = false;
				ApplyGradientTextures();
			}
			
			ApplySharedParticleSettings(activeMaterial);
		}

		void EnsureMaterials()
		{
			EnsureMaterial(ref directParticleMaterial, directParticleShader);
			EnsureMaterial(ref metaballMaterial, metaballShader);
			EnsureMaterial(ref jumpFloodDisplayMaterial, jumpFloodDisplayShader);
			EnsureMaterial(ref jumpFloodSeedMaterial, jumpFloodSeedShader);
			EnsureMaterial(ref jumpFloodPassMaterial, jumpFloodPassShader);
		}

		void UpdateJumpFloodRender()
		{
			RemoveCommandBuffer(); // avoid metaball cb

			EnsureJumpFloodRenderTextures(targetCamera);

			int width = jfaSeedA.width;
			int height = jfaSeedA.height;

			int clearKernel = -1;
			int seedKernel = -1;
			int jfKernel = -1;
			if (useComputeMethod)
			{
				clearKernel = jumpFloodCompute.FindKernel("Clear");
				seedKernel = jumpFloodCompute.FindKernel("Seed");
				jfKernel = jumpFloodCompute.FindKernel("JumpFlood");
			}
			
			Matrix4x4 VP = targetCamera.projectionMatrix * targetCamera.worldToCameraMatrix;

			if (useComputeMethod)
			{
				jumpFloodCompute.SetInt("_Width", width);
				jumpFloodCompute.SetInt("_Height", height);
				jumpFloodCompute.SetMatrix("_VP", VP);
				jumpFloodCompute.SetFloat("_TempMin", sim.ambientTemperature);
				jumpFloodCompute.SetFloat("_TempMax", sim.HeatSourceTemperature);
				jumpFloodCompute.SetInt("_ParticleCount", sim.positionBuffer.count);

				// clear
				jumpFloodCompute.SetTexture(clearKernel, "Result", jfaSeedA);
				int gx = (width + 15) / 16; 
				int gy = (height + 15) / 16;				
				jumpFloodCompute.Dispatch(clearKernel, gx, gy, 1);

				// seed
				jumpFloodCompute.SetBuffer(seedKernel, "Positions2D", sim.positionBuffer);
				jumpFloodCompute.SetBuffer(seedKernel, "Temperatures", sim.temperatureBuffer);
				jumpFloodCompute.SetBuffer(seedKernel, "Phases", sim.phaseBuffer);
				jumpFloodCompute.SetTexture(seedKernel, "Result", jfaSeedA);
				int sg = Mathf.CeilToInt(sim.positionBuffer.count / 64.0f);
				jumpFloodCompute.Dispatch(seedKernel, Mathf.Max(1, sg), 1, 1);
			}
			else
			{
				ClearJumpFloodSeedTexture();
				DrawJumpFloodSeeds(VP);
			}

			// jump flood passes implemented as raster blits using jumpFloodPassMaterial
			RenderTexture src = jfaSeedA;
			RenderTexture dst = jfaSeedB;
			int maxDim = Mathf.Max(width, height);
			int step = 1;
			while ((step << 1) < maxDim) step <<= 1; //largest power of 2 that is <= maxDim
			for (int s = step; s >= 1; s >>= 1)
			{
				if (useComputeMethod)
				{
					if (src == dst)
					{
						Debug.LogError("READ/WRITE SAME TEXTURE!");
					}
					int gx = (width + 15) / 16; 
					int gy = (height + 15) / 16;	
					jumpFloodCompute.SetInt("_Step", s);
					jumpFloodCompute.SetTexture(jfKernel, "_SrcTex", src);
					jumpFloodCompute.SetTexture(jfKernel, "_DstTex", dst);
					jumpFloodCompute.Dispatch(jfKernel, gx, gy, 1);
				}
				else
				{
					if (jumpFloodPassMaterial == null)
					{
						// no pass material assigned -> can't perform jump flooding; leave seed texture as-is
						break;
					}
					jumpFloodPassMaterial.SetInt("_Step", s);
					jumpFloodPassMaterial.SetInt("_Width", width);
					jumpFloodPassMaterial.SetInt("_Height", height);
					jumpFloodPassMaterial.SetTexture("_SrcTex", src);
					RunJumpFloodRasterPass(src, dst, width, height);
				}

				// swap
				RenderTexture tmp = src;
				src = dst;
				dst = tmp;
			}

			// Keep A/B as stable ping-pong buffers across frames and track the current result separately.
			jfaResult = src;
		}

		void UpdateJumpFloodCommandBuffer()
		{
			Material mat = jumpFloodDisplayMaterial;
			if (mat == null) return;
			EnsureGradientTextures();
			EnsureCommandBuffers(renderCameras);
			mat.SetTexture("_SeedTex", jfaResult != null ? jfaResult : jfaSeedA);
			mat.SetTexture("ColourMap", gradientTexture);
			mat.SetTexture("ColourMap2", gradientTexture2);
			mat.SetFloat("tempMin", sim.ambientTemperature);
			mat.SetFloat("tempMax", sim.HeatSourceTemperature);
			mat.SetFloat("_EdgeWidth", jFedgeWidth);
			mat.SetFloat("_BlurStrength", blurstrength);

			metaballCommandBuffer.Clear();
			metaballCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.black);
			metaballCommandBuffer.Blit(jfaResult != null ? jfaResult : jfaSeedA, BuiltinRenderTextureType.CameraTarget, mat);
		}

		Material GetActiveParticleMaterial()
		{
			return renderMode == RenderMode.Metaballs ? metaballMaterial : directParticleMaterial;
		}

		void EnsureMaterial(ref Material material, Shader shader)
		{
			if (shader == null || (material != null && material.shader == shader))
			{
				return;
			}

			material = new Material(shader);
			needsUpdate = true;
		}

		void BindSimulationBuffers(Material targetMaterial)
		{
			targetMaterial.SetBuffer("Positions2D", sim.positionBuffer);
			targetMaterial.SetBuffer("Velocities", sim.velocityBuffer);
			targetMaterial.SetBuffer("DensityData", sim.densityBuffer);
			targetMaterial.SetBuffer("Phases", sim.phaseBuffer);
			targetMaterial.SetBuffer("IsGhost", sim.ghostFlagBuffer);
			targetMaterial.SetBuffer("BlobIDs", sim.blobIdBuffer);
			targetMaterial.SetBuffer("Temperatures", sim.temperatureBuffer);
		}

		void ApplySharedParticleSettings(Material targetMaterial)
		{
			targetMaterial.SetFloat("scale", scale);
			targetMaterial.SetFloat("velocityMax", velocityDisplayMax);
			targetMaterial.SetFloat("tempMin", sim.ambientTemperature);
			targetMaterial.SetFloat("tempMax", sim.HeatSourceTemperature);
			targetMaterial.SetBuffer("DebugData", sim.csfGradientBuffer);
			targetMaterial.SetFloat("debugGradientMax", debugGradientMax);
			targetMaterial.SetFloat("debugCurvatureMax", sim.MaxDebugCurvature);
			targetMaterial.SetFloat("debugViscosityMax", sim.MaxDebugViscosity);
			targetMaterial.SetFloat("debugDensityMin", DebugDensityMin);
			targetMaterial.SetFloat("debugDensityMax", DebugDensityMax);
			targetMaterial.SetInt("debugMode", (int)debugMode);
			targetMaterial.SetFloat("metaballSharpness", metaballSharpness);
			targetMaterial.SetFloat("metaballIntensity", metaballIntensity);
		}

		float DebugDensityMin => Mathf.Min(debugDensityMin, debugDensityMax - 0.0001f);
		float DebugDensityMax => Mathf.Max(debugDensityMax, debugDensityMin + 0.0001f);

		void ApplyGradientTextures()
		{
			if (directParticleMaterial != null)
			{
				directParticleMaterial.SetTexture("ColourMap", gradientTexture);
				directParticleMaterial.SetTexture("ColourMap2", gradientTexture2);
			}
		}

		void EnsureGradientTextures()
		{
			if (!needsUpdate)
			{
				return;
			}

			Gradient primary = GetGradient(0);
			Gradient secondary = GetGradient(1);
			TextureFromGradient(ref gradientTexture, gradientResolution, primary);
			TextureFromGradient(ref gradientTexture2, gradientResolution, secondary);
		}

		Gradient GetGradient(int index)
		{
			if (colourMap != null && colourMap.Length > index && colourMap[index] != null)
			{
				return colourMap[index];
			}

			Gradient gradient = new Gradient();
			gradient.SetKeys(
				new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) },
				new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
			);
			return gradient;
		}

		void UpdateMetaballRender()
		{
			EnsurePostProcessMaterials();

			EnsureCommandBuffers(renderCameras);
			EnsureRenderTextures(targetCamera);

			compositeMaterial.SetFloat("densityThreshold", densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", phaseBlendWidth);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("ColourMap", gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", gradientTexture2);
			compositeMaterial.SetInt("debugMode", (int)debugMode);

			blurMaterial.SetFloat("blurRadius", GetEffectiveBlurRadius(targetCamera));

			metaballCommandBuffer.Clear();
			metaballCommandBuffer.SetRenderTarget(combinedAccumulationTexture);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			metaballCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, metaballMaterial, 0, argsBuffer);

			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			metaballCommandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			metaballCommandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);

			metaballCommandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, compositeMaterial);
		}

		void EnsurePostProcessMaterials()
		{
			if (compositeMaterial == null || compositeMaterial.shader != compositeShader)
			{
				compositeMaterial = new Material(compositeShader);
			}

			if (blurMaterial == null || blurMaterial.shader != blurShader)
			{
				blurMaterial = new Material(blurShader);
			}
		}

		void EnsureCommandBuffers(List<Camera> cameras)
		{
			if (metaballCommandBuffer == null)
			{
				metaballCommandBuffer = new CommandBuffer();
				metaballCommandBuffer.name = "Sim2D Metaball Render";
			}

			for (int i = boundCameras.Count - 1; i >= 0; i--)
			{
				Camera bound = boundCameras[i];
				if (bound == null || !cameras.Contains(bound))
				{
					if (bound != null)
					{
						bound.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, metaballCommandBuffer);
						bound.RemoveCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
					}
					boundCameras.RemoveAt(i);
				}
			}

			for (int i = 0; i < cameras.Count; i++)
			{
				Camera cam = cameras[i];
				if (cam == null || boundCameras.Contains(cam))
				{
					continue;
				}

				CameraEvent evt = GetCommandBufferEvent(cam);
				cam.AddCommandBuffer(evt, metaballCommandBuffer);
				boundCameras.Add(cam);
			}
		}

		void EnsureRenderTextures(Camera cam)
		{
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");

			// Also ensure Jump Flood textures (used by compute shader)
			ComputeHelper.CreateRenderTexture(ref jfaSeedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref jfaSeedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref jfaTemp, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Temp");
		}

		void EnsureJumpFloodRenderTextures(Camera cam)
		{
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref jfaSeedA, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed A");
			ComputeHelper.CreateRenderTexture(ref jfaSeedB, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Seed B");
			ComputeHelper.CreateRenderTexture(ref jfaTemp, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Temp");
			if (jfaResult != null && (jfaResult.width != width || jfaResult.height != height))
			{
				jfaResult = null;
			}
		}

		void RemoveCommandBuffer()
		{
			if (metaballCommandBuffer != null)
			{
				for (int i = 0; i < boundCameras.Count; i++)
				{
					Camera cam = boundCameras[i];
					if (cam != null)
					{
						cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, metaballCommandBuffer);
						cam.RemoveCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
					}
				}
			}
			boundCameras.Clear();
		}

		void CollectRenderCameras(List<Camera> cameras)
		{
			cameras.Clear();
			AddCamera(cameras, Camera.main);
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView != null)
			{
				AddCamera(cameras, SceneView.lastActiveSceneView.camera);
			}
#endif
			if (cameras.Count == 0)
			{
				AddCamera(cameras, targetCamera);
			}
		}

		static void AddCamera(List<Camera> cameras, Camera cam)
		{
			if (cam != null && !cameras.Contains(cam))
			{
				cameras.Add(cam);
			}
		}

		static CameraEvent GetCommandBufferEvent(Camera cam)
		{
			return cam != null && cam.cameraType == CameraType.SceneView
				? CameraEvent.BeforeImageEffects
				: CameraEvent.AfterEverything;
		}

		void EnsureBlurZoomReference(Camera cam)
		{
			if (!scaleBlurWithZoom)
			{
				blurReferenceOrthoSize = -1f;
				return;
			}

			if (cam != null && cam.orthographic && blurReferenceOrthoSize <= 0f)
			{
				blurReferenceOrthoSize = cam.orthographicSize;
			}
		}

		float GetEffectiveBlurRadius(Camera cam)
		{
			float baseRadius = Mathf.Max(0f, blurRadius);
			if (!scaleBlurWithZoom || cam == null || !cam.orthographic)
			{
				return baseRadius;
			}

			if (blurReferenceOrthoSize <= 0f)
			{
				blurReferenceOrthoSize = cam.orthographicSize;
				return baseRadius;
			}

			float zoomScale = blurReferenceOrthoSize / Mathf.Max(cam.orthographicSize, 0.0001f);
			return baseRadius * zoomScale;
		}

		static Camera GetPrimaryRenderCamera(List<Camera> cameras)
		{
			if (cameras == null || cameras.Count == 0)
			{
				return null;
			}

#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView != null)
			{
				Camera sceneCam = SceneView.lastActiveSceneView.camera;
				if (sceneCam != null && cameras.Contains(sceneCam))
				{
					return sceneCam;
				}
			}
#endif

			Camera best = cameras[0];
			int bestPixels = best.pixelWidth * best.pixelHeight;
			for (int i = 1; i < cameras.Count; i++)
			{
				Camera cam = cameras[i];
				int pixels = cam.pixelWidth * cam.pixelHeight;
				if (pixels > bestPixels)
				{
					best = cam;
					bestPixels = pixels;
				}
			}
			return best;
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
		{
			width = Mathf.Max(1, width);

			if (texture == null)
			{
				texture = new Texture2D(width, 1);
			}
			else if (texture.width != width)
			{
				texture.Reinitialize(width, 1);
			}

			if (gradient == null)
			{
				gradient = new Gradient();
				gradient.SetKeys(
					new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
					new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
				);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width];
			for (int i = 0; i < cols.Length; i++)
			{
				float t = cols.Length == 1 ? 0 : i / (cols.Length - 1f);
				cols[i] = gradient.Evaluate(t);
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		void ClearJumpFloodSeedTexture()
		{
			RenderTexture previous = RenderTexture.active;
			Graphics.SetRenderTarget(jfaSeedA);
			GL.Clear(false, true, new Color(-1f, -1f, 0f, -1f));
			Graphics.SetRenderTarget(previous);
		}

		void DrawJumpFloodSeeds(Matrix4x4 viewProjectionMatrix)
		{
			if (jumpFloodSeedMaterial == null)
			{
				return;
			}

			jumpFloodSeedMaterial.SetBuffer("Positions2D", sim.positionBuffer);
			jumpFloodSeedMaterial.SetBuffer("Temperatures", sim.temperatureBuffer);
			jumpFloodSeedMaterial.SetBuffer("Phases", sim.phaseBuffer);
			jumpFloodSeedMaterial.SetInt("_ParticleCount", sim.positionBuffer.count);
			jumpFloodSeedMaterial.SetMatrix("_VP", viewProjectionMatrix);
			jumpFloodSeedMaterial.SetFloat("_TempMin", sim.ambientTemperature);
			jumpFloodSeedMaterial.SetFloat("_TempMax", sim.HeatSourceTemperature);

			DrawFullscreenPass(jfaSeedA, jumpFloodSeedMaterial, MeshTopology.Points, sim.positionBuffer.count);
		}

		void RunJumpFloodRasterPass(RenderTexture src, RenderTexture dst, int width, int height)
		{
			// Graphics.Blit with identical source and destination RT is undefined on some platforms/drivers.
			if (src == dst)
			{
				if (jfaTemp == null || jfaTemp.width != width || jfaTemp.height != height)
				{
					ComputeHelper.CreateRenderTexture(ref jfaTemp, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Temp");
				}

				DrawFullscreenPass(jfaTemp, jumpFloodPassMaterial, MeshTopology.Triangles, 3);
				Graphics.CopyTexture(jfaTemp, dst);
				return;
			}

			DrawFullscreenPass(dst, jumpFloodPassMaterial, MeshTopology.Triangles, 3);
		}

		void DrawFullscreenPass(RenderTexture target, Material material, MeshTopology topology, int vertexCount)
		{
			RenderTexture previous = RenderTexture.active;
			Graphics.SetRenderTarget(target);
			GL.Viewport(new Rect(0, 0, target.width, target.height));
			material.SetPass(0);
			Graphics.DrawProceduralNow(topology, vertexCount);
			Graphics.SetRenderTarget(previous);
		}

		void OnValidate()
		{
			needsUpdate = true;
			blurReferenceOrthoSize = -1f;
		}

		void OnGUI()
		{
			GUIStyle style = new GUIStyle(GUI.skin.box)
			{
				alignment = TextAnchor.MiddleLeft,
				fontSize = 20,
			};

			GUI.Box(new Rect(30, 30, 240, 30), $"Debug: {debugMode}", style);
		}

		void OnDisable()
		{
			RemoveCommandBuffer();
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			// Release jump flood textures
			ComputeHelper.Release(jfaSeedA, jfaSeedB, jfaTemp);
			RemoveCommandBuffer();
			if (metaballCommandBuffer != null)
			{
				metaballCommandBuffer.Release();
			}
			if (jumpFloodDisplayMaterial != null)
			{
				DestroyImmediate(jumpFloodDisplayMaterial);
			}
			if (jumpFloodSeedMaterial != null)
			{
				DestroyImmediate(jumpFloodSeedMaterial);
			}
			if (jumpFloodPassMaterial != null)
			{
				DestroyImmediate(jumpFloodPassMaterial);
			}
		}
    }
}
