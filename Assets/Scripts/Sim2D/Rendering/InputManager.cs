using UnityEngine;
using UnityEngine.InputSystem;

namespace Seb.Fluid2D.Rendering
{
	[AddComponentMenu("Fluid Sim/2D/Particle Fluid Input Manager 2D")]
	[DisallowMultipleComponent]
	public sealed class InputManager : MonoBehaviour
	{
		[SerializeField] private ParticleDisplay2D display;
		[SerializeField] private ParticleFluidLighting2D lighting;

		private void Awake()
		{
			if (display == null)
			{
				display = GetComponent<ParticleDisplay2D>();
			}

			if (lighting == null)
			{
				lighting = display != null ? display.ActiveLighting : null;
			}

			if (lighting == null)
			{
				lighting = GetComponent<ParticleFluidLighting2D>();
			}

			if (lighting == null && transform.parent != null)
			{
				lighting = transform.parent.GetComponentInChildren<ParticleFluidLighting2D>(true);
			}
		}

		private void Update()
		{
			if (KeyPressed(Key.Digit0) || KeyPressed(Key.Numpad0))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit1) || KeyPressed(Key.Numpad1))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Gradient, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit2) || KeyPressed(Key.Numpad2))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Curvature, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit3) || KeyPressed(Key.Numpad3))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Viscosity, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit4) || KeyPressed(Key.Numpad4))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Density, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit5) || KeyPressed(Key.Numpad5))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.Temperature, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit6) || KeyPressed(Key.Numpad6))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.BlobIds, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.Digit7) || KeyPressed(Key.Numpad7))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.Caustics);
			}
			else if (KeyPressed(Key.Digit8) || KeyPressed(Key.Numpad8))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.SoftLight);
			}
			else if (KeyPressed(Key.U))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.None, ParticleFluidLighting2D.LightingDebugVisualization.RadianceCascadeRaw);
			}
			else if (KeyPressed(Key.W))
			{
				SetDebugMode(ParticleDisplay2D.DebugVisualization.ParticleMotion, ParticleFluidLighting2D.LightingDebugVisualization.None);
			}
			else if (KeyPressed(Key.G))
			{
				display.showHashGridOverlay = !display.showHashGridOverlay;
			}
		}

		private static bool KeyPressed(Key key)
		{
			return Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
		}

		private void SetDebugMode(ParticleDisplay2D.DebugVisualization displayMode, ParticleFluidLighting2D.LightingDebugVisualization lightingMode)
		{
			display.debugMode = displayMode;
			if (lighting != null)
			{
				lighting.debugMode = lightingMode;
			}
		}

		private void OnGUI()
		{
			GUIStyle style = new GUIStyle(GUI.skin.box)
			{
				alignment = TextAnchor.MiddleLeft,
				fontSize = 20,
			};

			string debugLabel = display.debugMode != ParticleDisplay2D.DebugVisualization.None
				? $"Debug: {display.debugMode}"
				: lighting != null && lighting.debugMode != ParticleFluidLighting2D.LightingDebugVisualization.None
					? $"Lighting: {lighting.debugMode}"
					: "Debug: None";
			GUI.Box(new Rect(30, 30, 280, 30), debugLabel, style);
		}
	}
}
