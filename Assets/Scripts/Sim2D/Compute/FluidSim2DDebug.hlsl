#ifndef FLUID_SIM_2D_DEBUG_INCLUDED
#define FLUID_SIM_2D_DEBUG_INCLUDED

void ClearDebugData(uint particleIndex)
{
	DebugData[particleIndex] = 0;
}

void ClearDebugVectorData(uint particleIndex)
{
	DebugVectorData[particleIndex] = 0;
}

void ClearDebugVectorSign(uint particleIndex)
{
	DebugVectorSign[particleIndex] = 0;
}

void ClearThermalBuoyancyDebug(uint particleIndex)
{
	if (debugVectorFieldMode == 4)
	{
		ClearDebugVectorData(particleIndex);
	}
}

void ClearInactiveCsfDebugOutputs(uint particleIndex)
{
	if (debugVisualizationMode == 2)
	{
		ClearDebugData(particleIndex);
	}

	ClearDebugVectorSign(particleIndex);

	if (debugVectorFieldMode != 0 && debugVectorFieldMode != 2 && debugVectorFieldMode != 4)
	{
		ClearDebugVectorData(particleIndex);
	}
}

void ClearActiveCsfDebugOutputs(uint particleIndex)
{
	if (debugVisualizationMode == 2)
	{
		ClearDebugData(particleIndex);
	}

	if (debugVectorFieldMode != 0 && debugVectorFieldMode != 2 && debugVectorFieldMode != 4)
	{
		ClearDebugVectorData(particleIndex);
		ClearDebugVectorSign(particleIndex);
	}
}

#endif
