#ifndef FLUID_SIM_2D_PRESSURE_INCLUDED
#define FLUID_SIM_2D_PRESSURE_INCLUDED

float PressureFromDensity(float density, uint particleIndex)
{
	float target = ParticleTargetDensities[particleIndex];
	return (density - target) * pressureMultiplier;
}

float NearPressureFromDensity(float nearDensity)
{
	return nearPressureMultiplier * nearDensity;
}

float GetPhaseInteraction(uint a, uint b)
{
	return PhaseInteractionMatrix[a * NumPhases + b];
}

uint PairIndex(uint a, uint b)
{
	uint lo = min(a, b);
	uint hi = max(a, b);
	return lo * NumPhases - (lo * (lo - 1)) / 2 + (hi - lo);
}

float GetPhaseCohesion(uint a, uint b)
{
	return PhaseCohesionMatrix[PairIndex(a, b)];
}

#endif
