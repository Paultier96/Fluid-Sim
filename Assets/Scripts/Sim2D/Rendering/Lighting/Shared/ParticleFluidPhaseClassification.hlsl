#ifndef PARTICLE_FLUID_PHASE_CLASSIFICATION_INCLUDED
#define PARTICLE_FLUID_PHASE_CLASSIFICATION_INCLUDED

float ParticleFluidMaxPhaseDensity(float4 combined)
{
	return max(combined.g, combined.a);
}

float2 ParticleFluidPhaseDensities(float4 combined)
{
	return float2(combined.g, combined.a);
}

int ParticleFluidPhaseFromCombined(float4 combined, float densityThresholdValue, float phase0RenderBiasValue)
{
	if (ParticleFluidMaxPhaseDensity(combined) < densityThresholdValue)
	{
		return -1;
	}

	return ParticleFluidPhaseRatioFromCombined(combined) < ParticleFluidPhaseBoundary(phase0RenderBiasValue) ? 0 : 1;
}

float ParticleFluidPhase0Mask(float4 combined, float densityThresholdValue, float phase0RenderBiasValue)
{
	return ParticleFluidPhaseFromCombined(combined, densityThresholdValue, phase0RenderBiasValue) == 0 ? 1.0 : 0.0;
}

float ParticleFluidPhase1Mask(float4 combined, float densityThresholdValue, float phase0RenderBiasValue)
{
	return ParticleFluidPhaseFromCombined(combined, densityThresholdValue, phase0RenderBiasValue) == 1 ? 1.0 : 0.0;
}

#endif
