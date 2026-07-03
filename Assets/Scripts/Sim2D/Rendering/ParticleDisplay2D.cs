using System;
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
	public class ParticleDisplay2D : MonoBehaviour
	{
		public enum RenderMode
		{
			DirectParticles,
			JumpFlood,
			Metaballs
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
			ParticleMotion = 11,
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
		
		
		public MetaballSettings metaballs = new ();

		MetaballRenderer2D metaballRenderer;
		internal MetaballRenderer2D MetaballRenderer => metaballRenderer ??= new MetaballRenderer2D();

		const float BlurReferenceOrthoSize = 15f;

		internal float EffectiveConfiguredBlurRadius => metaballs.blurRadius * ParticleResolutionLengthScale;

		float ParticleResolutionLengthScale
		{
			get
			{
				float resolutionFactor = Mathf.Max(0.0001f, sim.particleResolutionFactor);
				return 1f / Mathf.Sqrt(resolutionFactor);
			}
		}

		internal float GetEffectiveBlurRadius(Camera cam)
		{
			return EffectiveConfiguredBlurRadius * GetZoomScale(cam) * Mathf.Max(metaballs.renderTextureScale, 0.0001f);
		}

		internal float GetEffectiveMotionBlurRadius(Camera cam, float motionBlurRadius)
		{
			motionBlurRadius *= ParticleResolutionLengthScale;
			return motionBlurRadius * GetZoomScale(cam) * Mathf.Max(metaballs.renderTextureScale, 0.0001f);
		}

		internal float GetZoomScale(Camera cam)
		{
			if (!cam.orthographic)
			{
				return 1f;
			}

			return BlurReferenceOrthoSize / Mathf.Max(cam.orthographicSize, 0.0001f);
		}

		internal float GetEffectiveNormalStrength(float referenceBlurRadius)
		{
			float baseStrength = Mathf.Max(0f, metaballs.normalStrength);
			float compensation = Mathf.Max(0f, metaballs.normalBlurCompensation);
			if (compensation <= 0f)
			{
				return baseStrength;
			}

			float blurScale = Mathf.Max(0f, referenceBlurRadius) / 6f;
			return baseStrength * Mathf.Max(1f, Mathf.Pow(blurScale, compensation));
		}

		[Serializable]
		public sealed class MetaballSettings
		{
			[Header("Shaders")]
			[FormerlySerializedAs("compositeShader")]
			[Tooltip("Shader used only for metaball debug visualizations.")]
			public Shader debugShader;
			[Tooltip("Shader used by the separated material-map pass. If left empty, Hidden/Particle2DMetaballMaterial is used as a fallback.")]
			public Shader materialShader;
			[Tooltip("Shader used for the separable Gaussian blur applied to the accumulation texture.")]
			public Shader blurShader;

			[Header("Shape - Surface")]
			[Tooltip("Resolution of the metaball render textures relative to the screen. Lower values improve performance at the cost of sharpness.")]
			[Range(0.25f, 1f)] public float renderTextureScale = 0.5f;
			[Tooltip("Radius in pixels at resolution factor 1 of the Gaussian blur. Larger values make particles merge at greater distances.")]
			[Min(0)] public float blurRadius = 6;
			[Tooltip("Blurred density value at which the fluid surface appears. Increase to shrink the visible fluid; decrease to expand it.")]
			[Min(0)] public float densityThreshold = 0.18f;
			[Tooltip("Width of the density falloff around the surface threshold. Larger values give a softer, more transparent edge. Clamped so the fade never starts below zero density.")]
			[Min(0.0001f)] public float edgeSoftness = 0.06f;

			[Header("Shape - Phase Boundary")]
			[Tooltip("Screen-space width in pixels for anti-aliased blending between fluid phases.")]
			[Min(0.0001f)] public float phaseBlendWidth = 1f;
			[Tooltip("Render-only phase boundary bias. 0 is neutral, positive values make phase 0 visually expand, negative values make phase 1 expand.")]
			[Range(-0.99f, 0.99f)] public float phase0RenderBias = 0f;
			[Tooltip("How strongly phase boundary bias redistributes normal strength. The compressed phase is boosted strongly while the visually expanded phase is weakened mildly.")]
			[Range(0f, 10f)] public float phaseBiasNormalStrength = 0.5f;

			[Header("Shape - Particle Kernel")]
			[Tooltip("Steepness of each particle's density kernel. Higher values make particles contribute a tighter, more localised density spike.")]
			[Min(0.01f)] public float sharpness = 3.5f;
			[Tooltip("Uniform scale applied to each particle's density contribution. Increase if particles are too sparse to merge.")]
			[Min(0)] public float intensity = 1.0f;

			[Header("Lighting - Normals")]
			[Tooltip("Multiplier applied to reconstructed normal XY before rebuilding Z. Higher values make blurred normals look steeper.")]
			[Min(0f)] public float normalStrength = 1f;
			[Tooltip("Curves the reconstructed normal magnitude before rebuilding Z. Values above 1 keep the surface flatter for longer and push the steep falloff closer to the silhouette.")]
			[Min(0.0001f)] public float normalProfileCurve = 1f;
			[Tooltip("Exponent used to increase normal strength with effective blur radius. 0 disables automatic compensation, 1 is linear.")]
			[Min(0f)] public float normalBlurCompensation = 0.5f;

			[Header("Ghost Boundary Normals")]
			[Tooltip("Strength of the analytic ellipse/cut-boundary normals in the metaball composite. Values above 1 make the boundary normal ramp steeper; negative values flip the direction.")]
			[Range(-4f, 4f)] public float ghostBoundaryNormalStrength = 1f;
		}
		
		public JumpFloodSettings jumpFlood = new();

		JumpFloodRenderer2D jumpFloodRenderer;
		
		internal JumpFloodRenderer2D JumpFloodRenderer => jumpFloodRenderer ??= new JumpFloodRenderer2D();

		[Serializable]
		public sealed class JumpFloodSettings
		{
			public ComputeShader computeShader;
			public Shader displayShader;
			[Tooltip("Shader that converts the Jump Flood result into the albedo/normal material maps consumed by ParticleFluidLighting2D.")]
			public Shader materialShader;
		}

		public FluidSim2D sim;
		[SerializeField] ParticleFluidLighting2D lighting;
		public Mesh mesh;
		public RenderMode renderMode = RenderMode.DirectParticles;
		public Shader directParticleShader;
		public Shader metaballShader;
		public float scale;

		[Header("Albedo")]
		public Gradient phase0ColourMap;
		public Gradient phase1ColourMap;
		public int gradientResolution;

		[Header("Debug")]
		public DebugVisualization debugMode = DebugVisualization.None;
		public float debugGradientMax = 1.0f;
		[Min(0f)] public float debugDensityMin = 0f;
		[Min(0.0001f)] public float debugDensityMax = 500f;
		public bool debugShowClipping = true;
		public Gradient heatMap;
		public Gradient signedHeatMap;

		[Header("Vector Field Debug")]
		public Shader vectorFieldShader;
		public VectorFieldSource vectorFieldSource = VectorFieldSource.SurfaceTensionForce;
		[Min(0f)] public float vectorScale = 0.25f;
		[Min(0.0001f)] public float vectorMaxMagnitude = 1.0f;
		public bool vectorUseLogScale = false;
		[Min(1f)] public float vectorLogScaleStrength = 10.0f;
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
			RefreshSimulationDebugBuffersIfNeeded();
			EnsureMaterials();
			UpdateSettings();

			if (renderMode != RenderMode.JumpFlood && (renderMode != RenderMode.Metaballs || metaballs.blurShader == null))
			{
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
			targetMaterial.SetFloat("scale", scale * ParticleResolutionLengthScale);
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
		internal float DebugDensityMin => Mathf.Min(debugDensityMin, debugDensityMax - 0.0001f) * sim.particleResolutionFactor;
		internal float DebugDensityMax => Mathf.Max(debugDensityMax, debugDensityMin + 0.0001f) * sim.particleResolutionFactor;
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
		bool IsCompositeOnlyDebugMode(DebugVisualization mode)
		{
			return mode == DebugVisualization.ParticleMotion;
		}

		DebugVisualization ParticleShaderDebugMode => IsCompositeOnlyDebugMode(debugMode) ? DebugVisualization.None : debugMode;

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
			Gradient colourMap = index == 0 ? phase0ColourMap : phase1ColourMap;
			if (colourMap != null)
			{
				return colourMap;
			}

			Gradient gradient = new Gradient();
			gradient.SetKeys(
				new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) },
				new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
			);
			return gradient;
		}

		internal void SetPhaseColourMaps(Gradient phase0, Gradient phase1)
		{
			phase0ColourMap = CloneGradient(phase0);
			phase1ColourMap = CloneGradient(phase1);
			needsUpdate = true;
		}

		internal static Gradient CloneGradient(Gradient source)
		{
			if (source == null)
			{
				return null;
			}

			Gradient clone = new Gradient();
			clone.SetKeys(source.colorKeys, source.alphaKeys);
			clone.mode = source.mode;
			return clone;
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
			DrawDirectParticles(sceneViewCamera);
			DrawVectorField(sceneViewCamera);
		}
#endif

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

		void OnDisable()
		{
			#if UNITY_EDITOR
			Camera.onPreCull -= DrawSceneViewDirect;
			#endif
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(vectorArgsBuffer);
			metaballRenderer?.Release();
			jumpFloodRenderer?.Release();
			ResolveLighting()?.Release();
			if (vectorArrowMesh != null)
			{
				DestroyImmediate(vectorArrowMesh);
			}
		}

		internal ParticleFluidLighting2D Lighting => ResolveLighting();

		ParticleFluidLighting2D ResolveLighting()
		{
			if (lighting != null)
			{
				return lighting;
			}

			lighting = GetComponent<ParticleFluidLighting2D>();
			if (lighting != null)
			{
				return lighting;
			}

			if (transform.parent != null)
			{
				lighting = transform.parent.GetComponentInChildren<ParticleFluidLighting2D>(true);
			}

			lighting ??= FindAnyObjectByType<ParticleFluidLighting2D>();

			return lighting;
		}
    }
}
