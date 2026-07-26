using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public class ParticleDisplay2D : MonoBehaviour
	{
		private static readonly int Positions2D = Shader.PropertyToID("Positions2D");
		private static readonly int Velocities = Shader.PropertyToID("Velocities");
		private static readonly int DensityData = Shader.PropertyToID("DensityData");
		private static readonly int Phases = Shader.PropertyToID("Phases");
		private static readonly int IsGhost = Shader.PropertyToID("IsGhost");
		private static readonly int BlobIDs = Shader.PropertyToID("BlobIDs");
		private static readonly int Temperatures = Shader.PropertyToID("Temperatures");
		private static readonly int Curvatures = Shader.PropertyToID("Curvatures");
		private static readonly int Scale = Shader.PropertyToID("scale");
		private static readonly int TempMin = Shader.PropertyToID("tempMin");
		private static readonly int TempMax = Shader.PropertyToID("tempMax");
		private static readonly int DebugData = Shader.PropertyToID("DebugData");
		private static readonly int DebugGradientMax = Shader.PropertyToID("debugGradientMax");
		private static readonly int DebugCurvatureMax = Shader.PropertyToID("debugCurvatureMax");
		private static readonly int DebugViscosityMax = Shader.PropertyToID("debugViscosityMax");
		private static readonly int DensityMin = Shader.PropertyToID("debugDensityMin");
		private static readonly int DensityMax = Shader.PropertyToID("debugDensityMax");
		private static readonly int DebugMode = Shader.PropertyToID("debugMode");
		private static readonly int DebugShowClipping = Shader.PropertyToID("debugShowClipping");
		public ParticleFluidInteractionCursor2D interactionCursor;
		public enum RenderMode
		{
			DirectParticles = 0,
			Metaballs = 2
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
			ParticleMotion = 10,
		}

		public MetaballRenderer2D metaballs = new ();
		public ParticleFluidDirectParticleRenderer2D directParticles = new();
		public ParticleFluidVectorFieldRenderer2D vectorField = new();

		private const float BlurReferenceOrthoSize = 15f;
		internal float EffectiveConfiguredBlurRadius => metaballs.blurRadius * ParticleResolutionLengthScale;
		private float ParticleResolutionLengthScale => 1f / Mathf.Sqrt(Mathf.Max(0.0001f, sim.particleResolutionFactor));

		internal float GetEffectiveMotionBlurRadius(Camera cam, float motionBlurRadius)
		{
			motionBlurRadius *= ParticleResolutionLengthScale;
			return motionBlurRadius * GetZoomScale(cam) * Mathf.Max(metaballs.velocityTextureScale, 0.0001f);
		}

		internal float GetZoomScale(Camera cam)
		{
			return BlurReferenceOrthoSize / Mathf.Max(cam.orthographicSize, 0.0001f);
		}
		
		internal float EffectiveNormalStrength => 
			metaballs.normalBlurCompensation <= 0f ? 
			metaballs.normalStrength : 
			metaballs.normalStrength * Mathf.Max(1f, Mathf.Pow(EffectiveConfiguredBlurRadius / 6f, metaballs.normalBlurCompensation));
		
		public FluidSim2D sim;
		public ParticleFluidLighting2D lighting;
		internal ParticleFluidLighting2D ActiveLighting => lighting is { isActiveAndEnabled: true } ? lighting : null;
		public Mesh particleMesh;
		public RenderMode renderMode = RenderMode.DirectParticles;
		public Shader metaballShader;
		public float scale;

		[Header("Albedo")]
		public Gradient phase0ColourMap;
		public Gradient phase1ColourMap;
		public int gradientResolution = 64;

		[Header("Debug")]
		public DebugVisualization debugMode = DebugVisualization.None;
		public float debugGradientMax = 1.0f;
		[Min(0f)] public float debugDensityMin = 210f;
		[Min(0.0001f)] public float debugDensityMax = 500f;
		public bool debugShowClipping = true;
		public bool showHashGridOverlay;
		public Gradient heatMap;
		public Gradient signedHeatMap;
		
		internal ComputeBuffer argsBuffer;
		internal Texture2D gradientAtlasTexture;
		private bool _needsUpdate;
		private DebugVisualization _lastDebugMode;
		private ParticleFluidVectorFieldRenderer2D.VectorFieldSource _lastVectorFieldSource;
		private Camera _camera;

		private void Awake()
		{
			_camera = Camera.main;
			_needsUpdate = true;
			_lastDebugMode = debugMode;
			_lastVectorFieldSource = vectorField.source;
		}

		private void LateUpdate()
		{
			if (debugMode != _lastDebugMode || vectorField.source != _lastVectorFieldSource)
			{
				_lastDebugMode = debugMode;
				_lastVectorFieldSource = vectorField.source;
				sim.RefreshDebugBuffers();
			}

			ComputeHelper.CreateArgsBuffer(ref argsBuffer, particleMesh, sim.resources.positionBuffer.count);

			if (_needsUpdate)
			{
				_needsUpdate = false;
				EnsureGradientTextures();
			}

			directParticles.Prepare(this);
			vectorField.Prepare(this);

			if (renderMode == RenderMode.Metaballs) return;
			directParticles.Draw(this, _camera);
			vectorField.Draw(this, _camera);
		}

		internal void BindSimulationBuffers(Material targetMaterial)
		{
			targetMaterial.SetBuffer(Positions2D, sim.resources.positionBuffer);
			targetMaterial.SetBuffer(Velocities, sim.resources.velocityBuffer);
			targetMaterial.SetBuffer(DensityData, sim.resources.densityBuffer);
			targetMaterial.SetBuffer(Phases, sim.resources.phaseBuffer);
			targetMaterial.SetBuffer(IsGhost, sim.resources.ghostFlagBuffer);
			targetMaterial.SetBuffer(BlobIDs, sim.resources.blobIdBuffer);
			targetMaterial.SetBuffer(Temperatures, sim.resources.temperatureBuffer);
			targetMaterial.SetBuffer(Curvatures, sim.resources.debugVectorSignBuffer);
		}

		internal void ApplyCommonParticleSettings(Material targetMaterial)
		{
			targetMaterial.SetFloat(Scale, scale * ParticleResolutionLengthScale);
			targetMaterial.SetFloat(TempMin, sim.ambientTemperature);
			targetMaterial.SetFloat(TempMax, sim.HeatSourceTemperature);
			targetMaterial.SetBuffer(DebugData, sim.resources.debugDataBuffer);
			targetMaterial.SetFloat(DebugGradientMax, debugGradientMax);
			targetMaterial.SetFloat(DebugCurvatureMax, sim.maxSurfaceTensionCurvature);
			targetMaterial.SetFloat(DebugViscosityMax, sim.simulationDebug.GetMaxViscosity(sim.phases, sim.ambientTemperature, sim.HeatSourceTemperature));
			targetMaterial.SetFloat(DensityMin, Mathf.Min(debugDensityMin, debugDensityMax - 0.0001f) * sim.particleResolutionFactor);
			targetMaterial.SetFloat(DensityMax, Mathf.Max(debugDensityMax, debugDensityMin + 0.0001f) * sim.particleResolutionFactor);
			targetMaterial.SetInt(DebugMode, (int)(debugMode == DebugVisualization.ParticleMotion ? DebugVisualization.None : debugMode));
			targetMaterial.SetInt(DebugShowClipping, debugShowClipping ? 1 : 0);
		}

		private void EnsureGradientTextures()
		{
			const int height = 4;
			if (!gradientAtlasTexture || gradientAtlasTexture.width != gradientResolution || gradientAtlasTexture.height != height || gradientAtlasTexture.format != TextureFormat.RGBAHalf)
			{
				gradientAtlasTexture = new Texture2D(gradientResolution, height, TextureFormat.RGBAHalf, false, true);
			}

			gradientAtlasTexture.wrapMode = TextureWrapMode.Clamp;
			gradientAtlasTexture.filterMode = FilterMode.Bilinear;

			WriteGradientRow(gradientAtlasTexture, 0, gradientResolution, phase0ColourMap, true);
			WriteGradientRow(gradientAtlasTexture, 1, gradientResolution, phase1ColourMap, true);
			WriteGradientRow(gradientAtlasTexture, 2, gradientResolution, heatMap, false);
			WriteGradientRow(gradientAtlasTexture, 3, gradientResolution, signedHeatMap, false);
			gradientAtlasTexture.Apply();
		}

		internal void SetPhaseColourMaps(Gradient phase0, Gradient phase1)
		{
			phase0ColourMap = phase0;
			phase1ColourMap = phase1;
			_needsUpdate = true;
		}

		private static void WriteGradientRow(Texture2D texture, int row, int width, Gradient gradient, bool convertGammaToLinear)
		{
			for (int x = 0; x < width; x++)
			{
				float t = width == 1 ? 0 : x / (width - 1f);
				Color colour = gradient.Evaluate(t);
				if (convertGammaToLinear)
				{
					colour = colour.linear;
				}
				texture.SetPixel(x, row, colour);
			}
		}

		private void OnValidate()
		{
			_needsUpdate = true;
		}

		private void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			directParticles?.Release();
			vectorField?.Release();
			metaballs?.Release();
			lighting?.Release();
		}
	}
}
