using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationInput
    {
        public Camera cam;

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
            public readonly bool isActive;
            public readonly bool isPull;
            public readonly bool isPush;
            public readonly Vector2 position;
            public readonly float strength;

            public Interaction(bool isActive, bool isPull, bool isPush, Vector2 position, float strength)
            {
                this.isActive = isActive;
                this.isPull = isPull;
                this.isPush = isPush;
                this.position = position;
                this.strength = strength;
            }
        }

        public Commands PollCommands()
        {
            return new Commands(
                Input.GetKeyDown(KeyCode.Space),
                Input.GetKeyDown(KeyCode.RightArrow),
                Input.GetKeyDown(KeyCode.R));
        }

        public Interaction PollInteraction(float interactionStrength)
        {
            bool isPull = Input.GetMouseButton(0);
            bool isPush = Input.GetMouseButton(1);
            if ((isPush || isPull) && TryGetMouseWorldPosition(out Vector2 mousePosition))
            {
                float strength = isPush ? -interactionStrength : interactionStrength;
                return new Interaction(true, isPull, isPush, mousePosition, strength);
            }

            return default;
        }

        bool TryGetMouseWorldPosition(out Vector2 mouseWorldPosition)
        {
            mouseWorldPosition = Vector2.zero;

            Vector3 mousePosition = Input.mousePosition;
            if (!float.IsFinite(mousePosition.x) || !float.IsFinite(mousePosition.y) || !float.IsFinite(mousePosition.z))
            {
                return false;
            }

            if (mousePosition.x < 0 || mousePosition.y < 0 || mousePosition.x > cam.pixelWidth || mousePosition.y > cam.pixelHeight)
            {
                return false;
            }

            Vector3 worldPosition = cam.ScreenToWorldPoint(mousePosition);
            if (!float.IsFinite(worldPosition.x) || !float.IsFinite(worldPosition.y))
            {
                return false;
            }

            mouseWorldPosition = worldPosition;
            return true;
        }
    }
}
