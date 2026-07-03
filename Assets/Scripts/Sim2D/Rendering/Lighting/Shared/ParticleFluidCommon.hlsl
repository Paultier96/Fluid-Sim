#ifndef PARTICLE_FLUID_COMMON_INCLUDED
#define PARTICLE_FLUID_COMMON_INCLUDED

float2 ParticleFluidWorldFromUv(float2 uv, float2 worldCenter, float2 worldSize)
{
	return worldCenter + (uv - 0.5) * max(worldSize, float2(0.0001, 0.0001));
}

float2 ParticleFluidUvFromWorld(float2 worldPos, float2 worldCenter, float2 worldSize)
{
	return (worldPos - worldCenter) / max(worldSize, float2(0.0001, 0.0001)) + 0.5;
}

float ParticleFluidPhaseRatio(float density0, float density1)
{
	return density1 / max(density0 + density1, 0.0001);
}

float ParticleFluidPhaseRatio(float2 densities)
{
	return ParticleFluidPhaseRatio(densities.x, densities.y);
}

float ParticleFluidPhaseRatioFromCombined(float4 combined)
{
	return ParticleFluidPhaseRatio(combined.g, combined.a);
}

float ParticleFluidPhaseBoundary(float phase0RenderBias)
{
	return saturate(0.5 + phase0RenderBias * 0.5);
}

#endif
