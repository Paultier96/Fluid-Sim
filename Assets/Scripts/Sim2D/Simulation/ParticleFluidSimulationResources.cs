using Seb.Helpers;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationResources
    {
        public ComputeBuffer positionBuffer;
        public ComputeBuffer velocityBuffer;
        public ComputeBuffer densityBuffer;
        public ComputeBuffer phaseBuffer;
        public ComputeBuffer ghostFlagBuffer;
        public ComputeBuffer blobIdBuffer;
        public ComputeBuffer temperatureBuffer;
        public ComputeBuffer debugDataBuffer;
        public ComputeBuffer debugVectorDataBuffer;
        public ComputeBuffer debugVectorSignBuffer;
        public ComputeBuffer colorGradientBuffer;
        public ComputeBuffer cellBlobSummaryBuffer;
        public ComputeBuffer blobIdScratchBuffer;
        public ComputeBuffer blobIdPreviousBuffer;
        public ComputeBuffer blobSizeBuffer;
        public ComputeBuffer predictedPositionBuffer;

        public ComputeBuffer sortTargetPosition;
        public ComputeBuffer sortTargetPredictedPosition;
        public ComputeBuffer sortTargetVelocity;
        public ComputeBuffer sortTargetPhases;
        public ComputeBuffer sortTargetGhostFlags;
        public ComputeBuffer sortTargetBlobIds;
        public ComputeBuffer sortTargetTemperatures;
        public ComputeBuffer sortTargetParticleTargetDensities;

        public ComputeBuffer phaseTargetDensityBuffer;
        public ComputeBuffer phaseViscosityBuffer;
        public ComputeBuffer phaseViscosityTemperatureSensitivityBuffer;
        public ComputeBuffer phaseInteractionBuffer;
        public ComputeBuffer particleTargetDensityBuffer;
        public ComputeBuffer phaseThermalExpansionBuffer;
        public ComputeBuffer phaseThermalConductivityBuffer;
        public ComputeBuffer phaseSpecificHeatCapacityBuffer;
        public ComputeBuffer phaseCohesionBuffer;
        public ComputeBuffer phaseNonCoalescenceRadiusMultiplierBuffer;
        public ComputeBuffer phaseNonCoalescenceStrengthBuffer;

        public void AllocateParticleBuffers(int particleCount)
        {
            positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            phaseBuffer = ComputeHelper.CreateStructuredBuffer<int>(particleCount);
            ghostFlagBuffer = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            blobIdBuffer = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            blobIdScratchBuffer = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            blobIdPreviousBuffer = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            blobSizeBuffer = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
            debugDataBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            debugVectorDataBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            debugVectorSignBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
            colorGradientBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            cellBlobSummaryBuffer = ComputeHelper.CreateStructuredBuffer<uint2>(particleCount);
            particleTargetDensityBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        }

        public void AllocateSortBuffers(int particleCount)
        {
            sortTargetPosition = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            sortTargetPredictedPosition = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            sortTargetVelocity = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
            sortTargetPhases = ComputeHelper.CreateStructuredBuffer<int>(particleCount);
            sortTargetGhostFlags = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            sortTargetBlobIds = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
            sortTargetTemperatures = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
            sortTargetParticleTargetDensities = ComputeHelper.CreateStructuredBuffer<float>(particleCount);
        }

        public void EnsurePhaseBuffers(int phaseCount)
        {
            ComputeHelper.Release(
                phaseTargetDensityBuffer,
                phaseViscosityBuffer,
                phaseViscosityTemperatureSensitivityBuffer,
                phaseInteractionBuffer,
                phaseThermalExpansionBuffer,
                phaseThermalConductivityBuffer,
                phaseSpecificHeatCapacityBuffer,
                phaseCohesionBuffer
                );
            phaseCohesionBuffer = null;
            phaseTargetDensityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityTemperatureSensitivityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseInteractionBuffer = new ComputeBuffer(phaseCount * phaseCount, sizeof(float));
            phaseThermalExpansionBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseThermalConductivityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseSpecificHeatCapacityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
        }

        public void EnsureNonCoalescenceBuffers(int phaseCount)
        {
            ComputeHelper.Release(phaseNonCoalescenceRadiusMultiplierBuffer, phaseNonCoalescenceStrengthBuffer);
            phaseNonCoalescenceRadiusMultiplierBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceStrengthBuffer = new ComputeBuffer(phaseCount, sizeof(float));
        }

        public void EnsurePhaseCohesionBuffer(int triangularSize)
        {
            if (phaseCohesionBuffer != null && phaseCohesionBuffer.count == triangularSize)
            {
                return;
            }

            ComputeHelper.Release(phaseCohesionBuffer);
            phaseCohesionBuffer = new ComputeBuffer(triangularSize, sizeof(float));
        }

        public void Release()
        {
            ComputeHelper.Release(
                positionBuffer,
                predictedPositionBuffer,
                velocityBuffer,
                densityBuffer,
                phaseBuffer,
                ghostFlagBuffer,
                blobIdBuffer,
                blobIdScratchBuffer,
                blobIdPreviousBuffer,
                blobSizeBuffer,
                temperatureBuffer,
                sortTargetPosition,
                sortTargetVelocity,
                sortTargetPredictedPosition,
                sortTargetPhases,
                sortTargetGhostFlags,
                sortTargetBlobIds,
                sortTargetTemperatures,
                phaseInteractionBuffer,
                phaseViscosityBuffer,
                phaseViscosityTemperatureSensitivityBuffer,
                phaseTargetDensityBuffer,
                particleTargetDensityBuffer,
                phaseThermalExpansionBuffer,
                phaseThermalConductivityBuffer,
                phaseSpecificHeatCapacityBuffer,
                sortTargetParticleTargetDensities,
                phaseCohesionBuffer,
                phaseNonCoalescenceRadiusMultiplierBuffer,
                phaseNonCoalescenceStrengthBuffer,
                debugDataBuffer,
                debugVectorDataBuffer,
                debugVectorSignBuffer,
                colorGradientBuffer,
                cellBlobSummaryBuffer);
        }
    }
}
