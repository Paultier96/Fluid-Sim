using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

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
		[Tooltip("Replaces the colour ramp with a visualisation of the CSF (surface tension) gradient magnitude per particle.")]
		public bool debugMode = false;
		[Tooltip("CSF gradient magnitude mapped to the top of the debug colour range. Increase if the visualisation is saturating.")]
		public float debugGradientMax = 1.0f;
		
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
		public float jFAAWidth;
		
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
		Camera boundCamera;
		bool needsUpdate;

		void Awake()
		{
			EnsureMaterials();
			needsUpdate = true;
			targetCamera = Camera.main;
		}

		void LateUpdate()
		{
			EnsureMaterials();

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

			activeMaterial.SetBuffer("Positions2D", sim.positionBuffer);
			activeMaterial.SetBuffer("Velocities", sim.velocityBuffer);
			activeMaterial.SetBuffer("DensityData", sim.densityBuffer);
			activeMaterial.SetBuffer("Phases", sim.phaseBuffer);
			activeMaterial.SetBuffer("Temperatures", sim.temperatureBuffer);


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
			if (directParticleShader != null && (directParticleMaterial == null || directParticleMaterial.shader != directParticleShader))
			{
				directParticleMaterial = new Material(directParticleShader);
				needsUpdate = true;
			}

			if (metaballShader != null && (metaballMaterial == null || metaballMaterial.shader != metaballShader))
			{
				metaballMaterial = new Material(metaballShader);
				needsUpdate = true;
			}

			if (jumpFloodDisplayShader != null && (jumpFloodDisplayMaterial == null || jumpFloodDisplayMaterial.shader != jumpFloodDisplayShader))
			{
				jumpFloodDisplayMaterial = new Material(jumpFloodDisplayShader);
				needsUpdate = true;
			}

			if (jumpFloodSeedShader != null && (jumpFloodSeedMaterial == null || jumpFloodSeedMaterial.shader != jumpFloodSeedShader))
			{
				jumpFloodSeedMaterial = new Material(jumpFloodSeedShader);
				needsUpdate = true;
			}

			if (jumpFloodPassShader != null && (jumpFloodPassMaterial == null || jumpFloodPassMaterial.shader != jumpFloodPassShader))
			{
				jumpFloodPassMaterial = new Material(jumpFloodPassShader);
				needsUpdate = true;
			}
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
				jumpFloodCompute.SetFloat("_TempMax", sim.heatSourceTemperature);
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
				// clear jfaSeedA to sentinel (-1,-1,0,-1)
				RenderTexture prevRT = RenderTexture.active;
				Graphics.SetRenderTarget(jfaSeedA);
				GL.Clear(false, true, new Color(-1f, -1f, 0f, -1f));
				Graphics.SetRenderTarget(prevRT);

				// seed: raster pass using JumpFloodSeed shader. If no seed shader assigned, the texture stays cleared (no seeds).
				if (jumpFloodSeedMaterial != null)
				{
					jumpFloodSeedMaterial.SetBuffer("Positions2D", sim.positionBuffer);
					jumpFloodSeedMaterial.SetBuffer("Temperatures", sim.temperatureBuffer);
					jumpFloodSeedMaterial.SetBuffer("Phases", sim.phaseBuffer);

					jumpFloodSeedMaterial.SetInt("_ParticleCount", sim.positionBuffer.count);
					jumpFloodSeedMaterial.SetMatrix("_VP", VP);
					jumpFloodSeedMaterial.SetFloat("_TempMin", sim.ambientTemperature);
					jumpFloodSeedMaterial.SetFloat("_TempMax", sim.heatSourceTemperature);

					// render into jfaSeedA
					RenderTexture prev = RenderTexture.active;
					Graphics.SetRenderTarget(jfaSeedA);
					GL.Viewport(new Rect(0, 0, jfaSeedA.width, jfaSeedA.height));
					jumpFloodSeedMaterial.SetPass(0);
					Graphics.DrawProceduralNow(MeshTopology.Points, sim.positionBuffer.count);
					Graphics.SetRenderTarget(prev);
				}
			}
			// jump flood passes implemented as raster blits using jumpFloodPassMaterial
			RenderTexture src = jfaSeedA;
			RenderTexture dst = jfaSeedB;
			int maxDim = Mathf.Max(width, height);
			int step = 1;
			while (step < maxDim) step <<= 1;
			// start at largest power of two
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
					// Graphics.Blit with identical source and destination RT is undefined on some platforms/drivers.
	                if (src == dst)
	                {
	                    // ensure persistent temp RT exists and matches size
	                    if (jfaTemp == null || jfaTemp.width != width || jfaTemp.height != height)
	                    {
	                        ComputeHelper.CreateRenderTexture(ref jfaTemp, width, height, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, "JFA Temp");
	                    }
	                    RenderTexture prev = RenderTexture.active;
	                    Graphics.SetRenderTarget(jfaTemp);
	                    GL.Viewport(new Rect(0, 0, jfaTemp.width, jfaTemp.height));
	                    jumpFloodPassMaterial.SetPass(0);
	                    Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
	                    Graphics.SetRenderTarget(prev);
	                    Graphics.CopyTexture(jfaTemp, dst);
	                }
	                else
	                {
	                    RenderTexture prev = RenderTexture.active;
	                    Graphics.SetRenderTarget(dst);
	                    GL.Viewport(new Rect(0, 0, dst.width, dst.height));
	                    jumpFloodPassMaterial.SetPass(0);
	                    Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
	                    Graphics.SetRenderTarget(prev);
	                }
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
			EnsureCommandBuffer(targetCamera);
			mat.SetTexture("_SeedTex", jfaResult != null ? jfaResult : jfaSeedA);
			mat.SetTexture("ColourMap", gradientTexture);
			mat.SetTexture("ColourMap2", gradientTexture2);
			mat.SetFloat("tempMin", sim.ambientTemperature);
			mat.SetFloat("tempMax", sim.heatSourceTemperature);
			mat.SetFloat("_EdgeWidth", jFedgeWidth);
			mat.SetFloat("_AAWidth", jFAAWidth);

			metaballCommandBuffer.Clear();
			metaballCommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.black);
			metaballCommandBuffer.Blit(jfaResult != null ? jfaResult : jfaSeedA, BuiltinRenderTextureType.CameraTarget, mat);
		}

		Material GetActiveParticleMaterial()
		{
			return renderMode == RenderMode.Metaballs ? metaballMaterial : directParticleMaterial;
		}

		void ApplySharedParticleSettings(Material targetMaterial)
		{
			targetMaterial.SetFloat("scale", scale);
			targetMaterial.SetFloat("velocityMax", velocityDisplayMax);
			targetMaterial.SetFloat("tempMin", sim.ambientTemperature);
			targetMaterial.SetFloat("tempMax", sim.heatSourceTemperature);
			targetMaterial.SetBuffer("CSFGradients", sim.csfGradientBuffer);
			targetMaterial.SetFloat("debugGradientMax", debugGradientMax);
			targetMaterial.SetInt("debugMode", debugMode ? 1 : 0);
			targetMaterial.SetFloat("metaballSharpness", metaballSharpness);
			targetMaterial.SetFloat("metaballIntensity", metaballIntensity);
		}

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
			if (compositeMaterial == null || compositeMaterial.shader != compositeShader)
			{
				compositeMaterial = new Material(compositeShader);
			}

			if (blurMaterial == null || blurMaterial.shader != blurShader)
			{
				blurMaterial = new Material(blurShader);
			}

			EnsureCommandBuffer(targetCamera);
			EnsureRenderTextures(targetCamera);

			compositeMaterial.SetFloat("densityThreshold", densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", phaseBlendWidth);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("ColourMap", gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", gradientTexture2);
			compositeMaterial.SetInt("debugMode", debugMode ? 1 : 0);

			blurMaterial.SetFloat("blurRadius", blurRadius);

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

		void EnsureCommandBuffer(Camera cam)
		{
			if (metaballCommandBuffer == null)
			{
				metaballCommandBuffer = new CommandBuffer();
				metaballCommandBuffer.name = "Sim2D Metaball Render";
			}

			if (boundCamera != cam)
			{
				RemoveCommandBuffer();
				cam.AddCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
				boundCamera = cam;
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
			if (boundCamera != null && metaballCommandBuffer != null)
			{
				boundCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
			}
			boundCamera = null;
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

		void OnValidate()
		{
			needsUpdate = true;
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
