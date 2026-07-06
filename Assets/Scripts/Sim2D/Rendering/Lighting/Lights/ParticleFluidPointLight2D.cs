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
		
		const float TwoPi = 2f * Mathf.PI;
		const int BoundaryEllipseSamples = 128;
		const int BoundaryCutSamples = 31;

		public Vector4 GetPointLightVector()
		{
			Vector3 position = transform.position;
			return new Vector4(position.x, position.y, Mathf.Max(position.z, 0.0001f), Mathf.Max(range, 0.0001f));
		}

		public int GetCausticPointRayCount(Vector2 worldSize, Vector2Int resolution)
		{
			float pixelsPerWorldUnit = Mathf.Max(
				resolution.x / Mathf.Max(worldSize.x, 0.0001f),
				resolution.y / Mathf.Max(worldSize.y, 0.0001f)
			);
			float radiusPixels = Mathf.Max(range, 0.0001f) * pixelsPerWorldUnit;
			return Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2 * radiusPixels));
		}

		public void GetCausticRaySpan(ParticleFluidAnalyticBoundary2D boundary, float[] scratchAngles, out float angleStart, out float angleRange)
		{
			angleStart = 0f;
			angleRange = TwoPi;
			Vector2 point = transform.position;

			if (boundary == null || scratchAngles == null || scratchAngles.Length == 0 || !boundary.useEllipticalBounds || boundary.Contains(point))
			{
				return;
			}
			
			int angleCount = 0;
			for (int i = 0; i < BoundaryEllipseSamples; i++)
			{
				float angle = i * Mathf.PI * 2f / BoundaryEllipseSamples;
				if (boundary.TryGetEllipsePoint(angle, out Vector2 boundaryPoint))
				{
					AddBoundaryAngle(scratchAngles, point, boundaryPoint, ref angleCount);
				}
			}
			
			if (boundary.TryGetCutSegment(out Vector2 left, out Vector2 right))
			{
				for (int i = 0; i < BoundaryCutSamples; i++)
				{
					float t = i / (float)(BoundaryCutSamples - 1);
					AddBoundaryAngle(scratchAngles, point, Vector2.Lerp(left, right, t), ref angleCount);
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
		
		protected override void DrawLightGizmos()
		{
			float radius = Mathf.Max(range, 0.0001f);
			Vector3 previous = transform.position + Vector3.right * radius;
			for (int i = 1; i <= 48; i++)
			{
				float angle = i / (float)48 * Mathf.PI * 2f;
				Vector3 current = transform.position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
				Gizmos.DrawLine(previous, current);
				previous = current;
			}
		}
	}
}
