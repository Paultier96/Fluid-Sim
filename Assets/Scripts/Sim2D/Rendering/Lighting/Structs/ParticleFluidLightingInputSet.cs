using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	internal readonly struct ParticleFluidLightingInputSet
	{
		public readonly Texture albedoTexture;
		public readonly Texture normalTexture;
		public readonly Texture transportTexture;
		public readonly Bounds renderRegion;
		public readonly Vector2Int sourceSize;
		public readonly Texture velocityTexture;

		public ParticleFluidLightingInputSet(Texture albedoTexture, Texture normalTexture, Texture transportTexture, Bounds renderRegion, Vector2Int sourceSize, Texture velocityTexture = null)
		{
			this.albedoTexture = albedoTexture;
			this.normalTexture = normalTexture;
			this.transportTexture = transportTexture;
			this.renderRegion = renderRegion;
			this.sourceSize = sourceSize;
			this.velocityTexture = velocityTexture;
		}
	}
}
