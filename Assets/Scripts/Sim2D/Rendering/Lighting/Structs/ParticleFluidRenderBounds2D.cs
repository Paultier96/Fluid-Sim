using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public static class ParticleFluidRenderBounds2D
	{
		public static Matrix4x4 CreateRegionMatrix(this Bounds bounds)
		{
			return Matrix4x4.TRS(bounds.center, Quaternion.identity, bounds.size);
		}

		public static Matrix4x4 CreateRegionProjection(this Bounds bounds)
		{
			float left = bounds.min.x;
			float right = bounds.max.x;
			float bottom = bounds.min.y;
			float top = bounds.max.y;
			Matrix4x4 ortho = Matrix4x4.Ortho(left, right, bottom, top, -1f, 1f);
			return GL.GetGPUProjectionMatrix(ortho, false);
		}

		public static Vector2 WorldToCenteredPixel(this Bounds bounds, Vector2 worldPoint, Vector2Int resolution)
		{
			return (worldPoint - (Vector2)bounds.center) / Vector2.Max(bounds.size, Vector2.one * 0.0001f) * resolution;
		}

		private static Vector2 WorldToUV(Bounds bounds, Vector2 worldPoint)
		{
			return (worldPoint - (Vector2)bounds.min) / bounds.size;
		}
		
		public static Vector2Int ScaleSize(Vector2Int baseSize, float scale)
		{
			Vector2 size = new Vector2(Mathf.Max(baseSize.x, 1), Mathf.Max(baseSize.y, 1));
			return Vector2Int.Max(Vector2Int.one, Vector2Int.RoundToInt(size * scale));
		}
		
		public static Bounds GetCameraRenderRegion(Camera cam, Bounds? cropBounds, bool crop, out Vector2Int resolution)
		{
			resolution = new Vector2Int(Mathf.Max(cam.pixelWidth, 1), Mathf.Max(cam.pixelHeight, 1));
			float worldHeight = cam.orthographicSize * 2f;
			float worldWidth = worldHeight * cam.aspect;
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			Bounds fullRegion = new Bounds(worldCenter, new Vector2(worldWidth, worldHeight));
			if (!crop || cropBounds == null)
			{
				return fullRegion;
			}

			Vector2Int resolution1 = resolution;
			resolution1 = new Vector2Int(Mathf.Max(resolution1.x, 1), Mathf.Max(resolution1.y, 1));
			Vector2 cropMin = Vector2.Max(fullRegion.min, cropBounds.Value.min);
			Vector2 cropMax = Vector2.Min(fullRegion.max, cropBounds.Value.max);

			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				resolution = Vector2Int.one;
				float size = fullRegion.size.y / Mathf.Max(resolution1.y, 1);
				return new Bounds(fullRegion.center, new Vector3(size, size, 0f));
			}

			Vector2 uv = Vector2.Min(Vector2.Max(WorldToUV(fullRegion, cropMin), Vector2.zero), Vector2.one);
			Vector2Int minPixel = new Vector2Int(
				Mathf.Clamp(Mathf.FloorToInt(uv.x * resolution1.x), 0, Mathf.Max(resolution1.x - 1, 0)),
				Mathf.Clamp(Mathf.FloorToInt(uv.y * resolution1.y), 0, Mathf.Max(resolution1.y - 1, 0))
			);
			Vector2 uv1 = Vector2.Min(Vector2.Max(WorldToUV(fullRegion, cropMax), Vector2.zero), Vector2.one);
			Vector2Int maxPixel = new Vector2Int(
				Mathf.Clamp(Mathf.CeilToInt(uv1.x * resolution1.x), minPixel.x + 1, resolution1.x),
				Mathf.Clamp(Mathf.CeilToInt(uv1.y * resolution1.y), minPixel.y + 1, resolution1.y)
			);

			resolution = new Vector2Int(Mathf.Max(1, maxPixel.x - minPixel.x), Mathf.Max(1, maxPixel.y - minPixel.y));
			if (resolution.x >= resolution1.x - 1 && resolution.y >= resolution1.y - 1)
			{
				resolution = resolution1;
				return fullRegion;
			}

			Vector2 uvMin = minPixel / (Vector2)resolution1;
			Vector2 uvSize = resolution / (Vector2)resolution1;
			Vector2 worldMin = (Vector2)fullRegion.min + Vector2.Scale(uvMin, fullRegion.size);
			Vector2 worldSize = Vector2.Scale(uvSize, fullRegion.size);
			return new Bounds(worldMin + worldSize * 0.5f, worldSize);
		}
	}
}

