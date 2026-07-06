using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderLayout2D
	{
		public readonly ParticleFluidRenderRegion2D domainRegion;
		public readonly Vector2Int sourceSize;
		public readonly Vector2Int materialSize;
		public readonly Vector2Int causticSize;

		public ParticleFluidRenderLayout2D(
			ParticleFluidRenderRegion2D domainRegion,
			Vector2Int sourceSize,
			Vector2Int materialSize,
			Vector2Int causticSize)
		{
			this.domainRegion = domainRegion;
			this.sourceSize = ClampSize(sourceSize);
			this.materialSize = ClampSize(materialSize);
			this.causticSize = ClampSize(causticSize);
		}

		public ParticleFluidRenderRegion2D SourceRegion => CreateRegion(sourceSize);
		public ParticleFluidRenderRegion2D MaterialRegion => CreateRegion(materialSize);
		public ParticleFluidRenderRegion2D CausticRegion => CreateRegion(causticSize);

		ParticleFluidRenderRegion2D CreateRegion(Vector2Int size)
		{
			return new ParticleFluidRenderRegion2D(domainRegion.WorldCenter, domainRegion.WorldSize, size);
		}

		static Vector2Int ClampSize(Vector2Int size)
		{
			return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
		}
	}
}
