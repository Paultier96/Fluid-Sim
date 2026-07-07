using System;
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
				return new Vector3(Mathf.Cos(azimuth) * planarLength, Mathf.Sin(azimuth) * planarLength, Mathf.Sin(elevation));
			}
		}

		public void GetCausticRayRange(ParticleFluidLighting2D.FrameContext context, Vector2Int resolution, out float startOffset, out int rayCount)
		{
			Vector2 lightDirection = Direction;
			ParticleFluidAnalyticBoundary2D boundary = context.display.sim.analyticBoundary;
			Vector2 rayDir = lightDirection.sqrMagnitude > 0.0001f ? (-lightDirection).normalized : Vector2.right;
			Vector2 tangent = new Vector2(-rayDir.y, rayDir.x);
			float fullSpan = Mathf.Sqrt(resolution.x * resolution.x + resolution.y * resolution.y);
			float screenMinOffset = -fullSpan * 0.5f;
			float screenMaxOffset = fullSpan * 0.5f;
			startOffset = screenMinOffset;
			rayCount = Mathf.Max(1, Mathf.CeilToInt(fullSpan));

			if (!boundary.useEllipticalBounds)
			{
				return;
			}

			float minOffset = float.PositiveInfinity;
			float maxOffset = float.NegativeInfinity;

			int samples = 128;

			for (int i = 0; i < 128; i++)
			{
				float angle = i * Mathf.PI * 2f / samples;
				if (boundary.TryGetEllipsePoint(angle, out Vector2 launchPoint))
				{
					IncludeCausticLaunchPoint(launchPoint, context.renderLayout.CausticRegion, tangent, ref minOffset, ref maxOffset);
				}
			}

			if (boundary.TryGetCutSegment(out Vector2 left, out Vector2 right))
			{
				IncludeCausticLaunchPoint(left, context.renderLayout.CausticRegion, tangent, ref minOffset, ref maxOffset);
				IncludeCausticLaunchPoint(right, context.renderLayout.CausticRegion, tangent, ref minOffset, ref maxOffset);
			}

			if (float.IsNaN(minOffset) || float.IsInfinity(minOffset) || float.IsNaN(maxOffset) || float.IsInfinity(maxOffset))
			{
				return;
			}

			float angularPadding = Mathf.Sin(angularRadiusDegrees * Mathf.Deg2Rad) * fullSpan;
			float padding = Mathf.Max(4f + angularPadding, 2f);
			float clippedMinOffset = Mathf.Max(minOffset - padding, screenMinOffset);
			float clippedMaxOffset = Mathf.Min(maxOffset + padding, screenMaxOffset);
			if (clippedMaxOffset <= clippedMinOffset)
			{
				rayCount = 0;
				return;
			}

			startOffset = Mathf.Floor(clippedMinOffset);
			rayCount = Mathf.Max(1, Mathf.CeilToInt(clippedMaxOffset - startOffset));
		}

		private static void IncludeCausticLaunchPoint(Vector2 launchPoint, ParticleFluidRenderRegion2D causticRegion, Vector2 tangent, ref float minOffset, ref float maxOffset)
		{
			Vector2 centredPixel = causticRegion.WorldToCenteredPixel(launchPoint);
			float offset = Vector2.Dot(centredPixel, tangent);
			minOffset = Mathf.Min(minOffset, offset);
			maxOffset = Mathf.Max(maxOffset, offset);
		}

		public Vector3 GetBoundaryRefractedDirection(float ior, ParticleFluidAnalyticBoundary2D analyticBoundary)
		{
			Vector3 lightDirection = Direction;
			if (!analyticBoundary.useEllipticalBounds)
			{
				return lightDirection;
			}

			Vector2 lightXY = lightDirection;
			float planarLength = lightXY.magnitude;
			Vector2 directionToLight = lightXY.normalized;
			if (!analyticBoundary.TryRaycast(directionToLight, out Vector2 outwardNormal))
			{
				return lightDirection;
			}

			Vector2 incomingRayDirection = -directionToLight;
			Vector2 refractedRayDirection = Refract2D(incomingRayDirection, outwardNormal, 1f / Mathf.Max(ior, 1.0001f));
			Vector2 refractedLightXY = -refractedRayDirection * planarLength;
			return new Vector3(refractedLightXY.x, refractedLightXY.y, lightDirection.z).normalized;
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
	}
}
