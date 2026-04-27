using Seb.Helpers;
using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    public class FluidSim2D : MonoBehaviour
    {
        public event System.Action SimulationStepCompleted;

        [Header("Simulation Settings")]
        public float timeScale = 1;
        public float maxTimestepFPS = 60;
        public int iterationsPerFrame;
        public float gravity;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public float pressureMultiplier;
        public float nearPressureMultiplier;
        public Vector2 boundsSize;
        public Vector2 obstacleSize;
        public Vector2 obstacleCentre;
        [Tooltip("magnitude of repulsive acceleration at zero distance")]
        public float edgeForce;
        [Tooltip("distance from boundary over which repulsion fades to zero")]
        public float edgeForceDst;

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
            // Non-coalescence tuning per-phase
            [Tooltip("Short-range radius (in world units) within which non-coalescence repulsion acts")]
            public float nonCoalescenceRadius = 0.2f;
            [Tooltip("Multiplier for non-coalescence repulsion (scaled by phase surface tension)")]
            public float nonCoalescenceStrength = 1.0f;
            [Tooltip("Require normals to be sufficiently opposing: normals dot must be < -threshold")]
            [Range(0f, 1f)] public float nonCoalescenceNormalDotThreshold = 0.7f;
        }

        [Range(0f, 10f)]
        public float phaseSeparation = 0.3f;

        public float[] phaseCohesionValues = new float[] { 0.5f, -0.1f, 0.5f };

        [Header("Blob Connectivity")]
        [Tooltip("Simulation steps between blob-ID recomputation. 1 = every step.")]
        [Min(1)] public int blobIdUpdateInterval = 1;
        [Tooltip("Label-propagation passes used when recomputing blob IDs.")]
        [Min(1)] public int blobPropagationIterations = 8;
        
        [Header("Surface Tension")]
        public float surfaceTensionThreshold = 0.1f;

        // ADDED: temperature settings
        [Header("Temperature")]
        public float ambientTemperature = 20f;
        public float heatDiffusionRate = 0.5f;
        public float heatCoolingRate = 0.1f;
        public float heatSinkTemperature = 10f;

        [Header("Heat Source")]
        public HeatSource2D heatSource;
        [Tooltip("Heat transfer rate from the heat source area (Newton cooling/heating coefficient).")]
        [Min(0f)] public float heatSourceTransferRate = 2.0f;

        public float HeatSourceTemperature => heatSource != null && heatSource.isActiveAndEnabled ? heatSource.temperature : ambientTemperature;

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D spawner2D;

        // Buffers
        public ComputeBuffer positionBuffer { get; private set; }
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer phaseBuffer { get; private set; }
        public ComputeBuffer blobIdBuffer { get; private set; }
        public ComputeBuffer temperatureBuffer { get; private set; }
        public ComputeBuffer csfGradientBuffer { get; private set; }
        public ComputeBuffer colorGradientBuffer { get; private set; }
        ComputeBuffer blobIdScratchBuffer;

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_Phases;
        ComputeBuffer sortTarget_BlobIds;
        ComputeBuffer sortTarget_Temperatures;

        ComputeBuffer phaseTargetDensityBuffer;
        ComputeBuffer phaseViscosityBuffer;
        ComputeBuffer phaseViscosityTemperatureSensitivityBuffer;
        ComputeBuffer phaseInteractionBuffer;
        ComputeBuffer particleTargetDensityBuffer;
        ComputeBuffer phaseThermalExpansionBuffer;
        ComputeBuffer sortTarget_ParticleTargetDensities;
        ComputeBuffer phaseCohesionBuffer;
        ComputeBuffer phaseSurfaceTensionBuffer;
        ComputeBuffer phaseNonCoalescenceRadiusBuffer;
        ComputeBuffer phaseNonCoalescenceStrengthBuffer;
        ComputeBuffer phaseNonCoalescenceNormalDotThresholdBuffer;

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
        int reorderTemperatureKernel;
        int copybackTemperatureKernel;
        int reorderParticleTargetDensitiesKernel;
        int copybackParticleTargetDensitiesKernel;
        int cohesionKernel;
        int csfKernel;
        int computeColorGradKernel;
        int initializeBlobIdsKernel;
        int propagateBlobIdsKernel;
        int copyBlobIdsKernel;
        int reorderBlobIdsKernel;
        int copybackBlobIdsKernel;

        // State
        bool isPaused;
        Spawner2D.ParticleSpawnData spawnData;
        bool pauseNextFrame;
        float2[] velocityReadback;
        float2[] densityReadback;
        float[] targetDensityReadback;
        int blobStepCounter;

        public int numParticles { get; private set; }

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
            reorderTemperatureKernel = compute.FindKernel("ReorderTemperature");
            copybackTemperatureKernel = compute.FindKernel("ReorderTemperatureCopyback");
            reorderParticleTargetDensitiesKernel = compute.FindKernel("ReorderParticleTargetDensities");
            copybackParticleTargetDensitiesKernel = compute.FindKernel("ReorderParticleTargetDensitiesCopyback");
            cohesionKernel = compute.FindKernel("CalculateCohesion");
            csfKernel = compute.FindKernel("CalculateCSF");
            computeColorGradKernel = compute.FindKernel("ComputeColorGradients");
            initializeBlobIdsKernel = compute.FindKernel("InitializeBlobIDs");
            propagateBlobIdsKernel = compute.FindKernel("PropagateBlobIDs");
            copyBlobIdsKernel = compute.FindKernel("CopyBlobIDs");
            reorderBlobIdsKernel = compute.FindKernel("ReorderBlobIDs");
            copybackBlobIdsKernel = compute.FindKernel("ReorderBlobIDsCopyback");

            particleDisplay = GetComponent<Rendering.ParticleDisplay2D>();
            if (phases != null)
                particleDisplay?.SetPhaseColors(phases.Select(p => p.colourMap).ToArray());

            if (heatSource == null)
                heatSource = FindObjectOfType<HeatSource2D>();

            float deltaTime = 1 / 60f;
            Time.fixedDeltaTime = deltaTime;

            spawnData = spawner2D.GetSpawnData();
            numParticles = spawnData.positions.Length;
            spatialHash = new SpatialHash(numParticles);

            // Create buffers
            positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            phaseBuffer = ComputeHelper.CreateStructuredBuffer<int>(numParticles);
            blobIdBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            blobIdScratchBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);
            //debug
            csfGradientBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            ComputeHelper.SetBuffer(compute, csfGradientBuffer, "CSFGradients", csfKernel);
            // Color gradients for CSF
            colorGradientBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            ComputeHelper.SetBuffer(compute, colorGradientBuffer, "ColorGradients", computeColorGradKernel, csfKernel);

            particleTargetDensityBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);

            // Initialize to base phase densities
            float[] initialTargetDensities = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTargetDensities[i] = phases[spawnData.phases[i]].targetDensity;
            particleTargetDensityBuffer.SetData(initialTargetDensities);


            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Phases = ComputeHelper.CreateStructuredBuffer<int>(numParticles);
            sortTarget_BlobIds = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
            sortTarget_Temperatures = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            sortTarget_ParticleTargetDensities = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            int triangularSize = phases.Length * (phases.Length + 1) / 2;
            phaseCohesionBuffer = new ComputeBuffer(triangularSize, sizeof(float));
            phaseCohesionBuffer.SetData(phaseCohesionValues);
            float[] initialSurfaceTensionMatrix = BuildSurfaceTensionMatrix();
            phaseSurfaceTensionBuffer = new ComputeBuffer(phases.Length * phases.Length, sizeof(float));
            phaseSurfaceTensionBuffer.SetData(initialSurfaceTensionMatrix);
            ComputeHelper.SetBuffer(compute, phaseSurfaceTensionBuffer, "PhaseSurfaceTensionMatrix", csfKernel);

            // Add csfKernel to existing buffer bindings:
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, computeColorGradKernel, csfKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, cohesionKernel, csfKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, cohesionKernel,computeColorGradKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "Phases", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel, updatePositionKernel, cohesionKernel,computeColorGradKernel, csfKernel, reorderKernel, copybackKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, blobIdBuffer, "BlobIDs", initializeBlobIdsKernel, propagateBlobIdsKernel, copyBlobIdsKernel, pressureKernel, csfKernel, reorderBlobIdsKernel, copybackBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, blobIdScratchBuffer, "BlobIDsScratch", propagateBlobIdsKernel, copyBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, cohesionKernel, csfKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsetsRO", csfKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeysRO", csfKernel);
            
            ComputeHelper.SetBuffer(compute, phaseCohesionBuffer, "PhaseCohesionMatrix", cohesionKernel);

            CreateOrUpdatePhaseBuffers(initial: true);

            SetInitialBufferData(spawnData);

            // Bind buffers
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "Phases", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel, updatePositionKernel, reorderKernel, copybackKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, blobIdBuffer, "BlobIDs", initializeBlobIdsKernel, propagateBlobIdsKernel, copyBlobIdsKernel, pressureKernel, csfKernel, reorderBlobIdsKernel, copybackBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, blobIdScratchBuffer, "BlobIDsScratch", propagateBlobIdsKernel, copyBlobIdsKernel);

            ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", viscosityKernel, updateTemperatureKernel, reorderTemperatureKernel, copybackTemperatureKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Temperatures, "SortTarget_Temperatures", reorderTemperatureKernel, copybackTemperatureKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel, reorderTemperatureKernel, initializeBlobIdsKernel, reorderBlobIdsKernel);

            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel, reorderBlobIdsKernel, initializeBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateTemperatureKernel, computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", initializeBlobIdsKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "Phases", initializeBlobIdsKernel, propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", propagateBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", propagateBlobIdsKernel);

            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Phases, "SortTarget_Phases", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_BlobIds, "SortTarget_BlobIDs", reorderBlobIdsKernel, copybackBlobIdsKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityBuffer, "PhaseViscosities", viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseTargetDensityBuffer, "PhaseTargetDensities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, particleTargetDensityBuffer, "ParticleTargetDensities", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, updateThermalExpansionKernel, reorderParticleTargetDensitiesKernel, copybackParticleTargetDensitiesKernel);

            ComputeHelper.SetBuffer(compute, sortTarget_ParticleTargetDensities, "SortTarget_ParticleTargetDensities", reorderParticleTargetDensitiesKernel, copybackParticleTargetDensitiesKernel);

            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel, reorderTemperatureKernel, reorderParticleTargetDensitiesKernel, initializeBlobIdsKernel, reorderBlobIdsKernel); 
            ComputeHelper.SetBuffer(compute, csfGradientBuffer, "CSFGradients", computeColorGradKernel);
            ComputeHelper.SetBuffer(compute, csfGradientBuffer, "CSFGradients", externalForcesKernel, pressureKernel, viscosityKernel, thermalBuoyancyKernel, cohesionKernel, csfKernel);

            compute.SetInt("numParticles", numParticles);
            compute.SetInt("NumPhases", phases.Length);
            SettleSimulation();
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
            phaseCohesionBuffer?.Release();
            phaseSurfaceTensionBuffer?.Release();
            phaseCohesionBuffer             = null;
            phaseTargetDensityBuffer        = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityBuffer            = new ComputeBuffer(phaseCount, sizeof(float));
            phaseViscosityTemperatureSensitivityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseInteractionBuffer          = new ComputeBuffer(phaseCount * phaseCount, sizeof(float));
            phaseThermalExpansionBuffer     = new ComputeBuffer(phaseCount, sizeof(float));
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
            phaseSurfaceTensionBuffer.SetData(BuildSurfaceTensionMatrix());

            // Non-coalescence per-phase parameters
            phaseNonCoalescenceRadiusBuffer?.Release();
            phaseNonCoalescenceStrengthBuffer?.Release();
            phaseNonCoalescenceNormalDotThresholdBuffer?.Release();
            phaseNonCoalescenceRadiusBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceStrengthBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceNormalDotThresholdBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            phaseNonCoalescenceRadiusBuffer.SetData(phases.Select(p => p.nonCoalescenceRadius).ToArray());
            phaseNonCoalescenceStrengthBuffer.SetData(phases.Select(p => p.nonCoalescenceStrength).ToArray());
            phaseNonCoalescenceNormalDotThresholdBuffer.SetData(phases.Select(p => p.nonCoalescenceNormalDotThreshold).ToArray());

            particleDisplay?.SetPhaseColors(phases.Select(p => p.colourMap).ToArray());

            ComputeHelper.SetBuffer(compute, phaseTargetDensityBuffer, "PhaseTargetDensities",externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityBuffer,            "PhaseViscosities",         viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityTemperatureSensitivityBuffer, "PhaseViscosityTemperatureSensitivity", viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseInteractionBuffer,          "PhaseInteractionMatrix",    pressureKernel);
            ComputeHelper.SetBuffer(compute, phaseThermalExpansionBuffer,     "PhaseThermalExpansion",     updateThermalExpansionKernel);
            ComputeHelper.SetBuffer(compute, phaseNonCoalescenceRadiusBuffer, "PhaseNonCoalescenceRadius", csfKernel);
            ComputeHelper.SetBuffer(compute, phaseNonCoalescenceStrengthBuffer, "PhaseNonCoalescenceStrength", csfKernel);
            ComputeHelper.SetBuffer(compute, phaseNonCoalescenceNormalDotThresholdBuffer, "PhaseNonCoalescenceNormalDotThreshold", csfKernel);
            
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
            // bind non-coalescence per-phase buffers (if set)
            if (phaseNonCoalescenceRadiusBuffer != null)
                ComputeHelper.SetBuffer(compute, phaseNonCoalescenceRadiusBuffer, "PhaseNonCoalescenceRadius", csfKernel);
            if (phaseNonCoalescenceStrengthBuffer != null)
                ComputeHelper.SetBuffer(compute, phaseNonCoalescenceStrengthBuffer, "PhaseNonCoalescenceStrength", csfKernel);
            if (phaseNonCoalescenceNormalDotThresholdBuffer != null)
                ComputeHelper.SetBuffer(compute, phaseNonCoalescenceNormalDotThresholdBuffer, "PhaseNonCoalescenceNormalDotThreshold", csfKernel);
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
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: csfKernel); // ADDED
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updateTemperatureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updateThermalExpansionKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);

        }

        void RunSpatial()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
            spatialHash.Run();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderBlobIdsKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackBlobIdsKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderTemperatureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackTemperatureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderParticleTargetDensitiesKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackParticleTargetDensitiesKernel);
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
        }

        void UpdateSettings(float deltaTime)
        {
            compute.SetFloat("deltaTime", deltaTime);
            compute.SetFloat("gravity", gravity);
            compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetFloat("smoothingRadius", smoothingRadius);
            compute.SetFloat("pressureMultiplier", pressureMultiplier);
            compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
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

            // ADDED: temperature settings
            compute.SetFloat("ambientTemperature", ambientTemperature);
            compute.SetFloat("heatDiffusionRate", heatDiffusionRate);
            compute.SetFloat("heatCoolingRate", heatCoolingRate);
            compute.SetFloat("heatSinkTemperature", heatSinkTemperature);
            Vector2 sourcePos = Vector2.zero;
            float sourceRadius = 0f;
            float sourceTemp = ambientTemperature;
            if (heatSource != null && heatSource.isActiveAndEnabled)
            {
                sourcePos = heatSource.Position;
                sourceRadius = heatSource.radius;
                sourceTemp = heatSource.temperature;
            }
            compute.SetVector("heatSourcePos", sourcePos);
            compute.SetFloat("heatSourceRadius", sourceRadius);
            compute.SetFloat("heatSourceTemperature", sourceTemp);
            compute.SetFloat("heatSourceTransferRate", Mathf.Max(0f, heatSourceTransferRate));
            compute.SetFloat("buoyancyInversionStrength", buoyancyInversionStrength);
            compute.SetFloat("buoyancyInversionClamp", Mathf.Max(0f, buoyancyInversionClamp));
            compute.SetFloat("surfaceTensionThreshold", surfaceTensionThreshold);
            compute.SetInt("debugVisualizationMode", particleDisplay != null ? (int)particleDisplay.debugMode : 0);

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            bool isPullInteraction = Input.GetMouseButton(0);
            bool isPushInteraction = Input.GetMouseButton(1);
            float currInteractStrength = 0;
            if (isPushInteraction || isPullInteraction)
                currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;

            compute.SetVector("interactionInputPoint", mousePos);
            compute.SetFloat("interactionInputStrength", currInteractStrength);
            compute.SetFloat("interactionInputRadius", interactionRadius);
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
        {
            float2[] allPoints = new float2[spawnData.positions.Length];
            System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

            positionBuffer.SetData(allPoints);
            predictedPositionBuffer.SetData(allPoints);
            velocityBuffer.SetData(spawnData.velocities);
            phaseBuffer.SetData(spawnData.phases);
            uint[] initialBlobIds = new uint[numParticles];
            blobIdBuffer.SetData(initialBlobIds);

            // ADDED: reset temperatures to ambient on reset
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);
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
            if (blobIdBuffer != null) blobIdBuffer.Release();
            if (blobIdScratchBuffer != null) blobIdScratchBuffer.Release();
            if (temperatureBuffer != null) temperatureBuffer.Release();

            if (sortTarget_Position != null) sortTarget_Position.Release();
            if (sortTarget_Velocity != null) sortTarget_Velocity.Release();
            if (sortTarget_PredicitedPosition != null) sortTarget_PredicitedPosition.Release();
            if (sortTarget_Phases != null) sortTarget_Phases.Release();
            if (sortTarget_BlobIds != null) sortTarget_BlobIds.Release();
            if (sortTarget_Temperatures != null) sortTarget_Temperatures.Release();

            if (phaseInteractionBuffer != null) phaseInteractionBuffer.Release();
            if (phaseViscosityBuffer != null) phaseViscosityBuffer.Release();
            if (phaseViscosityTemperatureSensitivityBuffer != null) phaseViscosityTemperatureSensitivityBuffer.Release();
            if (phaseTargetDensityBuffer != null) phaseTargetDensityBuffer.Release();

            if (particleTargetDensityBuffer != null) particleTargetDensityBuffer.Release();
            if (phaseThermalExpansionBuffer != null) phaseThermalExpansionBuffer.Release();
            if (sortTarget_ParticleTargetDensities != null) sortTarget_ParticleTargetDensities.Release();
            if (phaseCohesionBuffer != null) phaseCohesionBuffer.Release();
            if (phaseSurfaceTensionBuffer != null) phaseSurfaceTensionBuffer.Release();
            if (phaseNonCoalescenceRadiusBuffer != null) phaseNonCoalescenceRadiusBuffer.Release();
            if (phaseNonCoalescenceStrengthBuffer != null) phaseNonCoalescenceStrengthBuffer.Release();
            if (phaseNonCoalescenceNormalDotThresholdBuffer != null) phaseNonCoalescenceNormalDotThresholdBuffer.Release();
            if (csfGradientBuffer != null) csfGradientBuffer.Release();
            if (colorGradientBuffer != null) colorGradientBuffer.Release();

            spatialHash?.Release();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0, 1, 0, 0.4f);
            
            if (useEllipticalBounds)
            {
                // Draw ellipse bounds
                DrawEllipseGizmo(ellipseBoundsCenter, ellipseBoundsSize, 32);
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
