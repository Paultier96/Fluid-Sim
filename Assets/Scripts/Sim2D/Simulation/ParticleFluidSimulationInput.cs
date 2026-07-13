using UnityEngine;
using UnityEngine.InputSystem;
using Seb.Fluid2D.Rendering;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationInput
    {
        public readonly struct Commands
        {
            public readonly bool togglePause;
            public readonly bool stepFrame;
            public readonly bool reset;

            public Commands(bool togglePause, bool stepFrame, bool reset)
            {
                this.togglePause = togglePause;
                this.stepFrame = stepFrame;
                this.reset = reset;
            }
        }

        public readonly struct Interaction
        {
            public readonly Vector2 position;
            public readonly Vector2 velocity;
            public readonly float strength;
            public readonly bool heatBrushActive;
            public readonly float heatBrushTargetTemperature;
            public readonly float heatBrushStrength;

            public Interaction(Vector2 position, Vector2 velocity, float strength, bool heatBrushActive, float heatBrushTargetTemperature, float heatBrushStrength)
            {
                this.position = position;
                this.velocity = velocity;
                this.strength = strength;
                this.heatBrushActive = heatBrushActive;
                this.heatBrushTargetTemperature = heatBrushTargetTemperature;
                this.heatBrushStrength = heatBrushStrength;
            }
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

        private bool _hasPreviousMouseWorldPosition;
        private Vector2 _previousMouseWorldPosition;

        public static float ReadInteractionAxis()
        {
            return Mathf.Clamp(Actions.Player.Interact.ReadValue<float>(), -1f, 1f);
        }

        public static bool IsInteractionActive(float value)
        {
            return Mathf.Abs(value) > 0.01f;
        }

        public Commands PollCommands()
        {
            return new Commands(
                Actions.Player.Pause.WasPressedThisFrame(),
                Keyboard.current != null && Keyboard.current[Key.RightArrow].wasPressedThisFrame,
                Keyboard.current != null && Keyboard.current[Key.R].wasPressedThisFrame);
        }

        public Interaction PollInteraction(
            ParticleDisplay2D display,
            float interactionStrength,
            float heatBrushTemperature,
            float coolBrushTemperature)
        {
            ParticleFluidInteractionCursor2D cursor = display.interactionCursor;
            float cursorStrength = 0f;
            float cursorTargetTemperature = 0;
            float cursorTemperatureStrength = 0f;
            bool isTemperatureActive = false;
            float interaction = ReadInteractionAxis();

            if (cursor.interactionMode == ParticleFluidInteractionCursor2D.InteractionMode.Force)
            {
                cursorStrength = interaction * interactionStrength;
            }
            else if (cursor.interactionMode == ParticleFluidInteractionCursor2D.InteractionMode.Temperature)
            {
                isTemperatureActive = IsInteractionActive(interaction);
                cursorTemperatureStrength = Mathf.Abs(interaction);
                cursorTargetTemperature = interaction >= 0f ? heatBrushTemperature : coolBrushTemperature;
            }
            
            return new Interaction(cursor.transform.position, cursor.currentWorldVelocity, cursorStrength, isTemperatureActive, cursorTargetTemperature, cursorTemperatureStrength);
        }
    }
}
