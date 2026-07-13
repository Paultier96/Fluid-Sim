using Seb.Helpers;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidBlobDetector
    {
        private int _stepCounter;

        public void Reset(ParticleFluidSimulationResources resources, int particleCount)
        {
            uint[] initialBlobIds = new uint[particleCount];
            resources.blobIdBuffer.SetData(initialBlobIds);
            resources.blobIdPreviousBuffer.SetData(initialBlobIds);
            _stepCounter = 0;
        }

        public void RecomputeIfDue(ComputeShader compute, ParticleFluidSimulationKernels kernels, int particleCount, int updateInterval, int propagationIterations)
        {
            int interval = Mathf.Max(1, updateInterval);
            _stepCounter++;
            if (_stepCounter % interval != 0)
            {
                return;
            }

            Recompute(compute, kernels, particleCount, propagationIterations);
        }

        static void Recompute(ComputeShader compute, ParticleFluidSimulationKernels kernels, int particleCount, int propagationIterations)
        {
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.InitializeBlobIds);
            int iterations = Mathf.Max(1, propagationIterations);
            for (int i = 0; i < iterations; i++)
            {
                ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.PropagateBlobIds);
                ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.CopyBlobIds);
            }

            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.ClearBlobSizes);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.CountBlobSizes);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.MarkSingleParticleBlobs);
            ComputeHelper.Dispatch(compute, particleCount, kernelIndex: kernels.CopyBlobIdsToPrevious);
        }
    }
}
