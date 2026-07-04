using UnityEngine;
using Seb.Fluid2D.Simulation;

namespace Seb.Fluid2D.Rendering
{
	[AddComponentMenu("Fluid Sim/2D/Particle Fluid Point Light 2D")]
	[DisallowMultipleComponent]
	public sealed class ParticleFluidPointLight2D : ParticleFluidLight2D
	{
		public bool followsMouse = false;
		[Min(0.0001f)] public float range = 20f;
		[Min(0.1f)] public float falloff = 2f;

		public bool FollowsMouse => followsMouse;

		public Vector4 GetPointLightVector()
		{
			Vector3 position = transform.position;
			return new Vector4(position.x, position.y, Mathf.Max(position.z, 0.0001f), Mathf.Max(range, 0.0001f));
		}

		public int GetCausticPointRayCount(Vector2 worldSize, int width, int height)
		{
			float pixelsPerWorldUnit = Mathf.Max(
				width / Mathf.Max(worldSize.x, 0.0001f),
				height / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(range, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2 * radiusPixels));
		}

		public void GetCausticRaySpan(ParticleFluidAnalyticBoundary2D boundary, float[] scratchAngles, out float angleStart, out float angleRange)
		{
			angleStart = 0f;
			angleRange = TwoPi;
			if (boundary == null || scratchAngles == null || scratchAngles.Length == 0 || !boundary.useEllipticalBounds)
			{
				return;
			}

			Vector3 lightPosition = transform.position;
			Vector2 point = new Vector2(lightPosition.x, lightPosition.y);
			if (boundary.Contains(point))
			{
				return;
			}

			Vector2 radii = boundary.Radii;
			float cutY = boundary.CutY;
			int angleCount = 0;
			for (int i = 0; i < BoundaryEllipseSamples; i++)
			{
				float t = i / (float)BoundaryEllipseSamples * TwoPi;
				Vector2 boundaryPoint = boundary.ellipseBoundsCenter + new Vector2(Mathf.Cos(t) * radii.x, Mathf.Sin(t) * radii.y);
				if (boundaryPoint.y >= cutY)
				{
					AddBoundaryAngle(scratchAngles, point, boundaryPoint, ref angleCount);
				}
			}

			float cutRelY = cutY - boundary.ellipseBoundsCenter.y;
			if (Mathf.Abs(cutRelY) <= radii.y)
			{
				float cutHalfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - cutRelY * cutRelY / (radii.y * radii.y)));
				for (int i = 0; i < BoundaryCutSamples; i++)
				{
					float t = BoundaryCutSamples > 1 ? i / (float)(BoundaryCutSamples - 1) : 0.5f;
					AddBoundaryAngle(scratchAngles, point, new Vector2(boundary.ellipseBoundsCenter.x + Mathf.Lerp(-cutHalfWidth, cutHalfWidth, t), cutY), ref angleCount);
				}
			}

			if (angleCount < 2)
			{
				return;
			}

			System.Array.Sort(scratchAngles, 0, angleCount);
			float largestGap = -1f;
			int largestGapIndex = 0;
			for (int i = 0; i < angleCount; i++)
			{
				float current = scratchAngles[i];
				float next = i == angleCount - 1 ? scratchAngles[0] + TwoPi : scratchAngles[i + 1];
				float gap = next - current;
				if (gap > largestGap)
				{
					largestGap = gap;
					largestGapIndex = i;
				}
			}

			float padding = 2f * Mathf.Deg2Rad;
			angleStart = Mathf.Repeat(scratchAngles[(largestGapIndex + 1) % angleCount] - padding, TwoPi);
			angleRange = Mathf.Clamp(TwoPi - largestGap + padding * 2f, 0.0001f, TwoPi);
		}

		protected override void DrawLightGizmos()
		{
			DrawWireCircleXY(transform.position, Mathf.Max(range, 0.0001f), 48);
		}

		static void AddBoundaryAngle(float[] scratchAngles, Vector2 lightPoint, Vector2 boundaryPoint, ref int angleCount)
		{
			if (angleCount >= scratchAngles.Length)
			{
				return;
			}

			Vector2 delta = boundaryPoint - lightPoint;
			if (delta.sqrMagnitude <= 0.000001f)
			{
				return;
			}

			scratchAngles[angleCount++] = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x), TwoPi);
		}

		static void DrawWireCircleXY(Vector3 center, float radius, int segments)
		{
			Vector3 previous = center + Vector3.right * radius;
			for (int i = 1; i <= segments; i++)
			{
				float angle = i / (float)segments * Mathf.PI * 2f;
				Vector3 current = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
				Gizmos.DrawLine(previous, current);
				previous = current;
			}
		}

		const float TwoPi = 2f * Mathf.PI;
		const int BoundaryEllipseSamples = 128;
		const int BoundaryCutSamples = 31;
	}
}
