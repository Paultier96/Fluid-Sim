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
			Temperature
		}

		[System.Serializable]
		public struct CursorLightSettings
		{
			[Min(0f)] public float intensity;
			[Range(1000f, 20000f)] public float temperatureKelvin;
			[ColorUsage(false, true)] public Color color;

			public CursorLightSettings(float intensity, float temperatureKelvin, Color color)
			{
				this.intensity = intensity;
				this.temperatureKelvin = temperatureKelvin;
				this.color = color;
			}
		}

		[System.Serializable]
		public struct CursorStateSettings
		{
			[ColorUsage(false, true)] public Color color;
			[Range(0.1f, 0.9f)] public float innerRingRadiusFraction;
			public CursorLightSettings light;

			public CursorStateSettings(Color color, float innerRingRadiusFraction, CursorLightSettings light)
			{
				this.color = color;
				this.innerRingRadiusFraction = innerRingRadiusFraction;
				this.light = light;
			}
		}

		[System.Serializable]
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

		[Header("State Visuals")]
		public CursorModeSettings force = new(
			new CursorStateSettings(new Color(1f, 1f, 1f, 0.45f), 0.5f, new CursorLightSettings(0.15f, 6500f, Color.white)),
			new CursorStateSettings(new Color(0.35f, 0.65f, 1f, 0.85f), 0.45f, new CursorLightSettings(0.35f, 9000f, Color.white)),
			new CursorStateSettings(new Color(1f, 0.55f, 0.2f, 0.85f), 0.6f, new CursorLightSettings(0.35f, 3200f, Color.white)));
		public CursorModeSettings temperature = new(
			new CursorStateSettings(new Color(1f, 1f, 1f, 0.45f), 0.5f, new CursorLightSettings(0.15f, 6500f, Color.white)),
			new CursorStateSettings(new Color(1f, 0.2f, 0.05f, 0.9f), 0.35f, new CursorLightSettings(0.65f, 1800f, Color.white)),
			new CursorStateSettings(new Color(0.2f, 0.85f, 1f, 0.9f), 0.7f, new CursorLightSettings(0.65f, 14000f, Color.white)));

		[Header("Behaviour")]
		public InteractionMode interactionMode = InteractionMode.Force;
		[Min(0f)] public float minimumVisibleRadius = 0.001f;
		[Min(0f)] public float modeTransitionDuration = 0.12f;

		[Header("Temperature Wave")]
		[Min(0f)] public float idleTemperatureWaveAmplitudeMultiplier = 1f;
		[Min(0f)] public float heatTemperatureWaveAmplitudeMultiplier = 1.7f;
		[Min(0f)] public float coolTemperatureWaveAmplitudeMultiplier = 0.35f;

		[Header("Gamepad")]
		public bool gamepadCursorEnabled = true;
		[Tooltip("World units per second at full stick deflection.")]
		[Min(0f)] public float gamepadCursorSpeed = 12f;
		public bool clampGamepadCursorToCamera = true;

		Mesh _mesh;
		MeshFilter _meshFilter;
		MeshRenderer _meshRenderer;
		MaterialPropertyBlock _propertyBlock;
		bool _hasGamepadCursorPosition;
		Vector2 _gamepadCursorPosition;
		Vector2 _gamepadCursorVelocity;
		public Vector2 currentWorldVelocity;
		Vector2 _lastMouseScreenPosition;
		bool _hasLastMouseScreenPosition;
		Vector2 _lastMouseWorldPosition;
		bool _hasLastMouseWorldPosition;
		bool _hasAnimatedVisualState;
		Color _animatedColor;
		float _animatedInnerRingRadiusFraction;
		float _animatedTemperatureWaveAmplitudeMultiplier = 1f;
		CursorLightSettings _animatedLightSettings;

		static readonly int ColorId = Shader.PropertyToID("_Color");
		static readonly int RadiusId = Shader.PropertyToID("_Radius");
		static readonly int CursorWorldRadiusId = Shader.PropertyToID("_CursorWorldRadius");
		static readonly int InnerRadiusFractionId = Shader.PropertyToID("_InnerRadiusFraction");
		static readonly int ModeId = Shader.PropertyToID("_Mode");
		static readonly int CursorFamilyId = Shader.PropertyToID("_CursorFamily");
		static readonly int TemperatureWaveAmplitudeMultiplierId = Shader.PropertyToID("_TemperatureWaveAmplitudeMultiplier");
		
		private InputSystem_Actions _actions;

		void Awake()
		{
			_actions = ParticleFluidSimulationInput.Actions;
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

		void OnEnable()
		{
			_actions.Player.switchMode.performed += ToggleInteractionMode;
		}

		void OnDisable()
		{
			_actions.Player.switchMode.performed -= ToggleInteractionMode;
			if (_meshRenderer != null)
			{
				_meshRenderer.enabled = false;
			}

			_hasAnimatedVisualState = false;
			DisableCursorLight();
			Cursor.visible = true;
		}

		void LateUpdate()
		{
			if (!TryResolveCursorWorldPosition(targetCamera, out Vector2 worldPosition, out Vector2 worldVelocity))
			{
				SetVisible(false);
				return;
			}

			float interaction = ParticleFluidSimulationInput.ReadInteractionAxis();
			float radius = ResolveRadius();
			if (radius <= minimumVisibleRadius)
			{
				SetVisible(false);
				return;
			}

			SetVisible(true);
			currentWorldVelocity = worldVelocity;
			transform.position = new Vector3(worldPosition.x, worldPosition.y, 0);
			float quadSize = radius / GetMaterialCursorRadius() * 2f;
			transform.localScale = new Vector3(quadSize, quadSize, 1f);

			UpdateAnimatedVisualState(interaction);
			ApplyMaterialProperties(interaction, radius);
			ApplyCursorLight(worldPosition);
		}

		void ToggleInteractionMode(InputAction.CallbackContext context)
		{
			interactionMode = interactionMode == InteractionMode.Force
				? InteractionMode.Temperature
				: InteractionMode.Force;
		}

		float ResolveRadius()
		{
			if (interactionMode == InteractionMode.Temperature)
			{
				return sim.cursorTemperatureBrushRadius > 0f ? sim.cursorTemperatureBrushRadius : sim.interactionRadius;
			}
			return sim.interactionRadius;
		}

		CursorStateSettings ResolveStateSettings(float interaction)
		{
			CursorModeSettings modeSettings = interactionMode == InteractionMode.Temperature
				? temperature
				: force;

			float amount = Mathf.Abs(interaction);
			if (amount <= 0f)
			{
				return modeSettings.hover;
			}

			CursorStateSettings activeSettings = interaction >= 0f
				? modeSettings.primary
				: modeSettings.secondary;
			return LerpStateSettings(modeSettings.hover, activeSettings, amount);
		}

		float ResolveTemperatureWaveAmplitudeMultiplier(float interaction)
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

			float activeMultiplier = interaction >= 0f
				? heatTemperatureWaveAmplitudeMultiplier
				: coolTemperatureWaveAmplitudeMultiplier;
			return Mathf.Lerp(idleTemperatureWaveAmplitudeMultiplier, activeMultiplier, amount);
		}

		int ResolveShaderMode(float interaction)
		{
			bool primary = interaction > 0.01f;
			bool secondary = interaction < -0.01f;
			return interactionMode switch
			{
				InteractionMode.Force when primary => 1,
				InteractionMode.Force when secondary => 2,
				InteractionMode.Temperature when primary => 3,
				InteractionMode.Temperature when secondary => 4,
				_ => 0
			};
		}

		void UpdateAnimatedVisualState(float interaction)
		{
			CursorStateSettings targetSettings = ResolveStateSettings(interaction);
			float targetTemperatureWaveAmplitudeMultiplier = ResolveTemperatureWaveAmplitudeMultiplier(interaction);
			if (!_hasAnimatedVisualState || modeTransitionDuration <= 0f)
			{
				_animatedColor = targetSettings.color;
				_animatedInnerRingRadiusFraction = targetSettings.innerRingRadiusFraction;
				_animatedTemperatureWaveAmplitudeMultiplier = targetTemperatureWaveAmplitudeMultiplier;
				_animatedLightSettings = targetSettings.light;
				_hasAnimatedVisualState = true;
				return;
			}

			float t = 1f - Mathf.Exp(-Time.deltaTime / modeTransitionDuration);
			_animatedColor = Color.Lerp(_animatedColor, targetSettings.color, t);
			_animatedInnerRingRadiusFraction = Mathf.Lerp(_animatedInnerRingRadiusFraction, targetSettings.innerRingRadiusFraction, t);
			_animatedTemperatureWaveAmplitudeMultiplier = Mathf.Lerp(_animatedTemperatureWaveAmplitudeMultiplier, targetTemperatureWaveAmplitudeMultiplier, t);
			_animatedLightSettings = LerpLightSettings(_animatedLightSettings, targetSettings.light, t);
		}

		static CursorLightSettings LerpLightSettings(CursorLightSettings current, CursorLightSettings target, float t)
		{
			return new CursorLightSettings(
				Mathf.Lerp(current.intensity, target.intensity, t),
				Mathf.Lerp(current.temperatureKelvin, target.temperatureKelvin, t),
				Color.Lerp(current.color, target.color, t));
		}

		static CursorStateSettings LerpStateSettings(CursorStateSettings current, CursorStateSettings target, float t)
		{
			return new CursorStateSettings(
				Color.Lerp(current.color, target.color, t),
				Mathf.Lerp(current.innerRingRadiusFraction, target.innerRingRadiusFraction, t),
				LerpLightSettings(current.light, target.light, t));
		}

		void ApplyMaterialProperties(float interaction, float radius)
		{
			_propertyBlock ??= new MaterialPropertyBlock();
			_meshRenderer.GetPropertyBlock(_propertyBlock);
			_propertyBlock.SetColor(ColorId, _animatedColor);
			_propertyBlock.SetFloat(CursorWorldRadiusId, radius);
			_propertyBlock.SetFloat(InnerRadiusFractionId, _animatedInnerRingRadiusFraction);
			_propertyBlock.SetInt(ModeId, ResolveShaderMode(interaction));
			_propertyBlock.SetInt(CursorFamilyId, interactionMode switch
			{
				InteractionMode.Force => 1,
				InteractionMode.Temperature => 2,
				_ => 0
			});
			_propertyBlock.SetFloat(TemperatureWaveAmplitudeMultiplierId, _animatedTemperatureWaveAmplitudeMultiplier);
			_meshRenderer.SetPropertyBlock(_propertyBlock);
		}

		float GetMaterialCursorRadius()
		{
			Material material = _meshRenderer != null ? _meshRenderer.sharedMaterial : null;
			if (material != null && material.HasProperty(RadiusId))
			{
				return Mathf.Max(material.GetFloat(RadiusId), 0.0001f);
			}

			return 0.85f;
		}

		void ApplyCursorLight(Vector2 worldPosition)
		{
			if (cursorLight == null)
			{
				return;
			}

			Vector3 lightPosition = cursorLight.transform.position;
			lightPosition.x = worldPosition.x;
			lightPosition.y = worldPosition.y;
			cursorLight.transform.position = lightPosition;
			cursorLight.intensity = _animatedLightSettings.intensity;
			cursorLight.temperatureKelvin = _animatedLightSettings.temperatureKelvin;
			cursorLight.color = _animatedLightSettings.color;
		}

		bool TryResolveCursorWorldPosition(Camera cam, out Vector2 worldPosition, out Vector2 worldVelocity)
		{
			if (MouseMovedSinceLastFrame())
			{
				_hasGamepadCursorPosition = false;
			}

			if (TryUpdateGamepadCursor(cam, out worldPosition, out worldVelocity))
			{
				return true;
			}

			if (TryGetMouseWorldPosition(cam, out worldPosition))
			{
				worldVelocity = CalculateMouseWorldVelocity(worldPosition);
				return true;
			}

			_hasLastMouseWorldPosition = false;
			worldVelocity = default;
			return false;
		}

		bool MouseMovedSinceLastFrame()
		{
			if (Mouse.current == null)
			{
				_hasLastMouseScreenPosition = false;
				return false;
			}

			Vector2 mousePosition = Mouse.current.position.ReadValue();
			bool moved = _hasLastMouseScreenPosition && (mousePosition - _lastMouseScreenPosition).sqrMagnitude > 0.01f;
			_lastMouseScreenPosition = mousePosition;
			_hasLastMouseScreenPosition = true;
			return moved;
		}

		bool TryUpdateGamepadCursor(Camera cam, out Vector2 worldPosition, out Vector2 worldVelocity)
		{
			worldPosition = default;
			worldVelocity = default;
			if (!gamepadCursorEnabled)
			{
				return false;
			}

			Vector2 cursorMove = GetVirtualCursorMove();
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
				_gamepadCursorPosition = TryGetMouseWorldPosition(cam, out Vector2 mouseWorldPosition)
					? mouseWorldPosition
					: cam.transform.position;
				_hasGamepadCursorPosition = true;
			}

			_gamepadCursorVelocity = cursorMove * gamepadCursorSpeed;
			_gamepadCursorPosition += _gamepadCursorVelocity * Time.deltaTime;
			if (clampGamepadCursorToCamera)
			{
				_gamepadCursorPosition = ClampToCameraView(cam, _gamepadCursorPosition);
			}

			worldPosition = _gamepadCursorPosition;
			worldVelocity = _gamepadCursorVelocity;
			_hasLastMouseWorldPosition = false;
			return true;
		}

		Vector2 GetVirtualCursorMove()
		{
			InputAction cursorAction = ParticleFluidSimulationInput.Actions.Player.Cursor;
			return cursorAction.activeControl?.device is Gamepad or Keyboard
				? cursorAction.ReadValue<Vector2>()
				: Vector2.zero;
		}

		Vector2 CalculateMouseWorldVelocity(Vector2 worldPosition)
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

		Vector2 ClampToCameraView(Camera cam, Vector2 worldPosition)
		{
			float halfHeight = cam.orthographicSize;
			float halfWidth = halfHeight * cam.aspect;
			Vector3 center = cam.transform.position;
			return new Vector2(
				Mathf.Clamp(worldPosition.x, center.x - halfWidth, center.x + halfWidth),
				Mathf.Clamp(worldPosition.y, center.y - halfHeight, center.y + halfHeight));
		}

		bool TryGetMouseWorldPosition(Camera cam, out Vector2 worldPosition)
		{
			worldPosition = default;
			Vector3 mousePosition = Mouse.current.position.ReadValue();
			if (mousePosition.x < 0f || mousePosition.y < 0f || mousePosition.x > cam.pixelWidth || mousePosition.y > cam.pixelHeight)
			{
				return false;
			}

			Vector2 screenPoint = new(mousePosition.x, mousePosition.y);
			Vector3 world = cam.ScreenToWorldPoint(screenPoint);
			worldPosition = world;
			return float.IsFinite(worldPosition.x) && float.IsFinite(worldPosition.y);
		}

		void SetVisible(bool visible)
		{
			if (_meshRenderer != null)
			{
				_meshRenderer.enabled = visible;
			}

			Cursor.visible = !visible;
			if (!visible)
			{
				_hasAnimatedVisualState = false;
				DisableCursorLight();
			}
		}

		void DisableCursorLight()
		{
			if (cursorLight != null)
			{
				cursorLight.intensity = 0f;
			}
		}

	}
}
