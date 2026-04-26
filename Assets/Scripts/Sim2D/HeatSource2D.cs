using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class HeatSource2D : MonoBehaviour
    {
        [Min(0f)] public float radius = 2f;
        public float temperature = 100f;
        private Color gizmoColor = new Color(1f, 0.35f, 0f, 0.5f);

        public Vector2 Position => transform.position;

        void OnDrawGizmos()
        {
            if (radius <= 0f)
                return;

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
