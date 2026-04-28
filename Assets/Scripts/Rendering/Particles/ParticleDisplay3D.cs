using Seb.Helpers;
using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{

	public class ParticleDisplay3D : MonoBehaviour
	{
		public enum DisplayMode
		{
			None,
			Shaded3D,
			Billboard
		}

		[Header("Settings")] public DisplayMode mode;
		public float scale;
		public Gradient colourMap;
		public bool colorByPhase = true;
		public Gradient[] phaseColourMaps;
		public int gradientResolution;
		public float velocityDisplayMax;
		public int meshResolution;

		[Header("References")] public FluidSim sim;
		public Shader shaderShaded;
		public Shader shaderBillboard;

		Mesh mesh;
		Material mat;
		ComputeBuffer argsBuffer;
		Texture2D gradientTexture;
		Texture2D phaseGradientTexture;
		DisplayMode modeOld;
		bool needsUpdate;

		void LateUpdate()
		{
			UpdateSettings();

			if (mode != DisplayMode.None)
			{
				Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
				Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
			}
		}

		void UpdateSettings()
		{
			if (modeOld != mode)
			{
				modeOld = mode;
				if (mode != DisplayMode.None)
				{
					if (mode == DisplayMode.Billboard) mesh = QuadGenerator.GenerateQuadMesh();
					else mesh = SphereGenerator.GenerateSphereMesh(meshResolution);
					ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);

					mat = mode switch
					{
						DisplayMode.Shaded3D => new Material(shaderShaded),
						DisplayMode.Billboard => new Material(shaderBillboard),
						_ => null
					};


					mat.SetBuffer("Positions", sim.positionBuffer);
					mat.SetBuffer("Velocities", sim.velocityBuffer);
					mat.SetBuffer("Phases", sim.phaseBuffer);
					mat.SetBuffer("ParticleIDs", sim.particleIdBuffer);
					mat.SetBuffer("DebugBuffer", sim.debugBuffer);
					needsUpdate = true;
				}
			}

			if (mat != null)
			{
				if (needsUpdate)
				{
					needsUpdate = false;
					if (colorByPhase)
					{
						int phaseRowsForTexture = Mathf.Max(1, sim != null && sim.phases != null ? sim.phases.Length : (phaseColourMaps != null ? phaseColourMaps.Length : 0));
						TextureFromPhaseGradients(ref phaseGradientTexture, Mathf.Max(2, gradientResolution), phaseRowsForTexture, phaseColourMaps);
						mat.SetTexture("ColourMap", phaseGradientTexture);
					}
					else
					{
						TextureFromGradient(ref gradientTexture, Mathf.Max(2, gradientResolution), colourMap);
						mat.SetTexture("ColourMap", gradientTexture);
					}
				}

				int phaseRows = colorByPhase ? Mathf.Max(1, sim != null && sim.phases != null ? sim.phases.Length : (phaseColourMaps != null ? phaseColourMaps.Length : 0)) : 1;
				mat.SetInt("usePhaseColoring", colorByPhase ? 1 : 0);
				mat.SetInt("numPhaseRows", phaseRows);

				mat.SetFloat("scale", scale * 0.01f);
				mat.SetFloat("velocityMax", velocityDisplayMax);

				Vector3 s = transform.localScale;
				transform.localScale = Vector3.one;
				var localToWorld = transform.localToWorldMatrix;
				transform.localScale = s;

				mat.SetMatrix("localToWorld", localToWorld);
			}
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
		{
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
					new GradientColorKey[] { new(Color.black, 0), new(Color.black, 1) },
					new GradientAlphaKey[] { new(1, 0), new(1, 1) }
				);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width];
			for (int i = 0; i < cols.Length; i++)
			{
				float t = i / (cols.Length - 1f);
				cols[i] = gradient.Evaluate(t);
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		static Gradient GetOrCreateGradient(Gradient gradient)
		{
			if (gradient != null) return gradient;
			gradient = new Gradient();
			gradient.SetKeys(
				new GradientColorKey[] { new(Color.black, 0), new(Color.black, 1) },
				new GradientAlphaKey[] { new(1, 0), new(1, 1) }
			);
			return gradient;
		}

		public static void TextureFromPhaseGradients(ref Texture2D texture, int width, int rowCount, Gradient[] gradients, FilterMode filterMode = FilterMode.Bilinear)
		{
			int height = Mathf.Max(1, rowCount);
			if (texture == null)
			{
				texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
			}
			else if (texture.width != width || texture.height != height)
			{
				texture.Reinitialize(width, height);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width * height];
			for (int y = 0; y < height; y++)
			{
				Gradient gradient = GetOrCreateGradient((gradients != null && y < gradients.Length) ? gradients[y] : null);
				for (int x = 0; x < width; x++)
				{
					float t = x / (width - 1f);
					cols[y * width + x] = gradient.Evaluate(t);
				}
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		private void OnValidate()
		{
			needsUpdate = true;
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			if (gradientTexture != null) Destroy(gradientTexture);
			if (phaseGradientTexture != null) Destroy(phaseGradientTexture);
		}
	}
}