#ifndef PARTICLE_FLUID_GRADIENT_SAMPLING_INCLUDED
#define PARTICLE_FLUID_GRADIENT_SAMPLING_INCLUDED

float3 ParticleFluidSamplePhaseGradientColour(float data, bool usePhase1)
{
	return usePhase1
		? tex2D(ColourMap2, float2(saturate(data), 0.5)).rgb
		: tex2D(ColourMap, float2(saturate(data), 0.5)).rgb;
}

float3 ParticleFluidSampleGradientColour(float4 combined, float density0, float density1, float fallbackData0, float fallbackData1, float phaseT)
{
	float sampleData0 = density0 > 0.0001 ? combined.r / density0 : fallbackData0;
	float sampleData1 = density1 > 0.0001 ? combined.b / density1 : fallbackData1;
	float3 colour0 = ParticleFluidSamplePhaseGradientColour(sampleData0, false);
	float3 colour1 = ParticleFluidSamplePhaseGradientColour(sampleData1, true);
	return lerp(colour0, colour1, phaseT);
}

#endif
