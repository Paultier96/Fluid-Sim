using Seb.Helpers;
using System;
using Seb.Fluid2D.Rendering;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationDebug
    {
        public float GetMaxViscosity(FluidSim2D.PhaseConfig[] phases, float ambientTemperature, float heatSourceTemperature)
        {
            float maxViscosity = 0;
            if (phases != null)
            {
                foreach (FluidSim2D.PhaseConfig phase in phases)
                {
                    if (phase == null)
                    {
                        continue;
                    }

                    maxViscosity = Mathf.Max(maxViscosity, GetTemperatureAdjustedViscosity(phase, ambientTemperature, ambientTemperature));
                    maxViscosity = Mathf.Max(maxViscosity, GetTemperatureAdjustedViscosity(phase, heatSourceTemperature, ambientTemperature));
                }
            }

            return Mathf.Max(maxViscosity, 0.0001f);
        }

        public void RefreshBuffers(
            ComputeShader compute,
            ParticleFluidSimulationKernels kernels,
            int particleCount,
            ComputeBuffer positionBuffer,
            Action uploadPhaseDataIfDirty,
            Action<float> updateSettings,
            Action runSpatial)
        {
            if (!Application.isPlaying || compute == null || particleCount <= 0 || positionBuffer == null)
            {
                return;
            }

            uploadPhaseDataIfDirty?.Invoke();
            updateSettings?.Invoke(0f);
            runSpatial?.Invoke();
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.Density);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.ComputeColorGradient);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.ThermalBuoyancy);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.Viscosity);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.CarrierWedge);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.Csf);
        }

        public void UploadSettings(ComputeShader compute, ParticleDisplay2D display)
        {
            compute.SetInt("debugVisualizationMode", display != null ? (int)display.debugMode : 0);
            compute.SetInt("debugVectorFieldMode", display != null ? display.ComputeVectorFieldMode : 0);
        }

        static float GetTemperatureAdjustedViscosity(FluidSim2D.PhaseConfig phase, float temperature, float ambientTemperature)
        {
            return phase.viscosity * Mathf.Clamp(1.0f - phase.viscosityTemperatureSensitivity * (temperature - ambientTemperature), 0.05f, 10.0f);
        }
    }
}
