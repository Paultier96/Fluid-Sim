using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Serialization;
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
			Viscosity = 3,
			Density = 4,
			Temperature = 5,
			BlobIds = 6,
		}

		public enum VectorFieldSource
		{
			None = 0,
			SurfaceTensionForce = 1,
			RepulsionForce = 2,
			Velocity = 3,
			CurvatureNormal = 4,
			Convection = 5,
			CarrierWedge = 6,
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

		[Header("Particle Lighting")]
		[Tooltip("Direction toward the light used to shade particles in normal rendering mode.")]
		public Vector3 particleLightDirection = new Vector3(-0.35f, 0.55f, 0.75f);
		[Tooltip("Colour of the directional light used to shade particles in normal rendering mode.")]
		[ColorUsage(false, true)] public Color particleLightColor = Color.white;
		[Tooltip("Unlit colour multiplier. Increase if shadowed particles are too dark.")]
		[Range(0f, 1f)] public float particleAmbientLight = 0.65f;
		[Tooltip("Directional light strength applied from each particle's reconstructed normal.")]
		[Min(0f)] public float particleDirectionalLightIntensity = 0.45f;
		[Tooltip("Multiplier applied to reconstructed normal XY before rebuilding Z. Higher values make blurred normals look steeper.")]
		[Min(0f)] public float particleNormalStrength = 1f;
		[Tooltip("Exponent used to increase normal strength with effective blur radius. 0 disables automatic compensation, 1 is linear.")]
		[Min(0f)] public float particleNormalBlurCompensation = 0.5f;
		[Tooltip("Colour of the specular highlight used in normal rendering mode.")]
		[ColorUsage(false, true)] public Color particleSpecularColor = Color.white;
		[Tooltip("Specular highlight strength applied from each particle's reconstructed normal.")]
		[Min(0f)] public float particleSpecularIntensity = 0.25f;
		[Tooltip("Specular exponent. Higher values make highlights smaller and sharper.")]
		[Min(1f)] public float particleSpecularPower = 24f;
		[Tooltip("Colour added at grazing view angles to fake transparent liquid edges.")]
		[ColorUsage(false, true)] public Color particleFresnelColor = new Color(0.75f, 0.9f, 1f, 1f);
		[Tooltip("Strength of the Fresnel edge glow.")]
		[Min(0f)] public float particleFresnelIntensity = 0.25f;
		[Tooltip("Fresnel exponent. Higher values concentrate the glow closer to grazing angles.")]
		[Min(0.1f)] public float particleFresnelPower = 3f;
		[Tooltip("Screen/normal-space direction for the one-sided liquid-glass rim glow.")]
		public Vector2 particleGlowDirection = new Vector2(-0.75f, -0.45f);
		[Tooltip("Colour of the one-sided liquid-glass rim glow.")]
		[ColorUsage(false, true)] public Color particleGlowColor = Color.white;
		[Tooltip("Strength of the one-sided liquid-glass rim glow.")]
		[Min(0f)] public float particleGlowIntensity = 0.35f;
		[Tooltip("Directional glow exponent. Higher values make the glow narrower along its chosen side.")]
		[Min(0.1f)] public float particleGlowPower = 1.5f;
		[Tooltip("Strength of the fake transmission/backlight term.")]
		[Min(0f)] public float particleTransmissionIntensity = 0.2f;
		[Tooltip("Transmission exponent. Higher values make transmission more directional.")]
		[Min(0.1f)] public float particleTransmissionPower = 2f;
		[Tooltip("Darkens thin/edge regions to fake inner shadow and liquid thickness.")]
		[Range(0f, 1f)] public float particleEdgeDarkening = 0.2f;
		[Tooltip("Edge darkening exponent. Higher values keep the darkening tighter to the edge.")]
		[Min(0.1f)] public float particleEdgeDarkeningPower = 2f;

		[Header("Metaball Rendering")]
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
		[Tooltip("Screen-space width in pixels for anti-aliased blending between fluid phases.")]
		[Min(0.0001f)] public float phaseBlendWidth = 1f;
		[Tooltip("Render-only phase boundary bias. 0 is neutral, positive values make phase 0 visually expand, negative values make phase 1 expand.")]
		[Range(-0.99f, 0.99f)] public float phase0RenderBias = 0f;
		[Tooltip("How strongly phase boundary bias redistributes normal strength. The visually expanded phase is weakened while the compressed phase is strengthened.")]
		[Range(0f, 1f)] public float phaseBiasNormalStrength = 0.5f;
		[Tooltip("Steepness of each particle's density kernel (exp(-r² × sharpness)). Higher values make particles contribute a tighter, more localised density spike.")]
		[Min(0.01f)] public float metaballSharpness = 3.5f;
		[Tooltip("Uniform scale applied to each particle's density contribution. Increase if particles are too sparse to merge.")]
		[Min(0)] public float metaballIntensity = 1.0f;
		[Tooltip("Render-only boost applied to convex high-curvature particles so small blobs survive larger blur radii. Set to 0 to disable.")]
		[Min(0f)] public float convexCurvatureMetaballBoost = 0f;
		[Tooltip("Curvature value that maps to full convex metaball boost.")]
		[Min(0.0001f)] public float convexCurvatureBoostMax = 5f;
		[Tooltip("Configured blur radius where convex curvature boost starts fading in.")]
		[Min(0f)] public float convexCurvatureBoostStartBlurRadius = 6f;
		[Tooltip("Configured blur radius range over which convex curvature boost reaches full strength.")]
		[Min(0.0001f)] public float convexCurvatureBoostBlurRange = 12f;
		[Tooltip("Metaball-only UV offset strength for refracting the blurred colour data outward from the normal. Alpha and phase remain unwarped.")]
		public float metaballRefractionStrength = 0.01f;
		[Tooltip("Density distance over which refraction fades in from the visible edge. Higher values push refraction farther inward.")]
		[Min(0.0001f)] public float metaballRefractionEdgeFade = 0.05f;

		[Header("Debug")]
		[Tooltip("Selects what to show in debug mode. Press 0-6 to switch modes at runtime.")]
		public DebugVisualization debugMode = DebugVisualization.None;
		[Tooltip("Maximum absolute value mapped in gradient debug visualisation.")]
		public float debugGradientMax = 1.0f;
		[Tooltip("Lower density value used for density debug colour mapping.")]
		[Min(0f)] public float debugDensityMin = 0f;
		[Tooltip("Upper density value used for density debug colour mapping.")]
		[Min(0.0001f)] public float debugDensityMax = 500f;
		[Tooltip("Colour gradient used by viscosity, density, and temperature debug views.")]
		public Gradient heatMap;
		[FormerlySerializedAs("debugSignedHeatMapGradient")] [Tooltip("Signed colour gradient used by curvature debug views. The centre represents zero; left is negative, right is positive.")]
		public Gradient signedHeatMap;
		[Tooltip("Screen-space dithering strength used by the metaball composite shader to reduce colour banding.")]
		[Min(0f)] public float ditherStrength = 1.0f / 255.0f;

		[Header("Vector Field Debug")]
		[Tooltip("Shader used to draw force vectors as instanced arrows.")]
		public Shader vectorFieldShader;
		[Tooltip("Vector data shown by the arrow overlay.")]
		public VectorFieldSource vectorFieldSource = VectorFieldSource.SurfaceTensionForce;
		[Tooltip("World-space length of an arrow at vectorMaxMagnitude.")]
		[Min(0f)] public float vectorScale = 0.25f;
		[Tooltip("Vector magnitude that maps to full arrow length and colour intensity.")]
		[Min(0.0001f)] public float vectorMaxMagnitude = 1.0f;
		[Tooltip("Use a logarithmic remap for arrow length so small vectors remain visible while large vectors stay bounded.")]
		public bool vectorUseLogScale = false;
		[Tooltip("Log remap strength. Higher values boost small vectors more strongly.")]
		[Min(1f)] public float vectorLogScaleStrength = 10.0f;
		[Tooltip("World-space arrow width.")]
		[Min(0f)] public float vectorWidth = 0.035f;
		
		//public float edgeWidth = 0.03f;


		Material directParticleMaterial;
		Material metaballMaterial;
		Material compositeMaterial;
		Material blurMaterial;
		Material vectorFieldMaterial;
		Mesh vectorArrowMesh;
		
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
		ComputeBuffer vectorArgsBuffer;
		Bounds bounds;
		Texture2D gradientTexture;
		Texture2D gradientTexture2;
		Texture2D debugHeatMapTexture;
		Texture2D debugSignedHeatMapTexture;
		RenderTexture combinedAccumulationTexture;
		RenderTexture combinedBlurTexture;
		RenderTexture normalAccumulationTexture;
		RenderTexture normalBlurTexture;
		CommandBuffer metaballCommandBuffer;
		bool commandBufferAttached;
		bool needsUpdate;
		const float BlurReferenceOrthoSize = 15f;
		const string MetaballCommandBufferName = "Sim2D Metaball Render";
		const string SceneViewDirectCommandBufferName = "Sim2D Scene View Direct Particles";
		private Camera mainCamera;

		void Awake()
		{
			EnsureMaterials();
			needsUpdate = true;
			mainCamera = Camera.main;
		}

#if UNITY_EDITOR
		void OnEnable()
		{
			Camera.onPreCull += DrawSceneViewDirect;
		}
#endif

		void LateUpdate()
		{
			UpdateDebugModeFromKeyboard();
			EnsureMaterials();
			UpdateSettings();

			if (renderMode == RenderMode.JumpFlood)
			{
				UpdateJumpFloodRender(mainCamera);
				UpdateJumpFloodCommandBuffer();
			}
			else if (renderMode == RenderMode.Metaballs && compositeShader != null && blurShader != null)
			{
				UpdateMetaballRender(mainCamera);
			}
			else
			{
				RemoveCommandBuffer();
				DrawDirectParticles(mainCamera);
				DrawVectorField(mainCamera);
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
				debugMode = DebugVisualization.Viscosity;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
			{
				debugMode = DebugVisualization.Density;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
			{
				debugMode = DebugVisualization.Temperature;
			}
			else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
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

            ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);
            ComputeHelper.CreateArgsBuffer(ref vectorArgsBuffer, vectorArrowMesh, sim.positionBuffer.count);
			bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

			if (needsUpdate)
			{
				needsUpdate = false;
				ApplyGradientTextures();
			}
			
			ApplyParticleMaterialSettings(directParticleMaterial);
			ApplyParticleMaterialSettings(metaballMaterial);
			ApplyParticleMaterialSettings(vectorFieldMaterial);
			ApplyVectorFieldSettings();
		}

		void ApplyParticleMaterialSettings(Material targetMaterial)
		{
			if (targetMaterial == null)
			{
				return;
			}

			BindSimulationBuffers(targetMaterial);
			ApplySharedParticleSettings(targetMaterial);
		}

		void EnsureMaterials()
		{
			if (vectorFieldShader == null)
			{
				vectorFieldShader = Shader.Find("Instanced/Particle2DVectorField");
			}

			EnsureMaterial(ref directParticleMaterial, directParticleShader);
			EnsureMaterial(ref metaballMaterial, metaballShader);
			EnsureMaterial(ref vectorFieldMaterial, vectorFieldShader);
			EnsureMaterial(ref jumpFloodDisplayMaterial, jumpFloodDisplayShader);
			EnsureMaterial(ref jumpFloodSeedMaterial, jumpFloodSeedShader);
			EnsureMaterial(ref jumpFloodPassMaterial, jumpFloodPassShader);
			EnsureVectorArrowMesh();
		}

		void UpdateJumpFloodRender(Camera cam)
		{
			RemoveCommandBuffer(); // avoid metaball cb

			EnsureJumpFloodRenderTextures(cam);

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
			
			Matrix4x4 VP = cam.projectionMatrix * cam.worldToCameraMatrix;

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
			EnsureCommandBuffer();
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
			AppendVectorFieldDraw(metaballCommandBuffer);
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
			targetMaterial.SetBuffer("Curvatures", sim.debugVectorSignBuffer);
		}

		void ApplySharedParticleSettings(Material targetMaterial)
		{
			targetMaterial.SetFloat("scale", scale);
			targetMaterial.SetFloat("velocityMax", velocityDisplayMax);
			targetMaterial.SetFloat("tempMin", sim.ambientTemperature);
			targetMaterial.SetFloat("tempMax", sim.HeatSourceTemperature);
			targetMaterial.SetBuffer("DebugData", sim.debugDataBuffer);
			targetMaterial.SetFloat("debugGradientMax", debugGradientMax);
			targetMaterial.SetFloat("debugCurvatureMax", sim.MaxDebugCurvature);
			targetMaterial.SetFloat("debugViscosityMax", sim.MaxDebugViscosity);
			targetMaterial.SetFloat("debugDensityMin", DebugDensityMin);
			targetMaterial.SetFloat("debugDensityMax", DebugDensityMax);
			targetMaterial.SetInt("debugMode", (int)ParticleShaderDebugMode);
			targetMaterial.SetVector("particleLightDirection", particleLightDirection);
			targetMaterial.SetColor("particleLightColor", particleLightColor);
			targetMaterial.SetFloat("particleAmbientLight", particleAmbientLight);
			targetMaterial.SetFloat("particleDirectionalLightIntensity", particleDirectionalLightIntensity);
			targetMaterial.SetColor("particleSpecularColor", particleSpecularColor);
			targetMaterial.SetFloat("particleSpecularIntensity", particleSpecularIntensity);
			targetMaterial.SetFloat("particleSpecularPower", particleSpecularPower);
			targetMaterial.SetColor("particleFresnelColor", particleFresnelColor);
			targetMaterial.SetFloat("particleFresnelIntensity", particleFresnelIntensity);
			targetMaterial.SetFloat("particleFresnelPower", particleFresnelPower);
			targetMaterial.SetVector("particleGlowDirection", new Vector4(particleGlowDirection.x, particleGlowDirection.y, 0f, 0f));
			targetMaterial.SetColor("particleGlowColor", particleGlowColor);
			targetMaterial.SetFloat("particleGlowIntensity", particleGlowIntensity);
			targetMaterial.SetFloat("particleGlowPower", particleGlowPower);
			targetMaterial.SetFloat("particleTransmissionIntensity", particleTransmissionIntensity);
			targetMaterial.SetFloat("particleTransmissionPower", particleTransmissionPower);
			targetMaterial.SetFloat("particleEdgeDarkening", particleEdgeDarkening);
			targetMaterial.SetFloat("particleEdgeDarkeningPower", particleEdgeDarkeningPower);
			targetMaterial.SetFloat("metaballSharpness", metaballSharpness);
			targetMaterial.SetFloat("metaballIntensity", metaballIntensity);
			targetMaterial.SetFloat("convexCurvatureMetaballBoost", convexCurvatureMetaballBoost);
			targetMaterial.SetFloat("convexCurvatureBoostMax", convexCurvatureBoostMax);
			targetMaterial.SetFloat("convexCurvatureBoostStartBlurRadius", convexCurvatureBoostStartBlurRadius);
			targetMaterial.SetFloat("convexCurvatureBoostBlurRange", convexCurvatureBoostBlurRange);
		}

		void ApplyVectorFieldSettings()
		{
			if (vectorFieldMaterial == null)
			{
				return;
			}

			vectorFieldMaterial.SetFloat("vectorScale", vectorScale);
			vectorFieldMaterial.SetFloat("vectorMaxMagnitude", EffectiveVectorMaxMagnitude);
			vectorFieldMaterial.SetInt("vectorUseLogScale", vectorUseLogScale ? 1 : 0);
			vectorFieldMaterial.SetFloat("vectorLogScaleStrength", vectorLogScaleStrength);
			vectorFieldMaterial.SetFloat("vectorWidth", vectorWidth);
			vectorFieldMaterial.SetInt("vectorUseSignedColor", vectorFieldSource == VectorFieldSource.CurvatureNormal ? 1 : 0);
			ComputeBuffer vectorFieldBuffer = GetVectorFieldBuffer();
			if (vectorFieldBuffer != null)
			{
				vectorFieldMaterial.SetBuffer("DebugVectorData", vectorFieldBuffer);
			}
			if (sim != null && sim.debugVectorSignBuffer != null)
			{
				vectorFieldMaterial.SetBuffer("DebugVectorSign", sim.debugVectorSignBuffer);
			}
		}

		float DebugDensityMin => Mathf.Min(debugDensityMin, debugDensityMax - 0.0001f);
		float DebugDensityMax => Mathf.Max(debugDensityMax, debugDensityMin + 0.0001f);
		float EffectiveVectorMaxMagnitude
		{
			get
			{
				if (sim != null)
				{
					if (vectorFieldSource == VectorFieldSource.CurvatureNormal)
					{
						return sim.MaxDebugCurvature;
					}

					if (vectorFieldSource == VectorFieldSource.SurfaceTensionForce)
					{
						return sim.MaxDebugSurfaceTensionForce;
					}

					if (vectorFieldSource == VectorFieldSource.Convection)
					{
						return sim.MaxDebugConvection;
					}

					if (vectorFieldSource == VectorFieldSource.CarrierWedge)
					{
						if (sim.carrierWedgeMaxAcceleration > 0)
						{
							return sim.carrierWedgeMaxAcceleration;
						}

						return Mathf.Max(0.0001f, Mathf.Abs(sim.carrierWedgeStrength));
					}
				}

				return Mathf.Max(0.0001f, vectorMaxMagnitude);
			}
		}
		DebugVisualization ParticleShaderDebugMode => debugMode;
		public int ComputeVectorFieldMode
		{
			get
			{
				if (vectorFieldSource == VectorFieldSource.SurfaceTensionForce)
				{
					return 1;
				}

				if (vectorFieldSource == VectorFieldSource.RepulsionForce)
				{
					return 2;
				}

				if (vectorFieldSource == VectorFieldSource.CurvatureNormal)
				{
					return 3;
				}

				if (vectorFieldSource == VectorFieldSource.Convection)
				{
					return 4;
				}

				if (vectorFieldSource == VectorFieldSource.CarrierWedge)
				{
					return 5;
				}

				return 0;
			}
		}

		ComputeBuffer GetVectorFieldBuffer()
		{
			if (sim == null)
			{
				return null;
			}

			return vectorFieldSource == VectorFieldSource.Velocity
				? sim.velocityBuffer
				: sim.debugVectorDataBuffer;
		}

		void ApplyGradientTextures()
		{
			if (directParticleMaterial != null)
			{
				directParticleMaterial.SetTexture("ColourMap", gradientTexture);
				directParticleMaterial.SetTexture("ColourMap2", gradientTexture2);
				directParticleMaterial.SetTexture("DebugHeatMap", debugHeatMapTexture);
				directParticleMaterial.SetTexture("DebugSignedHeatMap", debugSignedHeatMapTexture);
			}

			if (compositeMaterial != null)
			{
				compositeMaterial.SetTexture("DebugHeatMap", debugHeatMapTexture);
				compositeMaterial.SetTexture("DebugSignedHeatMap", debugSignedHeatMapTexture);
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
			TextureFromGradient(ref gradientTexture, gradientResolution, primary, FilterMode.Bilinear, true, TextureFormat.RGBAHalf, true);
			TextureFromGradient(ref gradientTexture2, gradientResolution, secondary, FilterMode.Bilinear, true, TextureFormat.RGBAHalf, true);
			TextureFromGradient(ref debugHeatMapTexture, gradientResolution, heatMap, FilterMode.Bilinear, true, TextureFormat.RGBAHalf);
			TextureFromGradient(ref debugSignedHeatMapTexture, GetSignedGradientResolution(), signedHeatMap, FilterMode.Bilinear, true, TextureFormat.RGBAHalf);
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

		int GetSignedGradientResolution()
		{
			int width = Mathf.Max(3, gradientResolution);
			return width % 2 == 0 ? width + 1 : width;
		}

		void UpdateMetaballRender(Camera cam)
		{
			EnsurePostProcessMaterials();

			EnsureCommandBuffer();
			EnsureRenderTextures(cam);

			compositeMaterial.SetFloat("densityThreshold", densityThreshold);
			compositeMaterial.SetFloat("edgeSoftness", edgeSoftness);
			compositeMaterial.SetFloat("phaseBlendWidth", phaseBlendWidth);
			compositeMaterial.SetFloat("phase0RenderBias", phase0RenderBias);
			compositeMaterial.SetFloat("phaseBiasNormalStrength", phaseBiasNormalStrength);
			compositeMaterial.SetTexture("CombinedTex", combinedAccumulationTexture);
			compositeMaterial.SetTexture("NormalTex", normalAccumulationTexture);
			compositeMaterial.SetTexture("ColourMap", gradientTexture);
			compositeMaterial.SetTexture("ColourMap2", gradientTexture2);
			compositeMaterial.SetTexture("DebugHeatMap", debugHeatMapTexture);
			compositeMaterial.SetTexture("DebugSignedHeatMap", debugSignedHeatMapTexture);
			float effectiveBlurRadius = GetEffectiveBlurRadius(cam);
			float effectiveRefractionStrength = metaballRefractionStrength * GetZoomScale(cam);
			metaballMaterial.SetFloat("metaballBlurRadius", blurRadius);
			compositeMaterial.SetFloat("metaballRefractionStrength", effectiveRefractionStrength);
			compositeMaterial.SetFloat("metaballRefractionEdgeFade", metaballRefractionEdgeFade);
			float effectiveNormalStrength = GetEffectiveNormalStrength(blurRadius);
			compositeMaterial.SetInt("debugMode", (int)ParticleShaderDebugMode);
			compositeMaterial.SetFloat("ditherStrength", ditherStrength);
			compositeMaterial.SetVector("particleLightDirection", particleLightDirection);
			compositeMaterial.SetColor("particleLightColor", particleLightColor);
			compositeMaterial.SetFloat("particleAmbientLight", particleAmbientLight);
			compositeMaterial.SetFloat("particleDirectionalLightIntensity", particleDirectionalLightIntensity);
			compositeMaterial.SetFloat("particleNormalStrength", effectiveNormalStrength);
			compositeMaterial.SetColor("particleSpecularColor", particleSpecularColor);
			compositeMaterial.SetFloat("particleSpecularIntensity", particleSpecularIntensity);
			compositeMaterial.SetFloat("particleSpecularPower", particleSpecularPower);
			compositeMaterial.SetColor("particleFresnelColor", particleFresnelColor);
			compositeMaterial.SetFloat("particleFresnelIntensity", particleFresnelIntensity);
			compositeMaterial.SetFloat("particleFresnelPower", particleFresnelPower);
			compositeMaterial.SetVector("particleGlowDirection", new Vector4(particleGlowDirection.x, particleGlowDirection.y, 0f, 0f));
			compositeMaterial.SetColor("particleGlowColor", particleGlowColor);
			compositeMaterial.SetFloat("particleGlowIntensity", particleGlowIntensity);
			compositeMaterial.SetFloat("particleGlowPower", particleGlowPower);
			compositeMaterial.SetFloat("particleTransmissionIntensity", particleTransmissionIntensity);
			compositeMaterial.SetFloat("particleTransmissionPower", particleTransmissionPower);
			compositeMaterial.SetFloat("particleEdgeDarkening", particleEdgeDarkening);
			compositeMaterial.SetFloat("particleEdgeDarkeningPower", particleEdgeDarkeningPower);

			blurMaterial.SetFloat("blurRadius", effectiveBlurRadius);

			metaballCommandBuffer.Clear();
			metaballCommandBuffer.SetRenderTarget(combinedAccumulationTexture);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			metaballCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, metaballMaterial, 0, argsBuffer);
			metaballCommandBuffer.SetRenderTarget(normalAccumulationTexture);
			metaballCommandBuffer.ClearRenderTarget(false, true, Color.clear);
			metaballCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, metaballMaterial, 1, argsBuffer);

			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			metaballCommandBuffer.Blit(combinedAccumulationTexture, combinedBlurTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			metaballCommandBuffer.Blit(combinedBlurTexture, combinedAccumulationTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(1, 0));
			metaballCommandBuffer.Blit(normalAccumulationTexture, normalBlurTexture, blurMaterial);
			metaballCommandBuffer.SetGlobalVector("blurDirection", new Vector2(0, 1));
			metaballCommandBuffer.Blit(normalBlurTexture, normalAccumulationTexture, blurMaterial);

			metaballCommandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, compositeMaterial);
			AppendVectorFieldDraw(metaballCommandBuffer);
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

		void EnsureCommandBuffer()
		{
			Camera mainCamera = Camera.main;

			if (metaballCommandBuffer == null)
			{
				metaballCommandBuffer = new CommandBuffer();
				metaballCommandBuffer.name = MetaballCommandBufferName;
			}

#if UNITY_EDITOR
			RemoveCommandBufferFromCamera(GetSceneViewCamera());
#endif

			if (!commandBufferAttached)
			{
				mainCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
				RemoveCommandBuffersByName(mainCamera, CameraEvent.AfterEverything, MetaballCommandBufferName);
				RemoveCommandBuffersByName(mainCamera, CameraEvent.BeforeImageEffects, MetaballCommandBufferName);
				mainCamera.AddCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
				commandBufferAttached = true;
			}
		}

		void DrawDirectParticles(Camera cam)
		{
			if (directParticleMaterial == null || argsBuffer == null)
			{
				return;
			}

			Graphics.DrawMeshInstancedIndirect(
				mesh,
				0,
				directParticleMaterial,
				bounds,
				argsBuffer,
				0,
				null,
				ShadowCastingMode.Off,
				false,
				gameObject.layer,
				cam
			);
		}

		bool ShouldDrawVectorField()
		{
			return vectorFieldSource != VectorFieldSource.None
			       && GetVectorFieldBuffer() != null
			       && vectorFieldMaterial != null
			       && vectorArrowMesh != null
			       && vectorArgsBuffer != null;
		}

		void DrawVectorField(Camera cam)
		{
			if (!ShouldDrawVectorField())
			{
				return;
			}

			Graphics.DrawMeshInstancedIndirect(
				vectorArrowMesh,
				0,
				vectorFieldMaterial,
				bounds,
				vectorArgsBuffer,
				0,
				null,
				ShadowCastingMode.Off,
				false,
				gameObject.layer,
				cam
			);
		}

		void AppendVectorFieldDraw(CommandBuffer commandBuffer)
		{
			if (!ShouldDrawVectorField())
			{
				return;
			}

			commandBuffer.DrawMeshInstancedIndirect(vectorArrowMesh, 0, vectorFieldMaterial, 0, vectorArgsBuffer);
		}

		void EnsureVectorArrowMesh()
		{
			if (vectorArrowMesh != null)
			{
				return;
			}

			vectorArrowMesh = new Mesh();
			vectorArrowMesh.name = "Sim2D Vector Arrow";
			vectorArrowMesh.vertices = new[]
			{
				new Vector3(0f, -0.5f, 0f),
				new Vector3(0.62f, -0.5f, 0f),
				new Vector3(0.62f, -1f, 0f),
				new Vector3(1f, 0f, 0f),
				new Vector3(0.62f, 1f, 0f),
				new Vector3(0.62f, 0.5f, 0f),
				new Vector3(0f, 0.5f, 0f),
			};
			vectorArrowMesh.triangles = new[]
			{
				0, 1, 6,
				1, 5, 6,
				1, 2, 3,
				1, 3, 5,
				3, 4, 5,
			};
			vectorArrowMesh.RecalculateBounds();
		}

#if UNITY_EDITOR
		void DrawSceneViewDirect(Camera sceneViewCamera)
		{
			if (sceneViewCamera == null || sceneViewCamera.cameraType != CameraType.SceneView || sceneViewCamera == Camera.main || sim == null || mesh == null)
			{
				return;
			}

			EnsureMaterials();
			UpdateSettings();
			RemoveCommandBufferFromCamera(sceneViewCamera);
			RemoveCommandBuffersByName(sceneViewCamera, CameraEvent.AfterEverything, SceneViewDirectCommandBufferName);
			DrawDirectParticles(sceneViewCamera);
			DrawVectorField(sceneViewCamera);
		}
#endif

		void EnsureRenderTextures(Camera cam)
		{
			int width = Mathf.Max(1, Mathf.RoundToInt(cam.pixelWidth * renderTextureScale));
			int height = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight * renderTextureScale));

			ComputeHelper.CreateRenderTexture(ref combinedAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Accumulation");
			ComputeHelper.CreateRenderTexture(ref combinedBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Combined Blur");
			ComputeHelper.CreateRenderTexture(ref normalAccumulationTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Accumulation");
			ComputeHelper.CreateRenderTexture(ref normalBlurTexture, width, height, FilterMode.Bilinear, GraphicsFormat.R16G16B16A16_SFloat, "Particle2D Normal Blur");

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
				RemoveCommandBufferFromCamera(Camera.main);
#if UNITY_EDITOR
				RemoveCommandBufferFromCamera(GetSceneViewCamera());
#endif
			}
			commandBufferAttached = false;
		}

		void RemoveCommandBufferFromCamera(Camera cam)
		{
			if (cam == null || metaballCommandBuffer == null)
			{
				return;
			}

			cam.RemoveCommandBuffer(CameraEvent.AfterEverything, metaballCommandBuffer);
			cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, metaballCommandBuffer);
			RemoveCommandBuffersByName(cam, CameraEvent.AfterEverything, MetaballCommandBufferName);
			RemoveCommandBuffersByName(cam, CameraEvent.BeforeImageEffects, MetaballCommandBufferName);
		}

		static void RemoveCommandBuffersByName(Camera cam, CameraEvent evt, string commandBufferName)
		{
			if (cam == null)
			{
				return;
			}

			CommandBuffer[] commandBuffers = cam.GetCommandBuffers(evt);
			for (int i = 0; i < commandBuffers.Length; i++)
			{
				CommandBuffer commandBuffer = commandBuffers[i];
				if (commandBuffer != null && commandBuffer.name == commandBufferName)
				{
					cam.RemoveCommandBuffer(evt, commandBuffer);
				}
			}
		}

		static Camera GetSceneViewCamera()
		{
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView != null)
			{
				return SceneView.lastActiveSceneView.camera;
			}
#endif
			return null;
		}

		float GetEffectiveBlurRadius(Camera cam)
		{
			return blurRadius * GetZoomScale(cam) * Mathf.Max(renderTextureScale, 0.0001f);
		}

		float GetZoomScale(Camera cam)
		{
			if (cam == null || !cam.orthographic)
			{
				return 1f;
			}

			return BlurReferenceOrthoSize / Mathf.Max(cam.orthographicSize, 0.0001f);
		}

		float GetEffectiveNormalStrength(float referenceBlurRadius)
		{
			float baseStrength = Mathf.Max(0f, particleNormalStrength);
			float compensation = Mathf.Max(0f, particleNormalBlurCompensation);
			if (compensation <= 0f)
			{
				return baseStrength;
			}

			float blurScale = Mathf.Max(0f, referenceBlurRadius) / 6f;
			return baseStrength * Mathf.Max(1f, Mathf.Pow(blurScale, compensation));
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear, bool linear = false, TextureFormat textureFormat = TextureFormat.RGBA32, bool convertGammaToLinear = false)
		{
			width = Mathf.Max(1, width);

			if (texture == null || texture.width != width || texture.format != textureFormat)
			{
				texture = new Texture2D(width, 1, textureFormat, false, linear);
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
				if (convertGammaToLinear)
				{
					cols[i] = cols[i].linear;
				}
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
			#if UNITY_EDITOR
			Camera.onPreCull -= DrawSceneViewDirect;
			#endif
			RemoveCommandBuffer();
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(vectorArgsBuffer);
			ComputeHelper.Release(combinedAccumulationTexture, combinedBlurTexture);
			ComputeHelper.Release(normalAccumulationTexture, normalBlurTexture);
			// Release jump flood textures
			ComputeHelper.Release(jfaSeedA, jfaSeedB, jfaTemp);
			RemoveCommandBuffer();
			if (metaballCommandBuffer != null)
			{
				metaballCommandBuffer.Release();
			}
			if (vectorArrowMesh != null)
			{
				DestroyImmediate(vectorArrowMesh);
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
