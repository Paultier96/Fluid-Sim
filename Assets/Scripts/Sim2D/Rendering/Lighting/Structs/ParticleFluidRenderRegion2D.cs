using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderRegion2D
	{
		public readonly Bounds WorldBounds;
		public readonly Vector2Int PixelSize;
		public Vector2 WorldCenter => new Vector2(WorldBounds.center.x, WorldBounds.center.y);
		public Vector2 WorldSize => new Vector2(WorldBounds.size.x, WorldBounds.size.y);

		public ParticleFluidRenderRegion2D(Vector2 worldCenter, Vector2 worldSize, int pixelWidth, int pixelHeight)
		{
			WorldBounds = new Bounds(worldCenter, worldSize);
			PixelSize = new Vector2Int(Mathf.Max(pixelWidth, 1), Mathf.Max(pixelHeight, 1));
		}

		public ParticleFluidRenderRegion2D(Bounds worldBounds, int pixelWidth, int pixelHeight)
		{
			WorldBounds = worldBounds;
			PixelSize = new Vector2Int(Mathf.Max(pixelWidth, 1), Mathf.Max(pixelHeight, 1));
		}

		public static ParticleFluidRenderRegion2D Full(Camera cam)
		{
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			return new ParticleFluidRenderRegion2D(worldCenter, new Vector2(worldWidth, worldHeight), Mathf.Max(cam.pixelWidth, 1), Mathf.Max(cam.pixelHeight, 1));
		}
	}
}
