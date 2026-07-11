using Seb.Helpers;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationKernels
    {
        public int ExternalForces { get; private set; }
        public int SpatialHash { get; private set; }
        public int Reorder { get; private set; }
        public int Copyback { get; private set; }
        public int Density { get; private set; }
        public int Pressure { get; private set; }
        public int Viscosity { get; private set; }
        public int ThermalBuoyancy { get; private set; }
        public int UpdatePosition { get; private set; }
        public int UpdateThermalExpansion { get; private set; }
        public int UpdateTemperature { get; private set; }
        public int Cohesion { get; private set; }
        public int CarrierWedge { get; private set; }
        public int Csf { get; private set; }
        public int ComputeColorGradient { get; private set; }
        public int InitializeBlobIds { get; private set; }
        public int PropagateBlobIds { get; private set; }
        public int CopyBlobIds { get; private set; }
        public int CopyBlobIdsToPrevious { get; private set; }
        public int ClearBlobSizes { get; private set; }
        public int CountBlobSizes { get; private set; }
        public int MarkSingleParticleBlobs { get; private set; }

        public void Resolve(ComputeShader compute)
        {
            ExternalForces = compute.FindKernel("ExternalForces");
            SpatialHash = compute.FindKernel("UpdateSpatialHash");
            Reorder = compute.FindKernel("Reorder");
            Copyback = compute.FindKernel("ReorderCopyback");
            Density = compute.FindKernel("CalculateDensities");
            Pressure = compute.FindKernel("CalculatePressureForce");
            Viscosity = compute.FindKernel("CalculateViscosity");
            ThermalBuoyancy = compute.FindKernel("ApplyThermalBuoyancy");
            UpdatePosition = compute.FindKernel("UpdatePositions");
            UpdateThermalExpansion = compute.FindKernel("UpdateThermalExpansion");
            UpdateTemperature = compute.FindKernel("UpdateTemperature");
            Cohesion = compute.FindKernel("CalculateCohesion");
            CarrierWedge = compute.FindKernel("ApplyCarrierWedgeForce");
            Csf = compute.FindKernel("CalculateCSF");
            ComputeColorGradient = compute.FindKernel("ComputeColorGradients");
            InitializeBlobIds = compute.FindKernel("InitializeBlobIDs");
            PropagateBlobIds = compute.FindKernel("PropagateBlobIDs");
            CopyBlobIds = compute.FindKernel("CopyBlobIDs");
            CopyBlobIdsToPrevious = compute.FindKernel("CopyBlobIDsToPrevious");
            ClearBlobSizes = compute.FindKernel("ClearBlobSizes");
            CountBlobSizes = compute.FindKernel("CountBlobSizes");
            MarkSingleParticleBlobs = compute.FindKernel("MarkSingleParticleBlobs");
        }

        public void BindParticleBuffers(ComputeShader compute, ParticleFluidSimulationResources resources)
        {
            ComputeHelper.SetBuffer(compute, resources.positionBuffer, "Positions", ExternalForces, UpdatePosition, Copyback);
            ComputeHelper.SetBuffer(compute, resources.positionBuffer, "PositionsRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.predictedPositionBuffer, "PredictedPositions", ExternalForces, SpatialHash, Density, Pressure, Viscosity, ThermalBuoyancy, UpdateTemperature, Cohesion, CarrierWedge, ComputeColorGradient, Csf, Copyback, InitializeBlobIds, PropagateBlobIds);
            ComputeHelper.SetBuffer(compute, resources.predictedPositionBuffer, "PredictedPositionsRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.velocityBuffer, "Velocities", ExternalForces, Pressure, Viscosity, ThermalBuoyancy, Cohesion, CarrierWedge, Csf, UpdatePosition, Copyback);
            ComputeHelper.SetBuffer(compute, resources.velocityBuffer, "VelocitiesRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.densityBuffer, "Densities", Density, Pressure, ComputeColorGradient);
            ComputeHelper.SetBuffer(compute, resources.densityBuffer, "DensitiesRO", Csf);
            ComputeHelper.SetBuffer(compute, resources.phaseBuffer, "Phases", ExternalForces, Density, Pressure, Viscosity, ThermalBuoyancy, UpdateTemperature, Cohesion, CarrierWedge, ComputeColorGradient, UpdatePosition, Copyback, UpdateThermalExpansion, CountBlobSizes, MarkSingleParticleBlobs, InitializeBlobIds, PropagateBlobIds);
            ComputeHelper.SetBuffer(compute, resources.phaseBuffer, "PhasesRO", Reorder, Csf);
            ComputeHelper.SetBuffer(compute, resources.ghostFlagBuffer, "IsGhost", ExternalForces, Pressure, Viscosity, ThermalBuoyancy, UpdateTemperature, UpdatePosition, Cohesion, CarrierWedge, Csf, UpdateThermalExpansion, Copyback);
            ComputeHelper.SetBuffer(compute, resources.ghostFlagBuffer, "IsGhostRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.temperatureBuffer, "Temperatures", Viscosity, UpdateTemperature, UpdateThermalExpansion, Copyback);
            ComputeHelper.SetBuffer(compute, resources.temperatureBuffer, "TemperaturesRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.particleTargetDensityBuffer, "ParticleTargetDensities", Pressure, ThermalBuoyancy, UpdateThermalExpansion, Copyback);
            ComputeHelper.SetBuffer(compute, resources.particleTargetDensityBuffer, "ParticleTargetDensitiesRO", Reorder);
            ComputeHelper.SetBuffer(compute, resources.blobIdBuffer, "BlobIDs", InitializeBlobIds, PropagateBlobIds, CopyBlobIds, Pressure, Cohesion, CopyBlobIdsToPrevious, CountBlobSizes, MarkSingleParticleBlobs, Copyback);
            ComputeHelper.SetBuffer(compute, resources.blobIdBuffer, "BlobIDsRO", Reorder, Viscosity, CarrierWedge, Csf, ComputeColorGradient);
            ComputeHelper.SetBuffer(compute, resources.blobIdScratchBuffer, "BlobIDsScratch", PropagateBlobIds, CopyBlobIds);
            ComputeHelper.SetBuffer(compute, resources.blobSizeBuffer, "BlobSizes", ClearBlobSizes, CountBlobSizes, MarkSingleParticleBlobs);
            ComputeHelper.SetBuffer(compute, resources.blobIdPreviousBuffer, "BlobIDsPrevious", InitializeBlobIds, PropagateBlobIds, CopyBlobIdsToPrevious);
        }

        public void BindSpatialHashBuffers(ComputeShader compute, SpatialHash spatialHash)
        {
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", Reorder);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", Density, Pressure, Viscosity, ThermalBuoyancy, UpdateTemperature, Cohesion, CarrierWedge, ComputeColorGradient, PropagateBlobIds);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", SpatialHash, Density, Pressure, Viscosity, ThermalBuoyancy, UpdateTemperature, Cohesion, CarrierWedge, ComputeColorGradient, PropagateBlobIds);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsetsRO", Csf);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeysRO", Csf);
        }

        public void BindSortBuffers(ComputeShader compute, ParticleFluidSimulationResources resources)
        {
            ComputeHelper.SetBuffer(compute, resources.sortTargetPosition, "SortTarget_Positions", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetPosition, "SortTarget_PositionsRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetPredictedPosition, "SortTarget_PredictedPositions", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetPredictedPosition, "SortTarget_PredictedPositionsRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetVelocity, "SortTarget_Velocities", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetVelocity, "SortTarget_VelocitiesRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetPhases, "SortTarget_Phases", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetPhases, "SortTarget_PhasesRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetTemperatures, "SortTarget_Temperatures", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetTemperatures, "SortTarget_TemperaturesRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetGhostFlags, "SortTarget_IsGhost", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetGhostFlags, "SortTarget_IsGhostRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetBlobIds, "SortTarget_BlobIDs", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetBlobIds, "SortTarget_BlobIDsRO", Copyback);
            ComputeHelper.SetBuffer(compute, resources.sortTargetParticleTargetDensities, "SortTarget_ParticleTargetDensities", Reorder);
            ComputeHelper.SetBuffer(compute, resources.sortTargetParticleTargetDensities, "SortTarget_ParticleTargetDensitiesRO", Copyback);
        }

        public void BindDebugBuffers(ComputeShader compute, ParticleFluidSimulationResources resources)
        {
            ComputeHelper.SetBuffer(compute, resources.debugDataBuffer, "DebugData", ExternalForces, Viscosity, ThermalBuoyancy, Cohesion, ComputeColorGradient, Csf);
            ComputeHelper.SetBuffer(compute, resources.debugVectorDataBuffer, "DebugVectorData", ThermalBuoyancy, CarrierWedge, Csf);
            ComputeHelper.SetBuffer(compute, resources.debugVectorSignBuffer, "DebugVectorSign", Csf);
            ComputeHelper.SetBuffer(compute, resources.colorGradientBuffer, "ColorGradients", ComputeColorGradient);
            ComputeHelper.SetBuffer(compute, resources.colorGradientBuffer, "ColorGradientsRO", Viscosity, CarrierWedge, Csf);
        }

        public void BindPhaseBuffers(ComputeShader compute, ParticleFluidSimulationResources resources)
        {
            ComputeHelper.SetBuffer(compute, resources.phaseTargetDensityBuffer, "PhaseTargetDensities", ThermalBuoyancy, UpdateThermalExpansion);
            ComputeHelper.SetBuffer(compute, resources.phaseViscosityBuffer, "PhaseViscosities", Viscosity);
            ComputeHelper.SetBuffer(compute, resources.phaseViscosityTemperatureSensitivityBuffer, "PhaseViscosityTemperatureSensitivity", Viscosity);
            ComputeHelper.SetBuffer(compute, resources.phaseInteractionBuffer, "PhaseInteractionMatrix", Pressure);
            ComputeHelper.SetBuffer(compute, resources.phaseThermalExpansionBuffer, "PhaseThermalExpansion", UpdateThermalExpansion);
            ComputeHelper.SetBuffer(compute, resources.phaseThermalConductivityBuffer, "PhaseThermalConductivity", UpdateTemperature);
            ComputeHelper.SetBuffer(compute, resources.phaseSpecificHeatCapacityBuffer, "PhaseSpecificHeatCapacity", UpdateTemperature);
            ComputeHelper.SetBuffer(compute, resources.phaseNonCoalescenceRadiusMultiplierBuffer, "PhaseNonCoalescenceRadiusMultiplier", Csf);
            ComputeHelper.SetBuffer(compute, resources.phaseNonCoalescenceStrengthBuffer, "PhaseNonCoalescenceStrength", Csf);
            ComputeHelper.SetBuffer(compute, resources.phaseCohesionBuffer, "PhaseCohesionMatrix", Cohesion);
        }
    }
}
