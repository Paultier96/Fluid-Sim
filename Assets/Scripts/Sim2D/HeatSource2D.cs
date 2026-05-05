using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class HeatSource2D : MonoBehaviour
    {
        public enum HeatSourceShape { Circular, Rectangular }
        
        public HeatSourceShape shape = HeatSourceShape.Circular;
        [Min(0f)] public float radius = 2f;
        public Vector2 size = new Vector2(4f, 2f);
        public float temperature = 100f;
        private Color gizmoColor = new Color(1f, 0.35f, 0f, 0.5f);

        public Vector2 Position => transform.position;

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            
            if (shape == HeatSourceShape.Circular)
            {
                if (radius <= 0f)
                    return;
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else if (shape == HeatSourceShape.Rectangular)
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
        }
    }
}
