using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(RectTransform))]
	public class ParticleFluidAnalyticBoundary2D : MonoBehaviour
	{
		[Header("Bounds Type")]
		public bool useEllipticalBounds = true;

		public Vector2 boundsSize => ((RectTransform)transform).rect.size;
		public Vector2 BoundsCenter => transform.position;

		[Header("Boundary Shape")]
		[Tooltip("Horizontal lower boundary used with elliptical bounds. The final fluid domain is the ellipse above this Y value.")]
		public float obstacleY = -10f;
		[Tooltip("World-space expansion applied to the analytic boundary. 0 uses the original, non-expanded analytic boundary.")]
		[Min(0f)] public float analyticBoundaryExpansion = 0.175f;
		
		public Bounds CropBounds { get; private set; }
		public Vector2 EllipseRadii => new Vector2(Mathf.Abs(boundsSize.x), Mathf.Abs(boundsSize.y)) * 0.5f;
		private Vector2 Radii => EllipseRadii + Vector2.one * analyticBoundaryExpansion;
		private float CutY => obstacleY - analyticBoundaryExpansion;

		private void Awake()
		{
			CropBounds = CalculateCropBounds();
		}

		private void OnValidate()
		{
			CropBounds = CalculateCropBounds();
		}

		Bounds CalculateCropBounds()
		{
			Bounds cropBounds = new Bounds();
			if (useEllipticalBounds)
			{
				Vector2 radii = EllipseRadii + Vector2.one * analyticBoundaryExpansion;
				cropBounds.SetMinMax(
					new Vector2(BoundsCenter.x - radii.x, Mathf.Max(BoundsCenter.y - radii.y, CutY)),
					BoundsCenter + radii
				);
			}
			else
			{
				Vector2 halfSize = (boundsSize + Vector2.one * analyticBoundaryExpansion * 2f) * 0.5f;
				cropBounds.SetMinMax(-halfSize, halfSize);
			}

			return cropBounds;
		}

		public bool TryGetEllipsePoint(float angle, out Vector2 boundaryPoint)
		{
			boundaryPoint = BoundsCenter + new Vector2(Mathf.Cos(angle) * Radii.x, Mathf.Sin(angle) * Radii.y);
			return IsAboveCut(boundaryPoint);
		}

		private bool IsAboveCut(Vector2 point)
		{
			return point.y >= CutY;
		}

		private bool IsInsideEllipse(Vector2 point)
		{
			Vector2 rel = point - BoundsCenter;
			Vector2 radii = Radii;

			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			return ellipseValue <= 1.0001f;
		}

		public bool Contains(Vector2 point)
		{
			return IsAboveCut(point) && IsInsideEllipse(point);
		}

		private Vector2 GetEllipseNormal(Vector2 point)
		{
			Vector2 rel = point - BoundsCenter;
			Vector2 radii = Radii;
			return new Vector2(rel.x / (radii.x * radii.x), rel.y / (radii.y * radii.y)).normalized;
		}
		
		public bool TryGetCutSegment(out Vector2 left, out Vector2 right)
		{
			float cutRelY = (CutY - BoundsCenter.y) / Radii.y;
			if (Mathf.Abs(cutRelY) > 1f)
			{
				left = right = default;
				return false;
			}

			float halfWidth = Radii.x * Mathf.Sqrt(1f - cutRelY * cutRelY);

			left = new Vector2(BoundsCenter.x - halfWidth, CutY);
			right = new Vector2(BoundsCenter.x + halfWidth, CutY);
			return true;
		}

		public bool TryRaycast(Vector2 direction, out Vector2 outwardNormal)
		{
			Vector2 start = new (BoundsCenter.x, (CropBounds.max.y + CutY) * 0.5f);
		    outwardNormal = Vector2.up;

		    if (Radii is not { x: > 0.0001f, y: > 0.0001f } || direction.sqrMagnitude <= Mathf.Epsilon)
		    {
		        return false;
		    }

		    float bestT = float.PositiveInfinity;
		    bool hasHit = false;

		    Vector2 radii = Radii;
		    Vector2 startRel = start - BoundsCenter;
		    
		    Vector2 invRadiiSq = new Vector2(1f / (radii.x * radii.x), 1f / (radii.y * radii.y));
		    
		    float a = Vector2.Dot(direction * invRadiiSq, direction);
		    float b = 2f * Vector2.Dot(startRel * invRadiiSq, direction);
		    float c = Vector2.Dot(startRel * invRadiiSq, startRel) - 1f;

		    float discriminant = b * b - 4f * a * c;

		    if (discriminant >= 0f && a > 0.000001f)
		    {
		        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
		        TryUseRaycastCandidate(start, direction, (-b - sqrtDiscriminant) / (2f * a), false, ref bestT, ref outwardNormal, ref hasHit);
		        TryUseRaycastCandidate(start, direction, (-b + sqrtDiscriminant) / (2f * a), false, ref bestT, ref outwardNormal, ref hasHit);
		    }

		    if (direction.y < -0.0001f)
		    {
		        float cutT = (CutY - start.y) / direction.y;
		        TryUseRaycastCandidate(start, direction, cutT, true, ref bestT, ref outwardNormal, ref hasHit);
		    }

		    return hasHit;
		}

		private void TryUseRaycastCandidate(Vector2 start, Vector2 direction, float t, bool isCut, ref float bestT, ref Vector2 outwardNormal, ref bool hasHit)
		{
		    if (t <= 0.0001f || t >= bestT)
		    {
		        return;
		    }

		    Vector2 point = start + direction * t;

		    if (isCut)
		    {
		        if (IsInsideEllipse(point))
		        {
		            return;
		        }
		        outwardNormal = Vector2.down;
		    }
		    else
		    {
		        if (!IsAboveCut(point))
		        {
		            return;
		        }

		        Vector2 normal = GetEllipseNormal(point);
		        if (normal.sqrMagnitude <= 0.000001f)
		        {
		            return;
		        }
		        outwardNormal = normal;
		    }

		    bestT = t;
		    hasHit = true;
		}

		void OnDrawGizmos()
		{
			Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
			if (!useEllipticalBounds)
			{
				Gizmos.DrawWireCube(CropBounds.center, CropBounds.size);
				return;
			}

			DrawEllipseGizmo(BoundsCenter, EllipseRadii, 128);

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
			float relY = obstacleY - BoundsCenter.y;
			float radiusY = Mathf.Max(EllipseRadii.y, 0.0001f);
			float normalizedY = relY / radiusY;
			if (Mathf.Abs(normalizedY) >= 1f)
			{
				return;
			}

			float halfWidth = EllipseRadii.x * Mathf.Sqrt(1f - normalizedY * normalizedY);
			Vector3 left = new(BoundsCenter.x - halfWidth, obstacleY, 0f);
			Vector3 right = new(BoundsCenter.x + halfWidth, obstacleY, 0f);
			Gizmos.DrawLine(left, right);
		}
	}
}
