#ifndef PARTICLE_FLUID_GRADIENT_SAMPLING_INCLUDED
#define PARTICLE_FLUID_GRADIENT_SAMPLING_INCLUDED

sampler2D GradientAtlas;

static const float ParticleFluidGradientAtlasPhase0Row = 0.125;
static const float ParticleFluidGradientAtlasPhase1Row = 0.375;
static const float ParticleFluidGradientAtlasHeatRow = 0.625;
static const float ParticleFluidGradientAtlasSignedHeatRow = 0.875;

float3 ParticleFluidSampleGradientAtlas(float data, float row)
{
	return tex2D(GradientAtlas, float2(saturate(data), row)).rgb;
}

float3 ParticleFluidSamplePhaseGradientColour(float data, bool usePhase1)
{
	return ParticleFluidSampleGradientAtlas(data, usePhase1 ? ParticleFluidGradientAtlasPhase1Row : ParticleFluidGradientAtlasPhase0Row);
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
