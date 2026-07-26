using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidPhaseDataUploader
    {
        private bool _dirty = true;

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void UploadIfDirty(
            ComputeShader compute,
            ParticleFluidSimulationResources resources,
            ParticleFluidSimulationKernels kernels,
            FluidSim2D.PhaseConfig[] phases,
            float targetDensityScale,
            float phaseSeparation,
            float[] phaseCohesionValues)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            int phaseCount = phases.Length;
            resources.EnsurePhaseBuffers(phaseCount);

            float[] interactionFlat = new float[phaseCount * phaseCount];
            for (int y = 0; y < phaseCount; y++)
            {
                for (int x = 0; x < phaseCount; x++)
                {
                    interactionFlat[y * phaseCount + x] = x == y ? 1.0f : phaseSeparation;
                }
            }

            float[] viscosities = new float[phaseCount];
            float[] viscosityTemperatureSensitivities = new float[phaseCount];
            float[] thermalExpansions = new float[phaseCount];
            float[] thermalConductivities = new float[phaseCount];
            float[] specificHeatCapacities = new float[phaseCount];
            float[] nonCoalescenceRadiusMultipliers = new float[phaseCount];
            float[] nonCoalescenceStrengths = new float[phaseCount];
            float[] targetDensities = new float[phaseCount];
            for (int i = 0; i < phaseCount; i++)
            {
                FluidSim2D.PhaseConfig phase = phases[i];
                targetDensities[i] = phase.targetDensity * targetDensityScale;
                viscosities[i] = phase.viscosity;
                viscosityTemperatureSensitivities[i] = phase.viscosityTemperatureSensitivity;
                thermalExpansions[i] = phase.thermalExpansion;
                thermalConductivities[i] = phase.thermalConductivity;
                specificHeatCapacities[i] = phase.specificHeatCapacity;
                nonCoalescenceRadiusMultipliers[i] = phase.nonCoalescenceRadiusMultiplier;
                nonCoalescenceStrengths[i] = phase.nonCoalescenceStrength;
            }

            resources.phaseTargetDensityBuffer.SetData(targetDensities);
            resources.phaseViscosityBuffer.SetData(viscosities);
            resources.phaseViscosityTemperatureSensitivityBuffer.SetData(viscosityTemperatureSensitivities);
            resources.phaseInteractionBuffer.SetData(interactionFlat);
            resources.phaseThermalExpansionBuffer.SetData(thermalExpansions);
            resources.phaseThermalConductivityBuffer.SetData(thermalConductivities);
            resources.phaseSpecificHeatCapacityBuffer.SetData(specificHeatCapacities);

            resources.EnsureNonCoalescenceBuffers(phaseCount);
            resources.phaseNonCoalescenceRadiusMultiplierBuffer.SetData(nonCoalescenceRadiusMultipliers);
            resources.phaseNonCoalescenceStrengthBuffer.SetData(nonCoalescenceStrengths);

            int triangularSize = phaseCount * (phaseCount + 1) / 2;
            resources.EnsurePhaseCohesionBuffer(triangularSize);
            if (phaseCohesionValues != null && phaseCohesionValues.Length == triangularSize)
            {
                resources.phaseCohesionBuffer.SetData(phaseCohesionValues);
            }

            kernels.BindPhaseBuffers(compute, resources);
        }
    }
}
