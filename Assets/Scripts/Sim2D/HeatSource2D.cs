using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class HeatSource2D : MonoBehaviour
    {
        public enum HeatSourceShape { Rectangular, Elliptical }
        
        public HeatSourceShape shape = HeatSourceShape.Elliptical;
        public float temperature = 100f;
        [Min(0f)] public float transferRate = 1f;
        [Min(0.01f)] public float falloffPower = 1f;
        private Color gizmoColor = new Color(1f, 0.35f, 0f, 0.5f);

        public Vector2 Position => transform.position;
        public Vector2 Size => transform.lossyScale;

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Vector2 size = Size;
            
            if (shape == HeatSourceShape.Rectangular)
            {
                if (size.x <= 0f || size.y <= 0f)
                    return;
                
                Vector2 halfSize = size * 0.5f;
                Vector3 topLeft = transform.position + new Vector3(-halfSize.x, halfSize.y, 0);
                Vector3 topRight = transform.position + new Vector3(halfSize.x, halfSize.y, 0);
                Vector3 bottomLeft = transform.position + new Vector3(-halfSize.x, -halfSize.y, 0);
                Vector3 bottomRight = transform.position + new Vector3(halfSize.x, -halfSize.y, 0);
                
                Gizmos.DrawLine(topLeft, topRight);
                Gizmos.DrawLine(topRight, bottomRight);
                Gizmos.DrawLine(bottomRight, bottomLeft);
                Gizmos.DrawLine(bottomLeft, topLeft);
            }
            else if (shape == HeatSourceShape.Elliptical)
            {
                if (size.x <= 0f || size.y <= 0f)
                    return;

                const int segments = 64;
                Vector2 radii = size * 0.5f;
                Vector3 previous = transform.position + new Vector3(radii.x, 0f, 0f);
                for (int i = 1; i <= segments; i++)
                {
                    float angle = i / (float)segments * Mathf.PI * 2f;
                    Vector3 current = transform.position + new Vector3(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y, 0f);
                    Gizmos.DrawLine(previous, current);
                    previous = current;
                }
            }
        }
    }
}
