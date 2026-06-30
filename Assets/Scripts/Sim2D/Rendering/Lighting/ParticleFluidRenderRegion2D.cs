using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderRegion2D
	{
		public readonly Vector2 WorldCenter;
		public readonly Vector2 WorldSize;
		public readonly Vector4 SourceUvRect;
		public readonly int PixelWidth;
		public readonly int PixelHeight;
		public readonly bool IsCropped;

		public ParticleFluidRenderRegion2D(Vector2 worldCenter, Vector2 worldSize, Vector4 sourceUvRect, int pixelWidth, int pixelHeight, bool isCropped)
		{
			WorldCenter = worldCenter;
			WorldSize = worldSize;
			SourceUvRect = sourceUvRect;
			PixelWidth = Mathf.Max(pixelWidth, 1);
			PixelHeight = Mathf.Max(pixelHeight, 1);
			IsCropped = isCropped;
		}

		public static ParticleFluidRenderRegion2D Full(Camera cam, int pixelWidth, int pixelHeight)
		{
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			return new ParticleFluidRenderRegion2D(worldCenter, new Vector2(worldWidth, worldHeight), new Vector4(0f, 0f, 1f, 1f), pixelWidth, pixelHeight, false);
		}
	}
}
