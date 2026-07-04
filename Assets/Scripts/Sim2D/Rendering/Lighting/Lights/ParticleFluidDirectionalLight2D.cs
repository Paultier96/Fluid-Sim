using Seb.Fluid2D.Simulation;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	[AddComponentMenu("Fluid Sim/2D/Particle Fluid Directional Light 2D")]
	[DisallowMultipleComponent]
	public sealed class ParticleFluidDirectionalLight2D : ParticleFluidLight2D
	{
		public float azimuthDegrees = 122.5f;
		[Range(-89f, 89f)] public float elevationDegrees = 50.3f;
		[Min(0f)] public float angularRadiusDegrees = 0f;

		public Vector3 Direction
		{
			get
			{
				float azimuth = azimuthDegrees * Mathf.Deg2Rad;
				float elevation = elevationDegrees * Mathf.Deg2Rad;
				float planarLength = Mathf.Cos(elevation);
				return new Vector3(
					Mathf.Cos(azimuth) * planarLength,
					Mathf.Sin(azimuth) * planarLength,
					Mathf.Sin(elevation)
				).normalized;
			}
		}

		public Vector3 GetDirectLightingDirection(ParticleFluidLighting2D lighting, ParticleFluidAnalyticBoundary2D analyticBoundary)
		{
			Vector3 lightDirection = Direction;
			if (lighting == null || !analyticBoundary.useEllipticalBounds)
			{
				return lightDirection;
			}

			Vector2 lightXY = new Vector2(lightDirection.x, lightDirection.y);
			float planarLength = lightXY.magnitude;
			if (planarLength <= 0.0001f)
			{
				return lightDirection;
			}

			Vector2 directionToLight = lightXY / planarLength;
			if (!TryGetAnalyticBoundaryHitFromCenter(analyticBoundary, directionToLight, out Vector2 outwardNormal))
			{
				return lightDirection;
			}

			ParticleFluidLighting2D.PhaseMaterialSettings[] materials = lighting.PhaseMaterials;
			Vector2 incomingRayDirection = -directionToLight;
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(materials[1].indexOfRefraction, 1.0001f));
			Vector2 refractedLightXY = -refractedRayDirection * planarLength;
			return new Vector3(refractedLightXY.x, refractedLightXY.y, lightDirection.z).normalized;
		}

		protected override void DrawLightGizmos()
		{
			Vector3 origin = transform.position;
			Vector3 direction = -Direction;
			float length = 3f;
			Vector3 tip = origin + direction * length;
			Gizmos.DrawLine(origin, tip);

			Vector3 side = Vector3.Cross(direction, Vector3.forward);
			if (side.sqrMagnitude < 0.0001f)
			{
				side = Vector3.right;
			}
			side.Normalize();

			float headLength = 0.5f;
			float headWidth = 0.25f;
			Vector3 back = tip - direction * headLength;
			Gizmos.DrawLine(tip, back + side * headWidth);
			Gizmos.DrawLine(tip, back - side * headWidth);
		}

		static Vector2 Refract2D(Vector2 rayDirection, Vector2 normal, float eta)
		{
			if (Vector2.Dot(rayDirection, normal) > 0f)
			{
				normal = -normal;
			}

			float cosI = Vector2.Dot(-rayDirection, normal);
			float sinT2 = eta * eta * Mathf.Max(0f, 1f - cosI * cosI);
			if (sinT2 > 1f)
			{
				return (rayDirection - 2f * Vector2.Dot(rayDirection, normal) * normal).normalized;
			}

			float cosT = Mathf.Sqrt(Mathf.Max(0f, 1f - sinT2));
			return (eta * rayDirection + (eta * cosI - cosT) * normal).normalized;
		}

		bool TryGetAnalyticBoundaryHitFromCenter(ParticleFluidAnalyticBoundary2D analyticBoundary, Vector2 directionToLight, out Vector2 outwardNormal)
		{
			Vector2 center = analyticBoundary.ellipseBoundsCenter;
			Vector2 radii = analyticBoundary.Radii;
			float cutY = analyticBoundary.CutY;
			Vector2 start = new Vector2(center.x, (analyticBoundary.BoundsMax.y + cutY) * 0.5f);
			outwardNormal = Vector2.up;
			if (radii.x <= 0.0001f || radii.y <= 0.0001f)
			{
				return false;
			}

			float bestT = float.PositiveInfinity;
			bool hasHit = false;

			float invRx2 = 1f / (radii.x * radii.x);
			float invRy2 = 1f / (radii.y * radii.y);
			float a = directionToLight.x * directionToLight.x * invRx2 + directionToLight.y * directionToLight.y * invRy2;
			Vector2 startRel = start - center;
			float b = 2f * (startRel.x * directionToLight.x * invRx2 + startRel.y * directionToLight.y * invRy2);
			float c = startRel.x * startRel.x * invRx2 + startRel.y * startRel.y * invRy2 - 1f;
			float discriminant = b * b - 4f * a * c;
			if (discriminant >= 0f && a > 0.000001f)
			{
				float sqrtDiscriminant = Mathf.Sqrt(discriminant);
				TryUseAnalyticBoundaryCandidate(analyticBoundary, (-b - sqrtDiscriminant) / (2f * a), directionToLight, false, ref bestT, ref outwardNormal, ref hasHit);
				TryUseAnalyticBoundaryCandidate(analyticBoundary, (-b + sqrtDiscriminant) / (2f * a), directionToLight, false, ref bestT, ref outwardNormal, ref hasHit);
			}

			if (directionToLight.y < -0.0001f)
			{
				float cutT = (cutY - start.y) / directionToLight.y;
				TryUseAnalyticBoundaryCandidate(analyticBoundary, cutT, directionToLight, true, ref bestT, ref outwardNormal, ref hasHit);
			}

			return hasHit;
		}

		static void TryUseAnalyticBoundaryCandidate(ParticleFluidAnalyticBoundary2D analyticBoundary, float t, Vector2 direction, bool isCut, ref float bestT, ref Vector2 outwardNormal, ref bool hasHit)
		{
			if (t <= 0.0001f || t >= bestT)
			{
				return;
			}
			Vector2 center = analyticBoundary.ellipseBoundsCenter;
			float cutY = analyticBoundary.CutY;
			Vector2 radii = analyticBoundary.Radii;

			Vector2 point = new Vector2(center.x, (analyticBoundary.BoundsMax.y + cutY) * 0.5f) + direction * t;
			Vector2 rel = point - center;
			float ellipseValue = rel.x * rel.x / (radii.x * radii.x) + rel.y * rel.y / (radii.y * radii.y);
			if (isCut)
			{
				if (ellipseValue > 1.0001f)
				{
					return;
				}

				outwardNormal = Vector2.down;
			}
			else
			{
				if (point.y < cutY - 0.0001f)
				{
					return;
				}

				Vector2 ellipseNormal = new Vector2(rel.x / (radii.x * radii.x), rel.y / (radii.y * radii.y));
				if (ellipseNormal.sqrMagnitude <= 0.000001f)
				{
					return;
				}

				outwardNormal = ellipseNormal.normalized;
			}

			bestT = t;
			hasHit = true;
		}
	}
}
