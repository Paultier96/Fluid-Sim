#ifndef PARTICLE_FLUID_GRADIENT_SAMPLING_COMPUTE_INCLUDED
#define PARTICLE_FLUID_GRADIENT_SAMPLING_COMPUTE_INCLUDED

float3 ParticleFluidSampleColourMap(Texture2D<float4> colourMap, float t)
{
	uint width;
	uint height;
	colourMap.GetDimensions(width, height);
	float2 pixel = float2(saturate(t) * max((float)width - 1.0, 0.0), 0.5 * max((float)height - 1.0, 0.0));
	int2 p00 = (int2)floor(pixel);
	int2 p10 = p00 + int2(1, 0);
	float f = pixel.x - p00.x;
	p00 = clamp(p00, int2(0, 0), int2((int)width - 1, (int)height - 1));
	p10 = clamp(p10, int2(0, 0), int2((int)width - 1, (int)height - 1));
	return lerp(colourMap.Load(int3(p00, 0)).rgb, colourMap.Load(int3(p10, 0)).rgb, f);
}

float3 ParticleFluidBoostGradientColour(float3 colour)
{
	float maxChannel = max(max(colour.r, colour.g), colour.b);
	if (maxChannel <= 0.0001)
	{
		return float3(1.0, 1.0, 1.0);
	}

	float targetMax = sqrt(saturate(maxChannel));
	float3 boostedColour = colour * (targetMax / maxChannel);
	return max(boostedColour, targetMax * 0.03);
}

float3 ParticleFluidColourFromCombined(float4 combined, int phase, Texture2D<float4> colourMap0, Texture2D<float4> colourMap1)
{
	if (phase == 0)
	{
		float data = combined.r / max(combined.g, 0.0001);
		return ParticleFluidBoostGradientColour(ParticleFluidSampleColourMap(colourMap0, data));
	}

	if (phase == 1)
	{
		float data = combined.b / max(combined.a, 0.0001);
		return ParticleFluidBoostGradientColour(ParticleFluidSampleColourMap(colourMap1, data));
	}

	return 1.0;
}

#endif
