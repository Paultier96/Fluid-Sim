using Seb.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

        public enum LiquidPhase
        {
            Wax = 0,
            Water = 1
        }

        public enum LiquidPhaseFilter
        {
            All = -1,
            Wax = 0,
            Water = 1
        }

        [Header("Simulation Settings")]
        public float timeScale = 1;
        public float maxTimestepFPS = 60;
        public int iterationsPerFrame;
        public float gravity;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public float pressureMultiplier;
        public float nearPressureMultiplier;
        [Tooltip("Multiplier applied to viscous velocity exchange across phase boundaries and between separate same-phase blobs. Lower values make interfaces more slippery.")]
        [Range(0f, 1f)] public float interfaceViscosityMultiplier = 1f;
        public Vector2 boundsSize;
        public Vector2 obstacleSize;
        public Vector2 obstacleCentre;
        [Tooltip("magnitude of repulsive acceleration at zero distance")]
        public float edgeForce;
        [Tooltip("distance from boundary over which repulsion fades to zero")]
        public float edgeForceDst;
        
        [Header("Boundary Pressure Support")]
        [Tooltip("Strength of wall-support pressure term used to compensate for missing neighbours near boundaries.")]
        [Min(0f)] public float wallPressureStrength = 0f;
        [Tooltip("Distance from boundary over which wall-pressure support is applied. Set to 0 to use smoothing radius.")]
        [Min(0f)] public float wallPressureRadius = 0f;

        [Header("Bounds Type")]
        public bool useEllipticalBounds = false;
        public Vector2 ellipseBoundsSize = new Vector2(10, 8);
        public Vector2 ellipseBoundsCenter = Vector2.zero;

        [Header("Interaction Settings")]
        public float interactionRadius;
        public float interactionStrength;
        
        [Header("Buoyancy")]
        [Tooltip("Scales thermal buoyancy inversion based on local density contrast.")]
        public float buoyancyInversionStrength = 1.0f;
        [Tooltip("Clamp for normalized density contrast used by thermal buoyancy to prevent spikes.")]
        public float buoyancyInversionClamp = 1.0f;

        [Header("Phases")]
        public PhaseConfig[] phases;
        
        [Header("Ghost Particles")]
        [Tooltip("Liquid phase assigned to ghost boundary particles. Clamped to valid phase range at runtime.")]
        public LiquidPhase ghostPhase = LiquidPhase.Water;

        [System.Serializable]
        public class PhaseConfig
        {
            public string name = "Water";
            public Gradient colourMap;
            public float targetDensity = 234;
            public float viscosity = 0.03f;
            [Tooltip("Fractional viscosity change per degree relative to ambient temperature. Positive values make hot fluid less viscous and cold fluid more viscous.")]
            public float viscosityTemperatureSensitivity = 0.0f;
            public float thermalExpansion = 0.0f;
            public float surfaceTension = 0f;
            [Tooltip("Thermal conductivity controls heat diffusion rate between particles. Higher values = faster heat spreading.")]
            public float thermalConductivity = 0.5f;
            [Tooltip("Specific heat capacity: how much energy is required to raise temperature by one degree. Higher values = more thermal inertia (slower heating).")]
            [Min(0.01f)] public float specificHeatCapacity = 1.0f;
            // Non-coalescence tuning per-phase
            //[HideInInspector]
            [Tooltip("Short-range radius (in world units) within which non-coalescence repulsion acts")]
            public float nonCoalescenceRadius = 0.2f;

            //[HideInInspector]
            [Tooltip("Multiplier for non-coalescence repulsion (scaled by phase surface tension)")]
            public float nonCoalescenceStrength = 1.0f;
        }

        [Range(0f, 10f)]
        public float phaseSeparation = 0.3f;

        public float[] phaseCohesionValues = new float[] { 0.5f, -0.1f, 0.5f };
        [Tooltip("Cohesion override for same-phase particles that belong to different blobs. Negative values repel.")]
        public float blobBlobCohesion = -0.1f;
        [Tooltip("Minimum separation to preserve a resolved carrier-liquid film between same-phase blobs. Set to 0 to disable.")]
        [Min(0f)] public float blobFilmSeparationDistance = 0f;
        [Tooltip("Repulsion strength used when different same-phase blobs are closer than the film separation distance.")]
        [Min(0f)] public float blobFilmSeparationStrength = 0f;
        [Tooltip("Carrier phase pushed into the gap between two nearby blob IDs. For the lava lamp setup this is usually the water phase.")]
        public LiquidPhase carrierWedgePhase = LiquidPhase.Water;
        [Tooltip("Search distance for pushing carrier fluid into wax-blob gaps. The shader caps this at 2.5x smoothing radius.")]
        [Min(0f)] public float carrierWedgeDistance = 0f;
        [Tooltip("Acceleration strength that pushes carrier fluid into the gap between two different blob IDs.")]
        [Min(0f)] public float carrierWedgeStrength = 0f;
        [Tooltip("Multiplier applied to carrier wedge strength inside the blob merge coil area. 0 disables the wedge in the coil, 1 leaves it unchanged.")]
        [Range(0f, 1f)] public float carrierWedgeCoilStrengthMultiplier = 0f;
        [Tooltip("Optional cap for carrier wedge acceleration. Set to 0 for no cap.")]
        [Min(0f)] public float carrierWedgeMaxAcceleration = 0f;
        [Tooltip("Maximum dot product between directions to the two blobs. Lower values require a clearer V-shaped gap.")]
        [Range(-1f, 1f)] public float carrierWedgeMaxDirectionDot = 0.5f;

        [Header("Blob Connectivity")]
        [Tooltip("Simulation steps between blob-ID recomputation. 1 = every step.")]
        [Min(1)] public int blobIdUpdateInterval = 1;
        [Tooltip("Label-propagation passes used when recomputing blob IDs.")]
        [Min(1)] public int blobPropagationIterations = 8;
        [Tooltip("When enabled, existing blobs can split anywhere, but different blobs only merge inside the configured coil area.")]
        public bool restrictBlobMergingToCoil = false;
        [Tooltip("Area where separate blobs are allowed to merge. If unset, the heat source area is used.")]
        public HeatSource2D blobMergeCoil;
        [Tooltip("Damping applied to velocity moving away from the merge coil center. Useful for making wax linger without resisting entry.")]
        [Min(0f)] public float coilVelocityDamping = 0f;
        [Tooltip("Acceleration toward the merge coil center for the selected phase. Set to 0 to disable.")]
        [Min(0f)] public float coilAttractionStrength = 0f;
        [Tooltip("Phase affected by coil velocity damping and attraction.")]
        public LiquidPhaseFilter coilVelocityDampingPhase = LiquidPhaseFilter.Wax;

        [Header("Surface Tension")]
        public float surfaceTensionThreshold = 0.1f;
        //[Min(0f)]
        public float blobBlobSurfaceTension = 800f;
        [Tooltip("Extra blob-ID based surface tension. This tries to minimize each blob's own perimeter, independent of the surrounding phase. Set to 0 to disable.")]
        [Min(0f)] public float blobSelfSurfaceTension = 0f;
        [Tooltip("Phase affected by blob self surface tension.")]
        public LiquidPhaseFilter blobSelfSurfaceTensionPhase = LiquidPhaseFilter.All;
        [Min(0f)] public float maxSurfaceTensionCurvature = 10f;

        // ADDED: temperature settings
        [Header("Temperature")]
        public float ambientTemperature = 20f;
        [Tooltip("Multiplier for heat transfer to ghost particles at boundaries. Higher = faster cooling at walls.")]
        [Min(0.1f)] public float ghostCoolingMultiplier = 1.0f;
        [Tooltip("Global Newton cooling toward ambient temperature. Set to 0 to disable.")]
        [Min(0f)] public float ambientCoolingRate = 0f;
        [Tooltip("Extra cooling toward ambient for particles close to the simulation bounds.")]
        [Min(0f)] public float wallCoolingRate = 0f;
        [Tooltip("Distance from the bounds over which wall cooling fades. Set to 0 to use the smoothing radius.")]
        [Min(0f)] public float wallCoolingDistance = 0f;

        [Header("Heat Source")]
        public HeatSource2D heatSource;

        public float HeatSourceTemperature => heatSource != null && heatSource.isActiveAndEnabled ? heatSource.temperature : ambientTemperature;
        public float MaxDebugCurvature => Mathf.Max(maxSurfaceTensionCurvature, 0.0001f);
        public float MaxDebugSurfaceTensionForce
        {
            get
            {
                float maxSurfaceTension = Mathf.Max(Mathf.Abs(blobBlobSurfaceTension), Mathf.Abs(blobSelfSurfaceTension));
                if (phases != null)
                {
                    for (int i = 0; i < phases.Length; i++)
                    {
                        if (phases[i] != null)
                        {
                            maxSurfaceTension = Mathf.Max(maxSurfaceTension, Mathf.Abs(phases[i].surfaceTension));
                        }
                    }
                }

                return Mathf.Max(maxSurfaceTension * MaxDebugCurvature, 0.0001f);
            }
        }

        public float MaxDebugConvection => Mathf.Max(Mathf.Abs(gravity) * buoyancyInversionStrength * Mathf.Max(0f, buoyancyInversionClamp), 0.0001f);


        public float MaxDebugViscosity
        {
            get
            {
                if (phases == null || phases.Length == 0)
                {
                    return 1f;
                }

                float maxViscosity = 0;
                float heatSourceTemperature = HeatSourceTemperature;
                for (int i = 0; i < phases.Length; i++)
                {
                    if (phases[i] == null)
                    {
                        continue;
                    }

                    maxViscosity = Mathf.Max(maxViscosity, GetTemperatureAdjustedViscosity(phases[i], ambientTemperature));
                    maxViscosity = Mathf.Max(maxViscosity, GetTemperatureAdjustedViscosity(phases[i], heatSourceTemperature));
                }
                return Mathf.Max(maxViscosity, 0.0001f);
            }
        }

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D spawner2D;

        // Buffers
        public ComputeBuffer positionBuffer { get; private set; }
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer phaseBuffer { get; private set; }
        public ComputeBuffer ghostFlagBuffer { get; private set; }
        public ComputeBuffer blobIdBuffer { get; private set; }
        public ComputeBuffer temperatureBuffer { get; private set; }
        public ComputeBuffer debugDataBuffer { get; private set; }
        public ComputeBuffer debugVectorDataBuffer { get; private set; }
        public ComputeBuffer debugVectorSignBuffer { get; private set; }
        public ComputeBuffer colorGradientBuffer { get; private set; }
        ComputeBuffer blobIdScratchBuffer;
        ComputeBuffer blobIdPreviousBuffer;
        ComputeBuffer blobSizeBuffer;

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_Phases;
        ComputeBuffer sortTarget_GhostFlags;
        ComputeBuffer sortTarget_BlobIds;
        ComputeBuffer sortTarget_Temperatures;

        ComputeBuffer phaseTargetDensityBuffer;
        ComputeBuffer phaseViscosityBuffer;
        ComputeBuffer phaseViscosityTemperatureSensitivityBuffer;
        ComputeBuffer phaseInteractionBuffer;
        ComputeBuffer particleTargetDensityBuffer;
        ComputeBuffer phaseThermalExpansionBuffer;
        ComputeBuffer phaseThermalConductivityBuffer;
        ComputeBuffer phaseSpecificHeatCapacityBuffer;
        ComputeBuffer sortTarget_ParticleTargetDensities;
        ComputeBuffer phaseCohesionBuffer;
        ComputeBuffer phaseSurfaceTensionBuffer;
        ComputeBuffer phaseNonCoalescenceRadiusBuffer;
        ComputeBuffer phaseNonCoalescenceStrengthBuffer;

        ComputeBuffer predictedPositionBuffer;
        SpatialHash spatialHash;

        public float[,] interactionMatrix = new float[,]
        {
            { 1.0f, 0.3f },
            { 0.3f, 1.0f }
        };

        // Kernel IDs (resolved at runtime via FindKernel)
        int externalForcesKernel;
        int spatialHashKernel;
        int reorderKernel;
        int copybackKernel;
        int densityKernel;
        int pressureKernel;
        int viscosityKernel;
        int thermalBuoyancyKernel;
        int updatePositionKernel;
        int updateThermalExpansionKernel;
        int updateTemperatureKernel;
        int cohesionKernel;
        int carrierWedgeKernel;
        int csfKernel;
        int computeColorGradKernel;
        int initializeBlobIdsKernel;
        int propagateBlobIdsKernel;
        int copyBlobIdsKernel;
        int copyBlobIdsToPreviousKernel;
        int clearBlobSizesKernel;
        int countBlobSizesKernel;
        int markSingleParticleBlobsKernel;

        // State
        bool isPaused;
        Spawner2D.ParticleSpawnData spawnData;
        List<float2> ghostPositions;
        List<float2> ghostVelocities;
        List<int> ghostPhases;
        bool pauseNextFrame;
        float2[] velocityReadback;
        float2[] densityReadback;
        float[] targetDensityReadback;
        int blobStepCounter;

        public int numParticles { get; private set; }
        public int numFluidParticles { get; private set; }
        public int numGhostParticles { get; private set; }
        int resolvedGhostPhase;

        // Runtime-change tracking
        Rendering.ParticleDisplay2D particleDisplay;

        void Start()
        {
            externalForcesKernel = compute.FindKernel("ExternalForces");
            spatialHashKernel = compute.FindKernel("UpdateSpatialHash");
            reorderKernel = compute.FindKernel("Reorder");
            copybackKernel = compute.FindKernel("ReorderCopyback");
            densityKernel = compute.FindKernel("CalculateDensities");
            pressureKernel = compute.FindKernel("CalculatePressureForce");
            viscosityKernel = compute.FindKernel("CalculateViscosity");
            thermalBuoyancyKernel = compute.FindKernel("ApplyThermalBuoyancy");
            updatePositionKernel = compute.FindKernel("UpdatePositions");
            updateThermalExpansionKernel = compute.FindKernel("UpdateThermalExpansion");
            updateTemperatureKernel = compute.FindKernel("UpdateTemperature");
            cohesionKernel = compute.FindKernel("CalculateCohesion");
            carrierWedgeKernel = compute.FindKernel("ApplyCarrierWedgeForce");
            csfKernel = compute.FindKernel("CalculateCSF");
            computeColorGradKernel = compute.FindKernel("ComputeColorGradients");
            initializeBlobIdsKernel = compute.FindKernel("InitializeBlobIDs");
            propagateBlobIdsKernel = compute.FindKernel("PropagateBlobIDs");
            copyBlobIdsKernel = compute.FindKernel("CopyBlobIDs");
            copyBlobIdsToPreviousKernel = compute.FindKernel("CopyBlobIDsToPrevious");
            clearBlobSizesKernel = compute.FindKernel("ClearBlobSizes");
            countBlobSizesKernel = compute.FindKernel("CountBlobSizes");
            markSingleParticleBlobsKernel = compute.FindKernel("MarkSingleParticleBlobs");

            particleDisplay = GetComponent<Rendering.ParticleDisplay2D>();
            if (phases != null)
                particleDisplay?.SetPhaseColors(phases.Select(p => p.colourMap).ToArray());
            if (phases == null || phases.Length == 0)
                throw new InvalidOperationException("At least one phase is required.");

            if (heatSource == null)
                heatSource = FindObjectOfType<HeatSource2D>();

            float deltaTime = 1 / 60f;
            Time.fixedDeltaTime = deltaTime;

            spawnData = spawner2D.GetSpawnData();
            numFluidParticles = spawnData.positions.Length;

            // Calculate fluid particle spacing
            float fluidSpacing = Mathf.Sqrt(1f / 400);
            
            // Calculate number of ghost layers needed to cover one smoothing radius
            int numGhostLayers = Mathf.CeilToInt(smoothingRadius / fluidSpacing);

            // Generate ghost particles with proper layering
            ghostPositions = new List<float2>();
            ghostVelocities = new List<float2>();
            ghostPhases = new List<int>();
            resolvedGhostPhase = ClampPhaseIndex(ghostPhase);
            spawner2D.GenerateGhostParticles(boundsSize, ellipseBoundsCenter, ellipseBoundsSize, useEllipticalBounds, fluidSpacing, numGhostLayers, resolvedGhostPhase, ghostPositions, ghostVelocities, ghostPhases, obstacleSize, obstacleCentre);
            numGhostParticles = ghostPositions.Count;
            numParticles = numFluidParticles + numGhostParticles;

            Debug.Log($"Fluid particles: {numFluidParticles}, Ghost particles: {numGhostParticles}, Total: {numParticles}");
            Debug.Log($"Fluid spacing: {fluidSpacing:F3}, Smoothing radius: {smoothingRadius:F3}, Ghost layers: {numGhostLayers}");

            spatialHash = new SpatialHash(numParticles);

            // Create buffers
            positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            phaseBuffer = ComputeHelper.CreateStructuredBuffer<int>(numParticles);
            ghostFlagBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            blobIdBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            blobIdScratchBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            blobIdPreviousBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            blobSizeBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numFluidParticles; i++)
                initialTemps[i] = ambientTemperature;
            for (int i = numFluidParticles; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);
            //debug
            debugDataBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            debugVectorDataBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            debugVectorSignBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            // Color gradients for CSF
            colorGradientBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

            particleTargetDensityBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);

            // Initialize to base phase densities for fluid particles; ghosts get rest density
            float[] initialTargetDensities = new float[numParticles];
            for (int i = 0; i < numFluidParticles; i++)
                initialTargetDensities[i] = phases[spawnData.phases[i]].targetDensity;
            for (int i = numFluidParticles; i < numParticles; i++)
                initialTargetDensities[i] = phases[resolvedGhostPhase].targetDensity;
            particleTargetDensityBuffer.SetData(initialTargetDensities);


            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Phases = ComputeHelper.CreateStructuredBuffer<int>(numParticles);
            sortTarget_GhostFlags = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            sortTarget_BlobIds = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            sortTarget_Temperatures = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            sortTarget_ParticleTargetDensities = ComputeHelper.CreateStructuredBuffer<float>(numParticles);

            CreateOrUpdatePhaseBuffers(initial: true);

            SetInitialBufferData(spawnData);

            BindParticleBuffers();
            BindSpatialHashBuffers();
            BindSortBuffers();
            BindDebugBuffers();

            compute.SetInt("numParticles", numParticles);
            compute.SetInt("numSpatialParticles", numParticles);
            compute.SetInt("NumPhases", phases.Length);
            SettleSimulation();
        }

        void BindParticleBuffers()
        {
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, positionBuffer, "PositionsRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, carrierWedgeKernel, computeColorGradKernel, csfKernel, copybackKernel, initializeBlobIdsKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositionsRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, cohesionKernel, carrierWedgeKernel, csfKernel, updatePositionKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "VelocitiesRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "DensitiesRO", csfKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "Phases", densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, carrierWedgeKernel, computeColorGradKernel, updatePositionKernel, copybackKernel, updateThermalExpansionKernel, countBlobSizesKernel, markSingleParticleBlobsKernel, initializeBlobIdsKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "PhasesRO", reorderKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, ghostFlagBuffer, "IsGhost", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, updatePositionKernel, cohesionKernel, carrierWedgeKernel, csfKernel, updateThermalExpansionKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, ghostFlagBuffer, "IsGhostRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", viscosityKernel, updateTemperatureKernel, updateThermalExpansionKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, temperatureBuffer, "TemperaturesRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, particleTargetDensityBuffer, "ParticleTargetDensities", pressureKernel, thermalBuoyancyKernel, updateThermalExpansionKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, particleTargetDensityBuffer, "ParticleTargetDensitiesRO", reorderKernel);
            ComputeHelper.SetBuffer(compute, blobIdBuffer, "BlobIDs", initializeBlobIdsKernel, propagateBlobIdsKernel, copyBlobIdsKernel, pressureKernel, cohesionKernel, copyBlobIdsToPreviousKernel, countBlobSizesKernel, markSingleParticleBlobsKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, blobIdBuffer, "BlobIDsRO", reorderKernel, viscosityKernel, carrierWedgeKernel, csfKernel, computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, blobIdScratchBuffer, "BlobIDsScratch", propagateBlobIdsKernel, copyBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, blobSizeBuffer, "BlobSizes", clearBlobSizesKernel, countBlobSizesKernel, markSingleParticleBlobsKernel);
            ComputeHelper.SetBuffer(compute, blobIdPreviousBuffer, "BlobIDsPrevious", initializeBlobIdsKernel, propagateBlobIdsKernel, copyBlobIdsToPreviousKernel);
        }

        void BindSpatialHashBuffers()
        {
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, carrierWedgeKernel, computeColorGradKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, carrierWedgeKernel, computeColorGradKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsetsRO", csfKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeysRO", csfKernel);
        }

        void BindSortBuffers()
        {
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_PositionsRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositionsRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_VelocitiesRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Phases, "SortTarget_Phases", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Phases, "SortTarget_PhasesRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Temperatures, "SortTarget_Temperatures", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Temperatures, "SortTarget_TemperaturesRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_GhostFlags, "SortTarget_IsGhost", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_GhostFlags, "SortTarget_IsGhostRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_BlobIds, "SortTarget_BlobIDs", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_BlobIds, "SortTarget_BlobIDsRO", copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleTargetDensities, "SortTarget_ParticleTargetDensities", reorderKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_ParticleTargetDensities, "SortTarget_ParticleTargetDensitiesRO", copybackKernel);
        }

        void BindDebugBuffers()
        {
            ComputeHelper.SetBuffer(compute, debugDataBuffer, "DebugData", externalForcesKernel, viscosityKernel, thermalBuoyancyKernel, cohesionKernel, computeColorGradKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, debugVectorDataBuffer, "DebugVectorData", thermalBuoyancyKernel, carrierWedgeKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, debugVectorSignBuffer, "DebugVectorSign", csfKernel);
            ComputeHelper.SetBuffer(compute, colorGradientBuffer, "ColorGradients", computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, colorGradientBuffer, "ColorGradientsRO", csfKernel);
        }
        
        void SettleSimulation()
        {
            float settleStepSize = 1f / 1200f; // very small fixed step
            int settleSteps = 300; // run for 0.25 simulated seconds

            for (int i = 0; i < settleSteps; i++)
            {
                UpdateSettings(settleStepSize);
                RunSimulationStep();
            }
            RecomputeBlobIDs();
        }

        void Update()
        {
            CreateOrUpdatePhaseBuffers(initial: false);

            if (!isPaused)
            {
                float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity;
                float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
                RunSimulationFrame(dt);
            }

            if (pauseNextFrame)
            {
                isPaused = true;
                pauseNextFrame = false;
            }

            HandleInput();
        }
        bool phasesDirty = true;

        float GetTemperatureAdjustedViscosity(PhaseConfig phase, float temperature)
        {
            float deltaT = temperature - ambientTemperature;
            float scale = Mathf.Clamp(1.0f - phase.viscosityTemperatureSensitivity * deltaT, 0.05f, 10.0f);
            return phase.viscosity * scale;
        }

        void OnValidate()
        {
            phasesDirty = true;
        }

        void CreateOrUpdatePhaseBuffers(bool initial)
        {
            if (!phasesDirty) return;
            phasesDirty = false;
            EnsurePhaseBuffers();
            UploadAndBindPhaseData();
        }

        void EnsurePhaseBuffers()
        {
            int phaseCount = phases.Length;

            phaseTargetDensityBuffer?.Release();
            phaseViscosityBuffer?.Release();
            phaseViscosityTemperatureSensitivityBuffer?.Release();
            phaseInteractionBuffer?.Release();
            phaseThermalExpansionBuffer?.Release();
            phaseThermalConductivityBuffer?.Release();
            phaseSpecificHeatCapacityBuffer?.Release();
            phaseCohesionBuffer?.Release();
            phaseSurfaceTensionBuffer?.Release();
            phaseCohesionBuffer             = null;
            phaseTargetDensityBuffer        = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityBuffer            = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityTemperatureSensitivityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseInteractionBuffer          = new ComputeBuffer(phaseCount * phaseCount, sizeof(float));
            phaseThermalExpansionBuffer     = new ComputeBuffer(phaseCount, sizeof(float));
            phaseThermalConductivityBuffer  = new ComputeBuffer(phaseCount, sizeof(float));
            phaseSpecificHeatCapacityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseSurfaceTensionBuffer       = new ComputeBuffer(phaseCount * phaseCount, sizeof(float));
        }

        void UploadAndBindPhaseData()
        {
            int phaseCount = phases.Length;

            // Build interaction matrix
            float[] interactionFlat = new float[phaseCount * phaseCount];
            for (int y = 0; y < phaseCount; y++)
                for (int x = 0; x < phaseCount; x++)
                    interactionFlat[y * phaseCount + x] = x == y ? 1.0f : phaseSeparation;

            phaseTargetDensityBuffer.SetData(phases.Select(p => p.targetDensity).ToArray());
            phaseViscosityBuffer.SetData(phases.Select(p => p.viscosity).ToArray());
            phaseViscosityTemperatureSensitivityBuffer.SetData(phases.Select(p => p.viscosityTemperatureSensitivity).ToArray());
            phaseInteractionBuffer.SetData(interactionFlat);
            phaseThermalExpansionBuffer.SetData(phases.Select(p => p.thermalExpansion).ToArray());
            phaseThermalConductivityBuffer.SetData(phases.Select(p => p.thermalConductivity).ToArray());
            phaseSpecificHeatCapacityBuffer.SetData(phases.Select(p => p.specificHeatCapacity).ToArray());
            phaseSurfaceTensionBuffer.SetData(BuildSurfaceTensionMatrix());

            // Non-coalescence per-phase parameters
            phaseNonCoalescenceRadiusBuffer?.Release();
            phaseNonCoalescenceStrengthBuffer?.Release();
            phaseNonCoalescenceRadiusBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceStrengthBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceRadiusBuffer.SetData(phases.Select(p => p.nonCoalescenceRadius).ToArray());
            phaseNonCoalescenceStrengthBuffer.SetData(phases.Select(p => p.nonCoalescenceStrength).ToArray());

            particleDisplay?.SetPhaseColors(phases.Select(p => p.colourMap).ToArray());

            ComputeHelper.SetBuffer(compute, phaseTargetDensityBuffer, "PhaseTargetDensities", thermalBuoyancyKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityBuffer,            "PhaseViscosities",         viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityTemperatureSensitivityBuffer, "PhaseViscosityTemperatureSensitivity", viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseInteractionBuffer,          "PhaseInteractionMatrix",    pressureKernel);
            ComputeHelper.SetBuffer(compute, phaseThermalExpansionBuffer,     "PhaseThermalExpansion",     updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, phaseThermalConductivityBuffer,  "PhaseThermalConductivity",  updateTemperatureKernel);
            ComputeHelper.SetBuffer(compute, phaseSpecificHeatCapacityBuffer, "PhaseSpecificHeatCapacity", updateTemperatureKernel);
            ComputeHelper.SetBuffer(compute, phaseNonCoalescenceRadiusBuffer, "PhaseNonCoalescenceRadius", csfKernel);
            ComputeHelper.SetBuffer(compute, phaseNonCoalescenceStrengthBuffer, "PhaseNonCoalescenceStrength", csfKernel);
            
            int triangularSize = phaseCount * (phaseCount + 1) / 2;
            if (phaseCohesionBuffer == null || phaseCohesionBuffer.count != triangularSize)
            {
                phaseCohesionBuffer?.Release();
                phaseCohesionBuffer = new ComputeBuffer(triangularSize, sizeof(float));
            }
            if (phaseCohesionValues != null && phaseCohesionValues.Length == triangularSize)
                phaseCohesionBuffer.SetData(phaseCohesionValues);
            ComputeHelper.SetBuffer(compute, phaseCohesionBuffer, "PhaseCohesionMatrix", cohesionKernel);
            ComputeHelper.SetBuffer(compute, phaseSurfaceTensionBuffer, "PhaseSurfaceTensionMatrix", csfKernel);
        }

        float[] BuildSurfaceTensionMatrix()
        {
            int phaseCount = phases.Length;
            float[] matrix = new float[phaseCount * phaseCount];
            for (int y = 0; y < phaseCount; y++)
            {
                float sigmaY = phases[y].surfaceTension;
                for (int x = 0; x < phaseCount; x++)
                {
                    float sigmaX = phases[x].surfaceTension;
                    matrix[y * phaseCount + x] = (x == y) ? sigmaX : 0.5f * (sigmaX + sigmaY);
                }
            }
            return matrix;
        }

        void RunSimulationFrame(float frameTime)
        {
            float timeStep = frameTime / iterationsPerFrame;
            UpdateSettings(timeStep);

            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }

        void RunSimulationStep()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);
            RunSpatial();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updateTemperatureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updateThermalExpansionKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: thermalBuoyancyKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: cohesionKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: computeColorGradKernel);
            if (ShouldRecomputeBlobIDs())
            {
                RecomputeBlobIDs();
            }
            bool needsCarrierWedgeDebugClear = particleDisplay != null && particleDisplay.ComputeVectorFieldMode == 5;
            if ((carrierWedgeStrength > 0 && carrierWedgeDistance > 0) || needsCarrierWedgeDebugClear)
            {
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: carrierWedgeKernel);
            }
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: csfKernel); // ADDED
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);

        }

        void RunSpatial()
        {
            // Hash/reorder all particles so static ghost particles can participate in neighbour sampling
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
            spatialHash.Run();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
        }

        bool ShouldRecomputeBlobIDs()
        {
            int interval = Mathf.Max(1, blobIdUpdateInterval);
            blobStepCounter++;
            return blobStepCounter % interval == 0;
        }

        void RecomputeBlobIDs()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: initializeBlobIdsKernel);
            int iterations = Mathf.Max(1, blobPropagationIterations);
            for (int i = 0; i < iterations; i++)
            {
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: propagateBlobIdsKernel);
                ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copyBlobIdsKernel);
            }
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: clearBlobSizesKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: countBlobSizesKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: markSingleParticleBlobsKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copyBlobIdsToPreviousKernel);
        }

        void UpdateSettings(float deltaTime)
        {
            compute.SetFloat("deltaTime", deltaTime);
            compute.SetFloat("gravity", gravity);
            compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetFloat("smoothingRadius", smoothingRadius);
            compute.SetFloat("pressureMultiplier", pressureMultiplier);
            compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
            compute.SetFloat("interfaceViscosityMultiplier", interfaceViscosityMultiplier);
            compute.SetVector("boundsSize", boundsSize);
            compute.SetVector("obstacleSize", obstacleSize);
            compute.SetVector("obstacleCentre", obstacleCentre);
            compute.SetBool("useEllipticalBounds", useEllipticalBounds);
            compute.SetVector("ellipseBoundsSize", ellipseBoundsSize);
            compute.SetVector("ellipseBoundsCenter", ellipseBoundsCenter);

            compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
            compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
            compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
            compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
            compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));
            compute.SetFloat("edgeForce", edgeForce);
            compute.SetFloat("edgeForceDst", edgeForceDst);
            compute.SetFloat("wallPressureStrength", wallPressureStrength);
            compute.SetFloat("wallPressureRadius", wallPressureRadius);
            compute.SetInt("numFluidParticles", numFluidParticles);
            compute.SetInt("numSpatialParticles", numParticles);

            compute.SetFloat("ambientTemperature", ambientTemperature);
            compute.SetFloat("ghostCoolingMultiplier", ghostCoolingMultiplier);
            compute.SetFloat("ambientCoolingRate", ambientCoolingRate);
            compute.SetFloat("wallCoolingRate", wallCoolingRate);
            compute.SetFloat("wallCoolingDistance", wallCoolingDistance);

            if (heatSource != null && heatSource.isActiveAndEnabled)
            {
                compute.SetVector("heatSourcePos", heatSource.Position);
                compute.SetVector("heatSourceSize", heatSource.Size);
                compute.SetInt("heatSourceShape", heatSource.shape == HeatSource2D.HeatSourceShape.Rectangular ? 0 : 1);
                compute.SetFloat("heatSourceTemperature", heatSource.temperature);
                compute.SetFloat("heatSourceTransferRate", heatSource.transferRate);
                compute.SetFloat("heatSourceFalloffPower", heatSource.falloffPower);
            }

            HeatSource2D mergeCoilSource = blobMergeCoil != null ? blobMergeCoil : heatSource;
            bool hasMergeCoilSource = mergeCoilSource != null && mergeCoilSource.isActiveAndEnabled;
            bool restrictMergingToCoil = restrictBlobMergingToCoil && hasMergeCoilSource;
            bool uploadMergeCoilArea = (restrictMergingToCoil || carrierWedgeCoilStrengthMultiplier < 1f || coilVelocityDamping > 0f || coilAttractionStrength > 0f) && hasMergeCoilSource;
            Vector2 mergeCoilPos = Vector2.zero;
            Vector2 mergeCoilSize = Vector2.zero;
            int mergeCoilShape = 1; // 0 = rectangular, 1 = elliptical
            if (uploadMergeCoilArea)
            {
                mergeCoilPos = mergeCoilSource.Position;
                mergeCoilSize = mergeCoilSource.Size;
                mergeCoilShape = mergeCoilSource.shape == HeatSource2D.HeatSourceShape.Rectangular ? 0 : 1;
            }
            compute.SetBool("restrictBlobMergingToCoil", restrictMergingToCoil);
            compute.SetVector("blobMergeCoilPos", mergeCoilPos);
            compute.SetVector("blobMergeCoilSize", mergeCoilSize);
            compute.SetInt("blobMergeCoilShape", mergeCoilShape);
            compute.SetFloat("coilVelocityDamping", coilVelocityDamping);
            compute.SetFloat("coilAttractionStrength", coilAttractionStrength);
            compute.SetInt("coilVelocityDampingPhase", PhaseFilterToIndex(coilVelocityDampingPhase));
            
            compute.SetFloat("buoyancyInversionStrength", buoyancyInversionStrength);
            compute.SetFloat("buoyancyInversionClamp", Mathf.Max(0f, buoyancyInversionClamp));
            compute.SetFloat("surfaceTensionThreshold", surfaceTensionThreshold);
            compute.SetFloat("blobBlobSurfaceTension", blobBlobSurfaceTension);
            compute.SetFloat("blobSelfSurfaceTension", blobSelfSurfaceTension);
            compute.SetInt("blobSelfSurfaceTensionPhase", PhaseFilterToIndex(blobSelfSurfaceTensionPhase));
            compute.SetFloat("maxSurfaceTensionCurvature", maxSurfaceTensionCurvature);
            compute.SetInt("debugVisualizationMode", particleDisplay != null ? (int)particleDisplay.debugMode : 0);
            compute.SetInt("debugVectorFieldMode", particleDisplay != null ? particleDisplay.ComputeVectorFieldMode : 0);

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currInteractStrength = 0;
            if (isPushInteraction || isPullInteraction)
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;

            compute.SetVector("interactionInputPoint", mousePos);
            compute.SetFloat("interactionInputStrength", currInteractStrength);
            compute.SetFloat("interactionInputRadius", interactionRadius);
            compute.SetFloat("blobBlobCohesion", blobBlobCohesion);
            compute.SetFloat("blobFilmSeparationDistance", blobFilmSeparationDistance);
            compute.SetFloat("blobFilmSeparationStrength", blobFilmSeparationStrength);
            compute.SetInt("carrierWedgePhase", ClampPhaseIndex(carrierWedgePhase));
            compute.SetFloat("carrierWedgeDistance", carrierWedgeDistance);
            compute.SetFloat("carrierWedgeStrength", carrierWedgeStrength);
            compute.SetFloat("carrierWedgeCoilStrengthMultiplier", carrierWedgeCoilStrengthMultiplier);
            compute.SetFloat("carrierWedgeMaxAcceleration", carrierWedgeMaxAcceleration);
            compute.SetFloat("carrierWedgeMaxDirectionDot", carrierWedgeMaxDirectionDot);
        }

        int ClampPhaseIndex(LiquidPhase phase)
        {
            return Mathf.Clamp((int)phase, 0, phases.Length - 1);
        }

        int PhaseFilterToIndex(LiquidPhaseFilter phase)
        {
            if (phase == LiquidPhaseFilter.All)
            {
                return -1;
            }

            return Mathf.Clamp((int)phase, 0, phases.Length - 1);
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
        {
            // Combine fluid and ghost particles into single arrays
            float2[] allPositions = new float2[numParticles];
            float2[] allVelocities = new float2[numParticles];
            int[] allPhases = new int[numParticles];
            uint[] allGhostFlags = new uint[numParticles];

            // Fluid particles first
            System.Array.Copy(spawnData.positions, 0, allPositions, 0, numFluidParticles);
            System.Array.Copy(spawnData.velocities, 0, allVelocities, 0, numFluidParticles);
            for (int i = 0; i < numFluidParticles; i++)
            {
                allPhases[i] = spawnData.phases[i];
                allGhostFlags[i] = 0;
            }

            // Ghost particles after
            for (int i = 0; i < numGhostParticles; i++)
            {
                allPositions[numFluidParticles + i] = ghostPositions[i];
                allVelocities[numFluidParticles + i] = ghostVelocities[i];
                allPhases[numFluidParticles + i] = ghostPhases[i];
                allGhostFlags[numFluidParticles + i] = 1;
            }

            positionBuffer.SetData(allPositions);
            predictedPositionBuffer.SetData(allPositions);
            velocityBuffer.SetData(allVelocities);
            phaseBuffer.SetData(allPhases);
            ghostFlagBuffer.SetData(allGhostFlags);
            uint[] initialBlobIds = new uint[numParticles];
            blobIdBuffer.SetData(initialBlobIds);
            blobIdPreviousBuffer.SetData(initialBlobIds);

            // ADDED: reset temperatures to ambient on reset
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);

            float[] initialTargetDensities = new float[numParticles];
            for (int i = 0; i < numFluidParticles; i++)
                initialTargetDensities[i] = phases[spawnData.phases[i]].targetDensity;
            for (int i = numFluidParticles; i < numParticles; i++)
                initialTargetDensities[i] = phases[resolvedGhostPhase].targetDensity;
            particleTargetDensityBuffer.SetData(initialTargetDensities);
            blobStepCounter = 0;
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                isPaused = !isPaused;

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                isPaused = false;
                pauseNextFrame = true;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                isPaused = true;
                SetInitialBufferData(spawnData);
                RunSimulationStep();
                SetInitialBufferData(spawnData);
            }
        }

        void OnDestroy()
        {
            if (positionBuffer != null) positionBuffer.Release();
            if (predictedPositionBuffer != null) predictedPositionBuffer.Release();
            if (velocityBuffer != null) velocityBuffer.Release();
            if (densityBuffer != null) densityBuffer.Release();
            if (phaseBuffer != null) phaseBuffer.Release();
            if (ghostFlagBuffer != null) ghostFlagBuffer.Release();
            if (blobIdBuffer != null) blobIdBuffer.Release();
            if (blobIdScratchBuffer != null) blobIdScratchBuffer.Release();
            if (blobIdPreviousBuffer != null) blobIdPreviousBuffer.Release();
            if (blobSizeBuffer != null) blobSizeBuffer.Release();
            if (temperatureBuffer != null) temperatureBuffer.Release();

            if (sortTarget_Position != null) sortTarget_Position.Release();
            if (sortTarget_Velocity != null) sortTarget_Velocity.Release();
            if (sortTarget_PredicitedPosition != null) sortTarget_PredicitedPosition.Release();
            if (sortTarget_Phases != null) sortTarget_Phases.Release();
            if (sortTarget_GhostFlags != null) sortTarget_GhostFlags.Release();
            if (sortTarget_BlobIds != null) sortTarget_BlobIds.Release();
            if (sortTarget_Temperatures != null) sortTarget_Temperatures.Release();

            if (phaseInteractionBuffer != null) phaseInteractionBuffer.Release();
            if (phaseViscosityBuffer != null) phaseViscosityBuffer.Release();
            if (phaseViscosityTemperatureSensitivityBuffer != null) phaseViscosityTemperatureSensitivityBuffer.Release();
            if (phaseTargetDensityBuffer != null) phaseTargetDensityBuffer.Release();

            if (particleTargetDensityBuffer != null) particleTargetDensityBuffer.Release();
            if (phaseThermalExpansionBuffer != null) phaseThermalExpansionBuffer.Release();
            if (phaseThermalConductivityBuffer != null) phaseThermalConductivityBuffer.Release();
            if (phaseSpecificHeatCapacityBuffer != null) phaseSpecificHeatCapacityBuffer.Release();
            if (sortTarget_ParticleTargetDensities != null) sortTarget_ParticleTargetDensities.Release();
            if (phaseCohesionBuffer != null) phaseCohesionBuffer.Release();
            if (phaseSurfaceTensionBuffer != null) phaseSurfaceTensionBuffer.Release();
            if (phaseNonCoalescenceRadiusBuffer != null) phaseNonCoalescenceRadiusBuffer.Release();
            if (phaseNonCoalescenceStrengthBuffer != null) phaseNonCoalescenceStrengthBuffer.Release();
            if (debugDataBuffer != null) debugDataBuffer.Release();
            if (debugVectorDataBuffer != null) debugVectorDataBuffer.Release();
            if (debugVectorSignBuffer != null) debugVectorSignBuffer.Release();
            if (colorGradientBuffer != null) colorGradientBuffer.Release();

            spatialHash?.Release();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0, 1, 0, 0.4f);
            
            if (useEllipticalBounds)
            {
                // Draw ellipse bounds
                DrawEllipseGizmo(ellipseBoundsCenter, ellipseBoundsSize, 128);
            }
            else
            {
                // Draw rectangular bounds
                Gizmos.DrawWireCube(Vector2.zero, boundsSize);
            }
            
            Gizmos.DrawWireCube(obstacleCentre, obstacleSize);

            if (Application.isPlaying)
            {
                Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                bool isPullInteraction = Input.GetMouseButton(0);
                bool isPushInteraction = Input.GetMouseButton(1);
                if (isPullInteraction || isPushInteraction)
                {
                    Gizmos.color = isPullInteraction ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(mousePos, interactionRadius);
                }
            }
        }

        void DrawEllipseGizmo(Vector2 center, Vector2 radii, int segments)
        {
            float angle = 0;
            float angleStep = 360f / segments;
            Vector3 lastPoint = center + new Vector2(radii.x, 0);

            for (int i = 1; i <= segments; i++)
            {
                angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 newPoint = center + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
                Gizmos.DrawLine(lastPoint, newPoint);
                lastPoint = newPoint;
            }
        }
    }
}
