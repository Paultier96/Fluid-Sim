#ifndef FLUID_SIM_2D_SURFACE_TENSION_INCLUDED
#define FLUID_SIM_2D_SURFACE_TENSION_INCLUDED

bool IsSameSurfaceTensionRegion(uint phaseA, uint blobA, uint phaseB, uint blobB)
{
	if (surfaceTensionInterfaceMode == 1)
	{
		return phaseA == phaseB;
	}

	return IsSameBlob(blobA, blobB);
}

#endif
