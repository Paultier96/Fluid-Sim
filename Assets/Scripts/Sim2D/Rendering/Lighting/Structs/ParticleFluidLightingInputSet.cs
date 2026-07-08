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
		public readonly Texture velocityPhase0Texture;
		public readonly Texture velocityPhase1Texture;

		public ParticleFluidLightingInputSet(Texture albedoTexture, Texture normalTexture, Texture transportTexture, Bounds renderRegion, Vector2Int sourceSize, Texture velocityPhase0Texture = null, Texture velocityPhase1Texture = null)
		{
			this.albedoTexture = albedoTexture;
			this.normalTexture = normalTexture;
			this.transportTexture = transportTexture;
			this.renderRegion = renderRegion;
			this.sourceSize = sourceSize;
			this.velocityPhase0Texture = velocityPhase0Texture;
			this.velocityPhase1Texture = velocityPhase1Texture;
		}
	}
}
