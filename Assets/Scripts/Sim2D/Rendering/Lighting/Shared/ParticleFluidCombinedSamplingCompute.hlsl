#ifndef PARTICLE_FLUID_COMBINED_SAMPLING_COMPUTE_INCLUDED
#define PARTICLE_FLUID_COMBINED_SAMPLING_COMPUTE_INCLUDED

float4 ParticleFluidLoadCombinedPixel(Texture2D<float4> combinedTex, int2 pixel, int2 combinedResolution)
{
	pixel = clamp(pixel, int2(0, 0), combinedResolution - 1);
	return combinedTex.Load(int3(pixel, 0));
}

float4 ParticleFluidLoadCombinedAtUv(Texture2D<float4> combinedTex, float2 uv, int2 combinedResolution)
{
	int2 pixel = (int2)round(saturate(uv) * (combinedResolution - 1));
	return ParticleFluidLoadCombinedPixel(combinedTex, pixel, combinedResolution);
}

bool ParticleFluidInsideUv01(float2 uv)
{
	return all(uv >= 0.0) && all(uv <= 1.0);
}

#endif
