#ifndef PARTICLE_FLUID_PHASE_AA_INCLUDED
#define PARTICLE_FLUID_PHASE_AA_INCLUDED

float ParticleFluidShiftedPhaseT(float density0, float density1, float phase0RenderBiasValue, float phaseBlendWidthValue)
{
	float phaseRatio = ParticleFluidPhaseRatio(density0, density1);
	float phaseBoundary = ParticleFluidPhaseBoundary(phase0RenderBiasValue);
	float phaseDelta = phaseRatio - phaseBoundary;
	float phaseAA = max(0.5 * fwidth(phaseRatio) * max(phaseBlendWidthValue, 0.0001), 0.00001);
	return smoothstep(-phaseAA, phaseAA, phaseDelta);
}

#endif
