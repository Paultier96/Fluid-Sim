using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderLayout2D
	{
		public readonly ParticleFluidRenderRegion2D CameraRegion;
		public readonly ParticleFluidRenderRegion2D DomainRegion;
		public readonly Vector2Int SourceSize;
		public readonly Vector2Int MaterialSize;
		public readonly Vector2Int CausticSize;

		public ParticleFluidRenderLayout2D(
			ParticleFluidRenderRegion2D cameraRegion,
			ParticleFluidRenderRegion2D domainRegion,
			Vector2Int sourceSize,
			Vector2Int materialSize,
			Vector2Int causticSize)
		{
			CameraRegion = cameraRegion;
			DomainRegion = domainRegion;
			SourceSize = ClampSize(sourceSize);
			MaterialSize = ClampSize(materialSize);
			CausticSize = ClampSize(causticSize);
		}

		public ParticleFluidRenderRegion2D SourceRegion => CreateRegion(SourceSize);
		public ParticleFluidRenderRegion2D MaterialRegion => CreateRegion(MaterialSize);
		public ParticleFluidRenderRegion2D CausticRegion => CreateRegion(CausticSize);

		public static Vector2Int ScaledSize(ParticleFluidRenderRegion2D cameraRegion, float scale)
		{
			float clampedScale = Mathf.Max(scale, 0.0001f);
			return new Vector2Int(
				Mathf.Max(1, Mathf.RoundToInt(cameraRegion.PixelSize.x * clampedScale)),
				Mathf.Max(1, Mathf.RoundToInt(cameraRegion.PixelSize.y * clampedScale)));
		}

		ParticleFluidRenderRegion2D CreateRegion(Vector2Int size)
		{
			return new ParticleFluidRenderRegion2D(
				DomainRegion.WorldCenter,
				DomainRegion.WorldSize,
				size.x,
				size.y);
		}

		static Vector2Int ClampSize(Vector2Int size)
		{
			return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
		}
	}
}
