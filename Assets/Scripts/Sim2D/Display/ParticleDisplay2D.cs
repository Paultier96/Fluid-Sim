using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleDisplay2D : MonoBehaviour
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
			NonCoalescenceForces = 2,
			Velocity = 3,
			CurvatureNormal = 4,
			Convection = 5,
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

		[Header("Debug")]
		[Tooltip("Selects what to show in debug mode. Press 0-6 to switch modes at runtime.")]
		public DebugVisualization debugMode = DebugVisualization.None;
		[Tooltip("Maximum absolute value mapped in gradient debug visualisation.")]
		public float debugGradientMax = 1.0f;
		[Tooltip("Lower density value used for density debug colour mapping.")]
		[Min(0f)] public float debugDensityMin = 0f;
		[Tooltip("Upper density value used for density debug colour mapping.")]
		[Min(0.0001f)] public float debugDensityMax = 500f;
		[Tooltip("Marks debug clipping. Normals clip to green; heatmap values below range clip to cyan and above range clip to magenta.")]
		public bool debugShowClipping = true;
		[Tooltip("Colour gradient used by viscosity, density, and temperature debug views.")]
		public Gradient heatMap;
		[FormerlySerializedAs("debugSignedHeatMapGradient")] [Tooltip("Signed colour gradient used by curvature debug views. The centre represents zero; left is negative, right is positive.")]
		public Gradient signedHeatMap;

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

		Material directParticleMaterial;
		Material vectorFieldMaterial;
		Mesh vectorArrowMesh;
		
		internal ComputeBuffer argsBuffer;
		ComputeBuffer vectorArgsBuffer;
		Bounds bounds;
		internal Texture2D gradientTexture;
		internal Texture2D gradientTexture2;
		internal Texture2D debugHeatMapTexture;
		internal Texture2D debugSignedHeatMapTexture;
		bool needsUpdate;
		DebugVisualization lastDebugMode;
		VectorFieldSource lastVectorFieldSource;
		const string SceneViewDirectCommandBufferName = "Sim2D Scene View Direct Particles";

		void Awake()
		{
			Debug.Assert(sim != null, "ParticleDisplay2D requires a FluidSim2D reference.", this);
			Debug.Assert(mesh != null, "ParticleDisplay2D requires a particle mesh.", this);
			Debug.Assert(directParticleShader != null, "ParticleDisplay2D requires a direct particle shader.", this);
			EnsureMaterials();
			needsUpdate = true;
			lastDebugMode = debugMode;
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
			RefreshSimulationDebugBuffersIfNeeded();
			EnsureMaterials();
			UpdateSettings();

			if (renderMode == RenderMode.JumpFlood)
			{
				jumpFloodRenderer ??= new JumpFloodRenderer2D();
				jumpFloodRenderer.Render(this, Camera.main);
			}
			else if (renderMode == RenderMode.Metaballs && metaballs.compositeShader != null && metaballs.blurShader != null)
			{
				jumpFloodRenderer?.RemoveCommandBuffer();
				metaballRenderer ??= new MetaballRenderer2D();
				metaballRenderer.Render(this, Camera.main);
			}
			else
			{
				RemoveCommandBuffer();
				DrawDirectParticles(Camera.main);
				DrawVectorField(Camera.main);
			}
		}

		void RefreshSimulationDebugBuffersIfNeeded()
		{
			bool changed = debugMode != lastDebugMode || vectorFieldSource != lastVectorFieldSource;
			if (!changed) return;

			lastDebugMode = debugMode;
			lastVectorFieldSource = vectorFieldSource;
			sim.RefreshDebugBuffers();
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
			
			ApplyDirectParticleMaterialSettings();
			ApplyVectorFieldMaterialSettings();
			ApplyVectorFieldSettings();
		}

		void ApplyDirectParticleMaterialSettings()
		{
			BindSimulationBuffers(directParticleMaterial);
			ApplyCommonParticleSettings(directParticleMaterial);
		}

		void ApplyVectorFieldMaterialSettings()
		{
			if (vectorFieldMaterial == null)
			{
				return;
			}

			BindSimulationBuffers(vectorFieldMaterial);
		}

		void EnsureMaterials()
		{
			if (vectorFieldShader == null)
			{
				vectorFieldShader = Shader.Find("Instanced/Particle2DVectorField");
			}

			EnsureMaterial(ref directParticleMaterial, directParticleShader);
			EnsureMaterial(ref vectorFieldMaterial, vectorFieldShader);
			EnsureVectorArrowMesh();
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

		internal void BindSimulationBuffers(Material targetMaterial)
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

		internal void ApplyCommonParticleSettings(Material targetMaterial)
		{
			targetMaterial.SetFloat("scale", EffectiveParticleScale);
			targetMaterial.SetFloat("tempMin", sim.ambientTemperature);
			targetMaterial.SetFloat("tempMax", sim.HeatSourceTemperature);
			targetMaterial.SetBuffer("DebugData", sim.debugDataBuffer);
			targetMaterial.SetFloat("debugGradientMax", debugGradientMax);
			targetMaterial.SetFloat("debugCurvatureMax", sim.MaxDebugCurvature);
			targetMaterial.SetFloat("debugViscosityMax", sim.MaxDebugViscosity);
			targetMaterial.SetFloat("debugDensityMin", DebugDensityMin);
			targetMaterial.SetFloat("debugDensityMax", DebugDensityMax);
			targetMaterial.SetInt("debugMode", (int)ParticleShaderDebugMode);
			ApplyDebugClipSettings(targetMaterial);
		}

		float EffectiveParticleScale
		{
			get
			{
				return scale * ParticleResolutionLengthScale;
			}
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
			vectorFieldMaterial.SetBuffer("DebugVectorData", GetVectorFieldBuffer());
			vectorFieldMaterial.SetBuffer("DebugVectorSign", sim.debugVectorSignBuffer);
		}

		float DensityDebugScale => Mathf.Max(0.0001f, sim.particleResolutionFactor);
		internal float DebugDensityMin => Mathf.Min(debugDensityMin, debugDensityMax - 0.0001f) * DensityDebugScale;
		internal float DebugDensityMax => Mathf.Max(debugDensityMax, debugDensityMin + 0.0001f) * DensityDebugScale;
		float EffectiveVectorMaxMagnitude
		{
			get
			{
				return vectorFieldSource switch
				{
					VectorFieldSource.CurvatureNormal => sim.MaxDebugCurvature,
					VectorFieldSource.SurfaceTensionForce => sim.MaxDebugSurfaceTensionForce,
					VectorFieldSource.Convection => sim.MaxDebugConvection,
					VectorFieldSource.NonCoalescenceForces => sim.carrierWedgeMaxAcceleration > 0
						? Mathf.Max(sim.carrierWedgeMaxAcceleration, vectorMaxMagnitude)
						: Mathf.Max(0.0001f, Mathf.Abs(sim.carrierWedgeStrength), vectorMaxMagnitude),
					_ => Mathf.Max(0.0001f, vectorMaxMagnitude),
				};
			}
		}
		DebugVisualization ParticleShaderDebugMode => debugMode;

		internal void ApplyDebugClipSettings(Material targetMaterial)
		{
			targetMaterial.SetInt("debugShowClipping", debugShowClipping ? 1 : 0);
		}

		public int ComputeVectorFieldMode
		{
			get
			{
				return vectorFieldSource switch
				{
					VectorFieldSource.SurfaceTensionForce => 1,
					VectorFieldSource.NonCoalescenceForces => 2,
					VectorFieldSource.CurvatureNormal => 3,
					VectorFieldSource.Convection => 4,
					_ => 0,
				};
			}
		}

		ComputeBuffer GetVectorFieldBuffer()
		{
			return vectorFieldSource == VectorFieldSource.Velocity ? sim.velocityBuffer : sim.debugVectorDataBuffer;
		}

		void ApplyGradientTextures()
		{
			directParticleMaterial.SetTexture("ColourMap", gradientTexture);
			directParticleMaterial.SetTexture("ColourMap2", gradientTexture2);
			directParticleMaterial.SetTexture("DebugHeatMap", debugHeatMapTexture);
			directParticleMaterial.SetTexture("DebugSignedHeatMap", debugSignedHeatMapTexture);
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

		void DrawDirectParticles(Camera cam)
		{
			if (argsBuffer == null)
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

		internal void AppendVectorFieldDraw(CommandBuffer commandBuffer)
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
			if (sceneViewCamera.cameraType != CameraType.SceneView || sceneViewCamera == Camera.main)
			{
				return;
			}

			EnsureMaterials();
			UpdateSettings();
			metaballRenderer?.RemoveFromCamera(sceneViewCamera);
			RemoveCommandBuffersByName(sceneViewCamera, CameraEvent.AfterEverything, SceneViewDirectCommandBufferName);
			DrawDirectParticles(sceneViewCamera);
			DrawVectorField(sceneViewCamera);
		}
#endif

		void RemoveCommandBuffer()
		{
			metaballRenderer?.RemoveCommandBuffer();
			jumpFloodRenderer?.RemoveCommandBuffer();
		}

		static void RemoveCommandBuffersByName(Camera cam, CameraEvent evt, string commandBufferName)
		{
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

		internal static Camera GetSceneViewCamera()
		{
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView != null)
			{
				return SceneView.lastActiveSceneView.camera;
			}
#endif
			return null;
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
					new GradientColorKey[] { new (Color.black, 0), new (Color.black, 1) },
					new GradientAlphaKey[] { new (1, 0), new (1, 1) }
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
			RemoveCommandBuffer();
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(vectorArgsBuffer);
			metaballRenderer?.Release();
			jumpFloodRenderer?.Release();
			if (vectorArrowMesh != null)
			{
				DestroyImmediate(vectorArrowMesh);
			}
		}
    }
}
