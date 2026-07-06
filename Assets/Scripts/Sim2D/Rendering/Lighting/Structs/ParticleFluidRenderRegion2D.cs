using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderRegion2D
	{
		public readonly Bounds worldBounds;
		public readonly Vector2Int pixelSize;
		public Vector2 WorldCenter => new (worldBounds.center.x, worldBounds.center.y);
		public Vector2 WorldSize => new (worldBounds.size.x, worldBounds.size.y);

		public ParticleFluidRenderRegion2D(Vector2 worldCenter, Vector2 worldSize, Vector2Int resolution)
		{
			worldBounds = new Bounds(worldCenter, worldSize);
			pixelSize = new Vector2Int(Mathf.Max(resolution.x, 1), Mathf.Max(resolution.y, 1));
		}

		public ParticleFluidRenderRegion2D(Bounds worldBounds, int pixelWidth, int pixelHeight)
		{
			this.worldBounds = worldBounds;
			pixelSize = new Vector2Int(Mathf.Max(pixelWidth, 1), Mathf.Max(pixelHeight, 1));
		}

		public static ParticleFluidRenderRegion2D Full(Camera cam)
		{
			float worldHeight = Mathf.Max(cam.orthographicSize * 2f, 0.0001f);
			float worldWidth = Mathf.Max(worldHeight * cam.aspect, 0.0001f);
			Vector2 worldCenter = new Vector2(cam.transform.position.x, cam.transform.position.y);
			return new ParticleFluidRenderRegion2D(worldCenter, new Vector2(worldWidth, worldHeight), new Vector2Int(Mathf.Max(cam.pixelWidth, 1), Mathf.Max(cam.pixelHeight, 1)));
		}
		
		public Vector2 WorldToCenteredPixel(Vector2 worldPoint)
		{
			return (worldPoint - WorldCenter) / Vector2.Max(WorldSize, Vector2.one * 0.0001f) * pixelSize;
		}
		
		public Vector2Int ScaledSize(float scale)
		{
			return Vector2Int.Max(Vector2Int.one, Vector2Int.RoundToInt((Vector2)pixelSize * scale));
		}


		private Vector2 WorldToUV(Vector2 worldPoint)
		{
		    return (worldPoint - (Vector2)worldBounds.min) / WorldSize;
		}


		public ParticleFluidRenderRegion2D CropToWorldBounds(Bounds cropBounds)
		{
		    Vector2 cropMin = Vector2.Max(worldBounds.min, cropBounds.min);
		    Vector2 cropMax = Vector2.Min(worldBounds.max, cropBounds.max);

		    if (cropMax.x <= cropMin.x || cropMax.y <= cropMin.y)
		    {
		        float size = WorldSize.y / Mathf.Max(pixelSize.y, 1);
		        return new ParticleFluidRenderRegion2D(
		            new Bounds(worldBounds.center, new Vector3(size, size, 0f)),
		            1,
		            1
		        );
		    }

		    Vector2 uv = Vector2.Min(Vector2.Max(WorldToUV(cropMin), Vector2.zero), Vector2.one);
		    Vector2Int minPixel = new Vector2Int(
			    Mathf.Clamp(Mathf.FloorToInt(uv.x * pixelSize.x), 0, Mathf.Max(pixelSize.x - 1, 0)),
			    Mathf.Clamp(Mathf.FloorToInt(uv.y * pixelSize.y), 0, Mathf.Max(pixelSize.y - 1, 0))
		    );
		    Vector2 uv1 = Vector2.Min(Vector2.Max(WorldToUV(cropMax), Vector2.zero), Vector2.one);
		    Vector2Int maxPixel = new Vector2Int(
			    Mathf.Clamp(Mathf.CeilToInt(uv1.x * pixelSize.x), minPixel.x + 1, pixelSize.x),
			    Mathf.Clamp(Mathf.CeilToInt(uv1.y * pixelSize.y), minPixel.y + 1, pixelSize.y)
		    );

		    Vector2Int resolution = new Vector2Int(Mathf.Max(1, maxPixel.x - minPixel.x), Mathf.Max(1, maxPixel.y - minPixel.y));

		    if (resolution.x >= pixelSize.x - 1 && resolution.y >= pixelSize.y - 1)
		    {
		        return this;
		    }
		    
		    Vector2 uvMin = minPixel / (Vector2)pixelSize;
		    Vector2 uvSize = resolution / (Vector2)pixelSize;

		    Vector2 worldMin = (Vector2)worldBounds.min + Vector2.Scale(uvMin, WorldSize);
		    Vector2 worldSize = Vector2.Scale(uvSize, WorldSize);

		    return new ParticleFluidRenderRegion2D(
		        new Bounds(worldMin + worldSize * 0.5f, worldSize),
		        resolution.x,
		        resolution.y
		    );
		}
		
		
		
		
		
		
		
		
		
	}
}
