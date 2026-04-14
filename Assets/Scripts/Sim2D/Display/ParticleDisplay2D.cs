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
			Metaballs
		}

		public FluidSim2D sim;
		public Mesh mesh;
		public RenderMode renderMode = RenderMode.DirectParticles;
		public Shader directParticleShader;
		public Shader metaballShader;
		public float scale;
		private Gradient[] colourMap;
		public int gradientResolution;
		public float velocityDisplayMax;

		[Header("Metaball Rendering")]
		public bool useRenderTextureMetaballs = true;
		public Camera targetCamera;
		public Shader compositeShader;
		public Shader blurShader;
		[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
		[Min(0)] public float blurRadius = 6;
		[Min(0)] public float densityThreshold = 0.18f;
		[Min(0.0001f)] public float edgeSoftness = 0.06f;
		[Min(0.0001f)] public float phaseBlendWidth = 0.02f;
		[Min(0)] public float opacity = 1.0f;
		[Min(0.01f)] public float metaballSharpness = 3.5f;
		[Min(0)] public float metaballIntensity = 1.0f;
		
		[Header("Debug")]
		public bool debugMode = false;
		public float debugGradientMax = 1.0f;

		Material directParticleMaterial;
		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		ComputeBuffer argsBuffer;
		Bounds bounds;
		Texture2D gradientTexture;
		Texture2D gradientTexture2;
		RenderTexture phase0AccumulationTexture;
		RenderTexture phase1AccumulationTexture;
		RenderTexture phase0BlurTexture;
		RenderTexture phase1BlurTexture;
		CommandBuffer metaballCommandBuffer;
		Camera boundCamera;
		bool needsUpdate;

		void Awake()
		{
			EnsureMaterials();
			needsUpdate = true;
		}

		void LateUpdate()
		{
			EnsureMaterials();
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

			if (metaballMaterial != null)
			{
				metaballMaterial.SetTexture("ColourMap", gradientTexture);
				metaballMaterial.SetTexture("ColourMap2", gradientTexture2);
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
			Camera cam = targetCamera != null ? targetCamera : Camera.main;
			if (cam == null)
			{
				return;
			}

			if (compositeMaterial == null || compositeMaterial.shader != compositeShader)
			{
				compositeMaterial = new Material(compositeShader);
			}

			if (blurMaterial == null || blurMaterial.shader != blurShader)
			{
				blurMaterial = new Material(blurShader);
			}

			EnsureCommandBuffer(cam);
			EnsureRenderTextures(cam);

			compositeMaterial.SetFloat("densityThreshold", densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", phaseBlendWidth);
			compositeMaterial.SetFloat("opacity", opacity);
			compositeMaterial.SetTexture("Phase0Tex", phase0AccumulationTexture);
			compositeMaterial.SetTexture("Phase1Tex", phase1AccumulationTexture);

			blurMaterial.SetFloat("blurRadius", blurRadius);

			metaballCommandBuffer.Clear();
			metaballCommandBuffer.SetGlobalInt("renderPhase", 0);
			metaballCommandBuffer.SetRenderTarget(phase0AccumulationTexture);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			metaballCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, metaballMaterial, 0, argsBuffer);

			metaballCommandBuffer.SetGlobalInt("renderPhase", 1);
			metaballCommandBuffer.SetRenderTarget(phase1AccumulationTexture);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			metaballCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, metaballMaterial, 0, argsBuffer);

			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			metaballCommandBuffer.Blit(phase0AccumulationTexture, phase0BlurTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			metaballCommandBuffer.Blit(phase0BlurTexture, phase0AccumulationTexture, blurMaterial);

			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			metaballCommandBuffer.Blit(phase1AccumulationTexture, phase1BlurTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			metaballCommandBuffer.Blit(phase1BlurTexture, phase1AccumulationTexture, blurMaterial);

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

			ComputeHelper.CreateRenderTexture(ref phase0AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase0 Accumulation");
			ComputeHelper.CreateRenderTexture(ref phase1AccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase1 Accumulation");
			ComputeHelper.CreateRenderTexture(ref phase0BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase0 Blur");
			ComputeHelper.CreateRenderTexture(ref phase1BlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Phase1 Blur");
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
			ComputeHelper.Release(phase0AccumulationTexture, phase1AccumulationTexture, phase0BlurTexture, phase1BlurTexture);
			RemoveCommandBuffer();
			if (metaballCommandBuffer != null)
			{
				metaballCommandBuffer.Release();
			}
		}
    }
}
