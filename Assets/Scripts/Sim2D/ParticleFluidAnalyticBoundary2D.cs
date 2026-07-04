using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
	[DisallowMultipleComponent]
	public sealed class ParticleFluidAnalyticBoundary2D : MonoBehaviour
	{
		[Header("Bounds Type")]
		public bool useEllipticalBounds = false;
		public Vector2 ellipseBoundsSize = new(10f, 8f);
		public Vector2 ellipseBoundsCenter = Vector2.zero;

		[Header("Boundary Shape")]
		[Tooltip("Horizontal lower boundary used with elliptical bounds. The final fluid domain is the ellipse above this Y value.")]
		public float obstacleY = -10f;
		[Tooltip("World-space expansion applied to the analytic boundary. 0 uses the original, non-expanded analytic boundary.")]
		[Min(0f)] public float analyticBoundaryExpansion = 0.175f;

		public Vector2 Radii => new Vector2(Mathf.Abs(ellipseBoundsSize.x), Mathf.Abs(ellipseBoundsSize.y)) + Vector2.one * analyticBoundaryExpansion;
		public float CutY => obstacleY - analyticBoundaryExpansion;
		public Vector2 BoundsMin => new(ellipseBoundsCenter.x - Radii.x, Mathf.Max(ellipseBoundsCenter.y - Radii.y, CutY));
		public Vector2 BoundsMax => ellipseBoundsCenter + Radii;

		public bool Contains(Vector2 point)
		{
			if (point.y < CutY)
			{
				return false;
			}

			Vector2 rel = point - ellipseBoundsCenter;
			float ellipseValue = rel.x * rel.x / (Radii.x * Radii.x) + rel.y * rel.y / (Radii.y * Radii.y);
			return ellipseValue <= 1f;
		}

		void OnDrawGizmos()
		{
			if (!useEllipticalBounds)
			{
				return;
			}

			Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
			DrawEllipseGizmo(ellipseBoundsCenter, ellipseBoundsSize, 128);

			Gizmos.color = new Color(1f, 0.65f, 0f, 0.8f);
			DrawHorizontalBoundaryLineGizmo();
		}

		static void DrawEllipseGizmo(Vector2 center, Vector2 radii, int segments)
		{
			float angleStep = 360f / segments;
			Vector3 lastPoint = center + new Vector2(radii.x, 0f);
			for (int i = 1; i <= segments; i++)
			{
				float angle = i * angleStep * Mathf.Deg2Rad;
				Vector3 newPoint = center + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
				Gizmos.DrawLine(lastPoint, newPoint);
				lastPoint = newPoint;
			}
		}

		void DrawHorizontalBoundaryLineGizmo()
		{
			float relY = obstacleY - ellipseBoundsCenter.y;
			float radiusY = Mathf.Max(ellipseBoundsSize.y, 0.0001f);
			float normalizedY = relY / radiusY;
			if (Mathf.Abs(normalizedY) >= 1f)
			{
				return;
			}

			float halfWidth = ellipseBoundsSize.x * Mathf.Sqrt(1f - normalizedY * normalizedY);
			Vector3 left = new(ellipseBoundsCenter.x - halfWidth, obstacleY, 0f);
			Vector3 right = new(ellipseBoundsCenter.x + halfWidth, obstacleY, 0f);
			Gizmos.DrawLine(left, right);
		}
	}
}
