using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public static class ParticleFluidRenderRegion2D
	{
		public static Bounds Full(Camera cam)
		{
			float worldHeight = cam.orthographicSize * 2f;
			float worldWidth = worldHeight * cam.aspect;
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			return new Bounds(worldCenter, new Vector2(worldWidth, worldHeight));
		}

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

		static Vector2 WorldToUV(Bounds bounds, Vector2 worldPoint)
		{
			return (worldPoint - (Vector2)bounds.min) / bounds.size;
		}

		public static Bounds CropToWorldBounds(this Bounds bounds, Bounds cropBounds, Vector2Int resolution, out Vector2Int croppedResolution)
		{
			resolution = new Vector2Int(Mathf.Max(resolution.x, 1), Mathf.Max(resolution.y, 1));
			Vector2 cropMin = Vector2.Max(bounds.min, cropBounds.min);
			Vector2 cropMax = Vector2.Min(bounds.max, cropBounds.max);

			if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
			{
				croppedResolution = Vector2Int.one;
				float size = bounds.size.y / Mathf.Max(resolution.y, 1);
				return new Bounds(bounds.center, new Vector3(size, size, 0f));
			}

			Vector2 uv = Vector2.Min(Vector2.Max(WorldToUV(bounds, cropMin), Vector2.zero), Vector2.one);
			Vector2Int minPixel = new Vector2Int(
				Mathf.Clamp(Mathf.FloorToInt(uv.x * resolution.x), 0, Mathf.Max(resolution.x - 1, 0)),
				Mathf.Clamp(Mathf.FloorToInt(uv.y * resolution.y), 0, Mathf.Max(resolution.y - 1, 0))
			);
			Vector2 uv1 = Vector2.Min(Vector2.Max(WorldToUV(bounds, cropMax), Vector2.zero), Vector2.one);
			Vector2Int maxPixel = new Vector2Int(
				Mathf.Clamp(Mathf.CeilToInt(uv1.x * resolution.x), minPixel.x + 1, resolution.x),
				Mathf.Clamp(Mathf.CeilToInt(uv1.y * resolution.y), minPixel.y + 1, resolution.y)
			);

			croppedResolution = new Vector2Int(Mathf.Max(1, maxPixel.x - minPixel.x), Mathf.Max(1, maxPixel.y - minPixel.y));
			if (croppedResolution.x >= resolution.x - 1 && croppedResolution.y >= resolution.y - 1)
			{
				croppedResolution = resolution;
				return bounds;
			}

			Vector2 uvMin = minPixel / (Vector2)resolution;
			Vector2 uvSize = croppedResolution / (Vector2)resolution;
			Vector2 worldMin = (Vector2)bounds.min + Vector2.Scale(uvMin, bounds.size);
			Vector2 worldSize = Vector2.Scale(uvSize, bounds.size);
			return new Bounds(worldMin + worldSize * 0.5f, worldSize);
		}
	}
}

