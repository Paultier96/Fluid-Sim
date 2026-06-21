namespace Seb.Fluid2D.Rendering
{
	public readonly struct ParticleFluidRenderLayout2D
	{
		public readonly ParticleFluidRenderRegion2D Crop;
		public readonly ParticleFluidRenderRegion2D Source;
		public readonly ParticleFluidRenderRegion2D Material;
		public readonly ParticleFluidRenderRegion2D Caustic;

		public ParticleFluidRenderLayout2D(
			ParticleFluidRenderRegion2D crop,
			ParticleFluidRenderRegion2D source,
			ParticleFluidRenderRegion2D material,
			ParticleFluidRenderRegion2D caustic)
		{
			Crop = crop;
			Source = source;
			Material = material;
			Caustic = caustic;
		}
	}
}
