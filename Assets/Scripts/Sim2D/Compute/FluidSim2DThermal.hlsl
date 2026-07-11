#ifndef FLUID_SIM_2D_THERMAL_INCLUDED
#define FLUID_SIM_2D_THERMAL_INCLUDED

float GetTemperatureAdjustedViscosity(uint phase, float temperature)
{
	float baseViscosity = PhaseViscosities[phase];
	float sensitivity = PhaseViscosityTemperatureSensitivity[phase];
	float deltaT = temperature - ambientTemperature;
	float scale = clamp(1.0 - sensitivity * deltaT, 0.05, 10.0);
	return baseViscosity * scale;
}

float ViscosityKernel(float dst, float radius)
{
	return SmoothingKernelPoly6(dst, radius);
}

bool IsInsideHeatSource(float2 pos)
{
	if (heatSourceShape == 0)
	{
		float2 relPos = pos - heatSourcePos;
		float2 halfSize = heatSourceSize * 0.5;
		return abs(relPos.x) < halfSize.x && abs(relPos.y) < halfSize.y;
	}
	if (heatSourceShape == 1)
	{
		float2 radii = max(heatSourceSize * 0.5, float2(1e-6, 1e-6));
		float2 normalizedOffset = (pos - heatSourcePos) / radii;
		return dot(normalizedOffset, normalizedOffset) < 1.0;
	}
	return false;
}

float SampleHeatSourceWeight(float2 pos)
{
	if (heatSourceShape == 0)
	{
		return IsInsideHeatSource(pos) ? 1.0 : 0.0;
	}
	if (heatSourceShape == 1)
	{
		float2 radii = max(heatSourceSize * 0.5, float2(1e-6, 1e-6));
		float normalizedRadius = length((pos - heatSourcePos) / radii);
		if (normalizedRadius < 1.0)
		{
			float smoothFalloff = 1.0 - smoothstep(0.0, 1.0, normalizedRadius);
			return pow(saturate(smoothFalloff), max(heatSourceFalloffPower, 0.01));
		}
	}
	return 0.0;
}

#endif
