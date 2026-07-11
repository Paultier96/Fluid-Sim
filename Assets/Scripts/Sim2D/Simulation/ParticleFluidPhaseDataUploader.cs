using System;
using System.Linq;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidPhaseDataUploader
    {
        bool _dirty = true;

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void UploadIfDirty(
            ComputeShader compute,
            ParticleFluidSimulationResources resources,
            ParticleFluidSimulationKernels kernels,
            FluidSim2D.PhaseConfig[] phases,
            float phaseSeparation,
            float[] phaseCohesionValues,
            Func<FluidSim2D.PhaseConfig, float> resolveTargetDensity)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            Upload(compute, resources, kernels, phases, phaseSeparation, phaseCohesionValues, resolveTargetDensity);
        }

        static void Upload(
            ComputeShader compute,
            ParticleFluidSimulationResources resources,
            ParticleFluidSimulationKernels kernels,
            FluidSim2D.PhaseConfig[] phases,
            float phaseSeparation,
            float[] phaseCohesionValues,
            Func<FluidSim2D.PhaseConfig, float> resolveTargetDensity)
        {
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

            resources.phaseTargetDensityBuffer.SetData(phases.Select(resolveTargetDensity).ToArray());
            resources.phaseViscosityBuffer.SetData(phases.Select(p => p.viscosity).ToArray());
            resources.phaseViscosityTemperatureSensitivityBuffer.SetData(phases.Select(p => p.viscosityTemperatureSensitivity).ToArray());
            resources.phaseInteractionBuffer.SetData(interactionFlat);
            resources.phaseThermalExpansionBuffer.SetData(phases.Select(p => p.thermalExpansion).ToArray());
            resources.phaseThermalConductivityBuffer.SetData(phases.Select(p => p.thermalConductivity).ToArray());
            resources.phaseSpecificHeatCapacityBuffer.SetData(phases.Select(p => p.specificHeatCapacity).ToArray());

            resources.EnsureNonCoalescenceBuffers(phaseCount);
            resources.phaseNonCoalescenceRadiusMultiplierBuffer.SetData(phases.Select(p => p.nonCoalescenceRadiusMultiplier).ToArray());
            resources.phaseNonCoalescenceStrengthBuffer.SetData(phases.Select(p => p.nonCoalescenceStrength).ToArray());

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
