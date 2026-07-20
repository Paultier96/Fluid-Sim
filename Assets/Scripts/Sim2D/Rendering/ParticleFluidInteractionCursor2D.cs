using System;
using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class ParticleFluidInteractionCursor2D : MonoBehaviour
	{
		public enum InteractionMode
		{
			Force,
			Temperature,
			Lighting
		}

		[Serializable]
		public struct CursorStateSettings
		{
			[ColorUsage(true, true)] public Color color;
			[Range(0.1f, 0.9f)] public float innerRingRadiusFraction;
			[Min(0f)] public float lightIntensity;
			[Range(1000f, 20000f)] public float lightTemperatureKelvin;
			[ColorUsage(false, true)] public Color lightColor;

			public CursorStateSettings(Color color, float innerRingRadiusFraction, float lightIntensity, float lightTemperatureKelvin, Color lightColor)
			{
				this.color = color;
				this.innerRingRadiusFraction = innerRingRadiusFraction;
				this.lightIntensity = lightIntensity;
				this.lightTemperatureKelvin = lightTemperatureKelvin;
				this.lightColor = lightColor;
			}
		}

		[Serializable]
		public struct CursorModeSettings
		{
			public CursorStateSettings hover;
			public CursorStateSettings primary;
			public CursorStateSettings secondary;

			public CursorModeSettings(CursorStateSettings hover, CursorStateSettings primary, CursorStateSettings secondary)
			{
				this.hover = hover;
				this.primary = primary;
				this.secondary = secondary;
			}
		}

		[Header("References")]
		public FluidSim2D sim;
		public Camera targetCamera;
		public ParticleFluidPointLight2D cursorLight;
		public ParticleFluidLighting2D presetTargetLighting;

		[Header("State Visuals")]
		public CursorModeSettings force = new(
			new CursorStateSettings(),
			new CursorStateSettings(),
			new CursorStateSettings());
		public CursorModeSettings temperature = new(
			new CursorStateSettings(),
			new CursorStateSettings(),
			new CursorStateSettings());
		public CursorModeSettings lighting = new(
			new CursorStateSettings(),
			new CursorStateSettings(),
			new CursorStateSettings());

		[Header("Behaviour")]
		public InteractionMode interactionMode = InteractionMode.Force;
		[Min(0f)] public float modeTransitionDuration = 0.12f;
		[Min(0f)] public float interactionRadius;
		public float interactionStrength;
		[Tooltip("How strongly particles inherit cursor velocity inside the interaction radius. 0 disables cursor stirring.")]
		[Min(0f)] public float cursorVelocityTransferStrength = 1f;
		[Tooltip("Radius of the cursor temperature brush. 0 uses the interaction radius.")]
		[Min(0f)] public float cursorTemperatureBrushRadius = 0f;
		[Tooltip("How quickly particles move toward the cursor brush target temperature.")]
		[Min(0f)] public float cursorTemperatureBrushTransferRate = 5f;
		public float cursorHeatBrushTemperature = 10f;
		public float cursorCoolBrushTemperature = -10f;

		[Header("Presets")]
		public ParticleFluidPhaseLookPreset[] phaseLookPresets;
		[Min(0)] public int activePhaseLookPresetIndex;
		public InputActionReference cyclePresetAction;

		[Header("Temperature Wave")]
		public float idleTemperatureWaveAmplitudeMultiplier = 1f;
		public float heatTemperatureWaveAmplitudeMultiplier = 1.7f;
		public float coolTemperatureWaveAmplitudeMultiplier = 0.35f;

		[Header("Gamepad")]
		public bool gamepadCursorEnabled = true;
		[Tooltip("World units per second at full stick deflection.")]
		[Min(0f)] public float gamepadCursorSpeed = 12f;

		private Mesh _mesh;
		private MeshFilter _meshFilter;
		private MeshRenderer _meshRenderer;
		private MaterialPropertyBlock _propertyBlock;
		private bool _hasGamepadCursorPosition;
		private Vector2 _gamepadCursorPosition;
		private Vector2 _gamepadCursorVelocity;
		public Vector2 currentWorldVelocity;
		private Vector2 _lastMouseWorldPosition;
		private bool _hasLastMouseWorldPosition;
		private bool _hasAnimatedVisualState;
		private Color _animatedColor;
		private float _animatedRadius;
		private float _animatedInnerRingRadiusFraction;
		private float _animatedInnerRotation;
		private float _targetInnerRotation;
		private float _animatedTemperatureWaveAmplitudeMultiplier = 1f;
		private float _animatedLightIntensity;
		private float _animatedLightTemperatureKelvin;
		private Color _animatedLightColor;

		private static readonly int ColorId = Shader.PropertyToID("_Color");
		private static readonly int RadiusId = Shader.PropertyToID("_Radius");
		private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
		private static readonly int CursorWorldRadiusId = Shader.PropertyToID("_CursorWorldRadius");
		private static readonly int ShadowSoftnessId = Shader.PropertyToID("_ShadowSoftness");
		private static readonly int UvScaleId = Shader.PropertyToID("_UvScale");
		private static readonly int InnerRadiusFractionId = Shader.PropertyToID("_InnerRadiusFraction");
		private static readonly int InnerRotationId = Shader.PropertyToID("_InnerRotation");
		private static readonly int InteractionId = Shader.PropertyToID("_Interaction");
		private static readonly int CursorFamilyId = Shader.PropertyToID("_CursorFamily");
		private static readonly int TemperatureWaveAmplitudeMultiplierId = Shader.PropertyToID("_TemperatureWaveAmplitudeMultiplier");
		

		private void Awake()
		{
			_meshFilter = GetComponent<MeshFilter>();
			_meshRenderer = GetComponent<MeshRenderer>();
			_propertyBlock = new MaterialPropertyBlock();

			if (_mesh == null)
			{
				_mesh = QuadGenerator.GenerateQuadMesh();
				_mesh.name = "Particle Fluid Interaction Cursor Quad";
				_meshFilter.sharedMesh = _mesh;
			}

			if (sim == null)
			{
				sim = FindAnyObjectByType<FluidSim2D>();
			}
		}

		private void OnEnable()
		{
			Actions.Player.switchMode.performed += RotateInteractionMode;
			if (cyclePresetAction != null)
			{
				cyclePresetAction.action.performed += CyclePhaseLookPreset;
				cyclePresetAction.action.Enable();
			}
		}

		private void OnDisable()
		{
			Actions.Player.switchMode.performed -= RotateInteractionMode;
			if (cyclePresetAction != null)
			{
				cyclePresetAction.action.performed -= CyclePhaseLookPreset;
			}
			_meshRenderer.enabled = false;
			_hasAnimatedVisualState = false;
			if (cursorLight != null)
			{
				cursorLight.intensity = 0f;
			}

			Cursor.visible = true;
		}

		private void LateUpdate()
		{
			if (Actions.Player.PointerDelta.ReadValue<Vector2>().sqrMagnitude > 0.0001f)
			{
				_hasGamepadCursorPosition = false;
			}

			if (!TryUpdateGamepadCursor(targetCamera, out Vector2 worldPosition, out Vector2 worldVelocity))
			{
				worldPosition = targetCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
				worldVelocity = CalculateMouseWorldVelocity(worldPosition);
			}

			float targetRadius;
			if (interactionMode == InteractionMode.Temperature)
			{
				targetRadius = cursorTemperatureBrushRadius > 0f ? cursorTemperatureBrushRadius : interactionRadius;
			}
			else
			{
				targetRadius = interactionRadius;
			}
			_meshRenderer.enabled = true;
			Cursor.visible = false;

			currentWorldVelocity = worldVelocity;
			transform.position =  worldPosition;
			float interaction = Actions.Player.Interact.ReadValue<float>();
			UpdateAnimatedVisualState(interaction, targetRadius);

			Material material = _meshRenderer.sharedMaterial;
			float uvScale = Mathf.Max(1f, material.GetFloat(RadiusId) + material.GetFloat(ShadowSoftnessId) + material.GetFloat(ThicknessId));
			float quadSize = _animatedRadius / Mathf.Max(_meshRenderer.sharedMaterial.GetFloat(RadiusId), 0.0001f) * uvScale * 2f;
			transform.localScale = new Vector3(quadSize, quadSize, 1f);

			ApplyMaterialProperties(interaction, _animatedRadius, uvScale);
			cursorLight.transform.position = worldPosition;
			cursorLight.intensity = _animatedLightIntensity;
			cursorLight.temperatureKelvin = _animatedLightTemperatureKelvin;
			cursorLight.color = _animatedLightColor;
		}

		private void RotateInteractionMode(InputAction.CallbackContext context)
		{
			int interactionModeCount = Enum.GetNames(typeof(InteractionMode)).Length;
			int input = (int)context.ReadValue<float>();
			int next = ((int)interactionMode + input + interactionModeCount) % interactionModeCount;
			interactionMode = (InteractionMode)next;
			_targetInnerRotation += input * Mathf.PI * 0.5f;
		}

		private void CyclePhaseLookPreset(InputAction.CallbackContext context)
		{
			int direction = Mathf.RoundToInt(context.ReadValue<float>());
			if (direction == 0)
			{
				direction = 1;
			}
			CyclePhaseLookPreset(direction);
		}

		public void CyclePhaseLookPreset(int direction)
		{
			if (phaseLookPresets == null || phaseLookPresets.Length == 0 || direction == 0)
			{
				return;
			}

			activePhaseLookPresetIndex = Mod(activePhaseLookPresetIndex + direction, phaseLookPresets.Length);
			ApplyActivePhaseLookPreset();
		}

		public void NextPhaseLookPreset()
		{
			CyclePhaseLookPreset(1);
		}

		public void PreviousPhaseLookPreset()
		{
			CyclePhaseLookPreset(-1);
		}

		public void ApplyActivePhaseLookPreset()
		{
			ParticleFluidLighting2D targetLighting = ResolvePresetTargetLighting();
			ParticleFluidPhaseLookPreset preset = GetActivePhaseLookPreset();
			if (targetLighting == null || preset == null)
			{
				return;
			}

			targetLighting.phaseLookPreset = preset;
			targetLighting.ApplyPhaseLookPreset();
		}

		private ParticleFluidPhaseLookPreset GetActivePhaseLookPreset()
		{
			if (phaseLookPresets == null || phaseLookPresets.Length == 0)
			{
				return null;
			}

			activePhaseLookPresetIndex = Mod(activePhaseLookPresetIndex, phaseLookPresets.Length);
			return phaseLookPresets[activePhaseLookPresetIndex];
		}

		private ParticleFluidLighting2D ResolvePresetTargetLighting()
		{
			if (presetTargetLighting != null)
			{
				return presetTargetLighting;
			}

			ParticleDisplay2D display = sim != null ? sim.GetComponent<ParticleDisplay2D>() : null;
			if (display != null && display.ActiveLighting != null)
			{
				presetTargetLighting = display.ActiveLighting;
			}
			else
			{
				presetTargetLighting = FindAnyObjectByType<ParticleFluidLighting2D>();
			}

			return presetTargetLighting;
		}

		private static int Mod(int value, int length)
		{
			return (value % length + length) % length;
		}

		private CursorStateSettings ResolveStateSettings(float interaction)
		{
			CursorModeSettings modeSettings = interactionMode switch
			{
				InteractionMode.Temperature => temperature,
				InteractionMode.Lighting => lighting,
				_ => force
			};

			CursorStateSettings activeSettings = interaction >= 0f
				? modeSettings.primary
				: modeSettings.secondary;

			return new CursorStateSettings(
				Color.Lerp(modeSettings.hover.color, activeSettings.color, Mathf.Abs(interaction)),
				Mathf.Lerp(modeSettings.hover.innerRingRadiusFraction, activeSettings.innerRingRadiusFraction, Mathf.Abs(interaction)),
				Mathf.Lerp(modeSettings.hover.lightIntensity, activeSettings.lightIntensity, Mathf.Abs(interaction)),
				Mathf.Lerp(modeSettings.hover.lightTemperatureKelvin, activeSettings.lightTemperatureKelvin, Mathf.Abs(interaction)),
				Color.Lerp(modeSettings.hover.lightColor, activeSettings.lightColor, Mathf.Abs(interaction)));
		}

		private float ResolveTemperatureWaveAmplitudeMultiplier(float interaction)
		{
			if (interactionMode != InteractionMode.Temperature)
			{
				return idleTemperatureWaveAmplitudeMultiplier;
			}

			float amount = Mathf.Abs(interaction);
			if (amount <= 0f)
			{
				return idleTemperatureWaveAmplitudeMultiplier;
			}

			float activeMultiplier = interaction >= 0f ? heatTemperatureWaveAmplitudeMultiplier : coolTemperatureWaveAmplitudeMultiplier;
			return Mathf.Lerp(idleTemperatureWaveAmplitudeMultiplier, activeMultiplier, amount);
		}

		private void UpdateAnimatedVisualState(float interaction, float targetRadius)
		{
			CursorStateSettings targetSettings = ResolveStateSettings(interaction);
			float targetTemperatureWaveAmplitudeMultiplier = ResolveTemperatureWaveAmplitudeMultiplier(interaction);
			if (!_hasAnimatedVisualState || modeTransitionDuration <= 0f)
			{
				_animatedColor = targetSettings.color;
				_animatedRadius = targetRadius;
				_animatedInnerRingRadiusFraction = targetSettings.innerRingRadiusFraction;
				_animatedInnerRotation = _targetInnerRotation;
				_animatedTemperatureWaveAmplitudeMultiplier = targetTemperatureWaveAmplitudeMultiplier;
				_animatedLightIntensity = targetSettings.lightIntensity;
				_animatedLightTemperatureKelvin = targetSettings.lightTemperatureKelvin;
				_animatedLightColor = targetSettings.lightColor;
				_hasAnimatedVisualState = true;
				return;
			}

			float t = 1f - Mathf.Exp(-Time.deltaTime / modeTransitionDuration);
			_animatedColor = Color.Lerp(_animatedColor, targetSettings.color, t);
			_animatedRadius = Mathf.Lerp(_animatedRadius, targetRadius, t);
			_animatedInnerRingRadiusFraction = Mathf.Lerp(_animatedInnerRingRadiusFraction, targetSettings.innerRingRadiusFraction, t);
			_animatedInnerRotation = Mathf.Lerp(_animatedInnerRotation, _targetInnerRotation, t);
			_animatedTemperatureWaveAmplitudeMultiplier = Mathf.Lerp(_animatedTemperatureWaveAmplitudeMultiplier, targetTemperatureWaveAmplitudeMultiplier, t);
			_animatedLightIntensity = Mathf.Lerp(_animatedLightIntensity, targetSettings.lightIntensity, t);
			_animatedLightTemperatureKelvin = Mathf.Lerp(_animatedLightTemperatureKelvin, targetSettings.lightTemperatureKelvin, t);
			_animatedLightColor = Color.Lerp(_animatedLightColor, targetSettings.lightColor, t);
		}

		private void ApplyMaterialProperties(float interaction, float radius, float uvScale)
		{
			_propertyBlock ??= new MaterialPropertyBlock();
			_meshRenderer.GetPropertyBlock(_propertyBlock);
			_propertyBlock.SetColor(ColorId, _animatedColor);
			_propertyBlock.SetFloat(CursorWorldRadiusId, radius);
			_propertyBlock.SetFloat(UvScaleId, uvScale);
			_propertyBlock.SetFloat(InnerRadiusFractionId, _animatedInnerRingRadiusFraction);
			_propertyBlock.SetFloat(InnerRotationId, _animatedInnerRotation);
			_propertyBlock.SetFloat(InteractionId, interaction);
			_propertyBlock.SetInt(CursorFamilyId, interactionMode switch
			{
				InteractionMode.Force => 1,
				InteractionMode.Temperature => 2,
				InteractionMode.Lighting => 3,
				_ => 0
			});
			_propertyBlock.SetFloat(TemperatureWaveAmplitudeMultiplierId, _animatedTemperatureWaveAmplitudeMultiplier);
			_meshRenderer.SetPropertyBlock(_propertyBlock);
		}

		private bool TryUpdateGamepadCursor(Camera cam, out Vector2 worldPosition, out Vector2 worldVelocity)
		{
			worldPosition = default;
			worldVelocity = default;
			if (!gamepadCursorEnabled)
			{
				return false;
			}

			Vector2 cursorMove = Actions.Player.GamepadCursor.ReadValue<Vector2>();
			
			if (cursorMove.sqrMagnitude <= 0f)
			{
				if (_hasGamepadCursorPosition)
				{
					_hasLastMouseWorldPosition = false;
					worldPosition = _gamepadCursorPosition;
					worldVelocity = Vector2.zero;
					return true;
				}

				return false;
			}

			if (!_hasGamepadCursorPosition)
			{
				_gamepadCursorPosition = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
				_hasGamepadCursorPosition = true;
			}

			_gamepadCursorVelocity = cursorMove * gamepadCursorSpeed;
			_gamepadCursorPosition += _gamepadCursorVelocity * Time.deltaTime;

			Vector3 viewport = cam.WorldToViewportPoint(_gamepadCursorPosition);

			viewport.x = Mathf.Clamp01(viewport.x);
			viewport.y = Mathf.Clamp01(viewport.y);
		
			_gamepadCursorPosition = cam.ViewportToWorldPoint(viewport);

			worldPosition = _gamepadCursorPosition;
			worldVelocity = _gamepadCursorVelocity;
			_hasLastMouseWorldPosition = false;
			return true;
		}

		private Vector2 CalculateMouseWorldVelocity(Vector2 worldPosition)
		{
			Vector2 velocity = Vector2.zero;
			if (_hasLastMouseWorldPosition && Time.deltaTime > 0f)
			{
				velocity = (worldPosition - _lastMouseWorldPosition) / Time.deltaTime;
			}

			_lastMouseWorldPosition = worldPosition;
			_hasLastMouseWorldPosition = true;
			return velocity;
		}

		private static InputSystem_Actions _actions;

		public static InputSystem_Actions Actions
		{
			get
			{
				_actions ??= new InputSystem_Actions();

				if (!_actions.Player.enabled)
				{
					_actions.Player.Enable();
				}
				return _actions;
			}
		}
		
		
		public readonly struct Interaction
		{
			public readonly float strength;
			public readonly bool heatBrushActive;
			public readonly float heatBrushTargetTemperature;
			public readonly float heatBrushStrength;

			public Interaction(float strength, bool heatBrushActive, float heatBrushTargetTemperature, float heatBrushStrength)
			{
				this.strength = strength;
				this.heatBrushActive = heatBrushActive;
				this.heatBrushTargetTemperature = heatBrushTargetTemperature;
				this.heatBrushStrength = heatBrushStrength;
			}
		}
		
		public Interaction PollInteraction()
		{
			float cursorStrength = 0f;
			float cursorTargetTemperature = 0;
			float cursorTemperatureStrength = 0f;
			bool isTemperatureActive = false;
			float interaction = Mathf.Clamp(Actions.Player.Interact.ReadValue<float>(), -1f, 1f);

			if (interactionMode == InteractionMode.Force)
			{
				cursorStrength = interaction * interactionStrength;
			}
			else if (interactionMode == InteractionMode.Temperature)
			{
				isTemperatureActive = Mathf.Abs(interaction) > 0.01f;
				cursorTemperatureStrength = Mathf.Abs(interaction);
				cursorTargetTemperature = interaction >= 0f ? cursorHeatBrushTemperature : cursorCoolBrushTemperature;
			}
			return new Interaction(cursorStrength, isTemperatureActive, cursorTargetTemperature, cursorTemperatureStrength);
		}
	}
}
