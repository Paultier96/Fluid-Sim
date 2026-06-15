using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	[AddComponentMenu("Fluid Sim/2D/Particle Fluid Input Manager 2D")]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ParticleDisplay2D))]
	public sealed class ParticleFluidInputManager2D : MonoBehaviour
	{
		[SerializeField] ParticleDisplay2D display;
		[SerializeField] ParticleFluidLighting2D lighting;

		void Awake()
		{
			FindMissingReferences();
		}

		void OnValidate()
		{
			FindMissingReferences();
		}

		void Update()
		{
			UpdateDebugModeFromKeyboard();
		}

		void FindMissingReferences()
		{
			if (display == null)
			{
				display = GetComponent<ParticleDisplay2D>();
			}

			if (lighting == null)
			{
				lighting = GetComponent<ParticleFluidLighting2D>();
			}
		}

		void UpdateDebugModeFromKeyboard()
		{
			if (display == null)
			{
				FindMissingReferences();
				if (display == null) return;
			}

			if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Gradient, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Curvature, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Viscosity, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Density, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Temperature, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.BlobIds, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.Caustics);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.SoftLight);
			}
			else if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.DirectionalLightField);
			}
			else if (Input.GetKeyDown(KeyCode.Q))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.CausticMotion);
			}
			else if (Input.GetKeyDown(KeyCode.W))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.ParticleMotion, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
		}

		void SetDebugMode(ParticleDisplay2D.DebugVisualization displayMode, ParticleFluidLighting2D.LightingDebugVisualization lightingMode)
		{
			display.debugMode = displayMode;
			if (lighting != null)
			{
				lighting.debugMode = lightingMode;
			}
		}
		
		void OnGUI()
		{
			GUIStyle style = new GUIStyle(GUI.skin.box)
			{
				alignment = TextAnchor.MiddleLeft,
				fontSize = 20,
			};

			ParticleFluidLighting2D lighting = GetComponent<ParticleFluidLighting2D>();
			string debugLabel = display.debugMode != ParticleDisplay2D.DebugVisualization.None
				? $"Debug: {display.debugMode}"
				: lighting != null && lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None
					? $"Lighting: {lighting.debugMode}"
					: "Debug: None";
			GUI.Box(new Rect(30, 30, 280, 30), debugLabel, style);
		}
	}
}
