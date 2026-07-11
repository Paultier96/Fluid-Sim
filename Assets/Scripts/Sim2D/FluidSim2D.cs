using Seb.Helpers;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    [RequireComponent(typeof(ParticleFluidAnalyticBoundary2D))]
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

        public enum SurfaceTensionInterfaceMode
        {
            BlobAware = 0,
            PhaseBased = 1
        }

        [Header("Simulation Settings")]
        public float timeScale = 1;
        [Tooltip("When enabled, ignore real-time pacing and run the maximum configured substep budget every rendered frame.")]
        public bool unlockedTimeScale = false;
        [Tooltip("Minimum simulation substep frequency in Hertz. Higher values mean smaller, more stable substeps.")]
        [Min(1f)] public float minSubstepHz = 360f;
        [Tooltip("Automatically calculate iterations per rendered frame from monitor refresh rate, time scale, and minimum substep frequency.")]
        public bool autoIterationsPerFrame = true;
        [Tooltip("Manual substep count used when auto mode is disabled. In auto mode this is updated to the current calculated value.")]
        [Min(1)] public int iterationsPerFrame = 1;
        [Tooltip("Upper bound for automatically calculated iterations per frame. In unlocked mode this is the fast-forward work budget.")]
        [Min(1)] public int maxAutoIterationsPerFrame = 16;
        public float gravity;
        [Range(0, 1)] public float collisionDamping = 0.95f;
        public float smoothingRadius = 2;
        public float pressureMultiplier;
        public float nearPressureMultiplier;
        [Tooltip("Multiplier applied to viscous velocity exchange across phase boundaries and between separate same-phase blobs. Lower values make interfaces more slippery.")]
        [Range(0f, 1f)] public float interfaceViscosityMultiplier = 1f;
        public Vector2 boundsSize;
        [Tooltip("magnitude of repulsive acceleration at zero distance")]
        public float edgeForce;
        [Tooltip("distance from boundary over which repulsion fades to zero")]
        public float edgeForceDst;
        
        [Header("Wall Phase Force")]
        [Tooltip("Phase pushed away from analytic walls to preserve a carrier-fluid film. Use Wax for lava-lamp blobs.")]
        public LiquidPhaseFilter wallFilmPhase = LiquidPhaseFilter.Wax;
        [Tooltip("Distance from analytic walls over which the wall-film force fades. Set to 0 to disable.")]
        [Min(0f)] public float wallFilmDistance = 0f;
        [Tooltip("Acceleration strength pushing the selected phase away from analytic walls.")]
        [Min(0f)] public float wallFilmStrength = 0f;
        [Tooltip("Phase pulled toward analytic walls to fill the wall film. Use Water for lava-lamp carrier fluid.")]
        public LiquidPhaseFilter wallFilmAttractionPhase = LiquidPhaseFilter.Water;
        [Tooltip("Acceleration strength pulling the selected phase toward analytic walls.")]
        [Min(0f)] public float wallFilmAttractionStrength = 0f;
        [Tooltip("Optional cap for wall-film acceleration. Set to 0 for no cap.")]
        [Min(0f)] public float wallFilmMaxAcceleration = 0f;

        [Header("Boundary Pressure Support")]
        [Tooltip("Strength of wall-support pressure term used to compensate for missing neighbours near boundaries.")]
        [Min(0f)] public float wallPressureStrength = 0f;
        [Tooltip("Distance from boundary over which wall-pressure support is applied. Set to 0 to use smoothing radius.")]
        [Min(0f)] public float wallPressureRadius = 0f;

        [Header("Interaction Settings")]
        public float interactionRadius;
        public float interactionStrength;
        
        [Header("Buoyancy")]
        [Tooltip("Scales thermal buoyancy inversion based on local density contrast.")]
        public float buoyancyInversionStrength = 1.0f;
        [Tooltip("Clamp for normalized density contrast used by thermal buoyancy to prevent spikes.")]
        public float buoyancyInversionClamp = 1.0f;

        [Tooltip("Resolution scale relative to the authored values. Scales particle count, target densities, and smoothing radius consistently.")]
        [Range(0.01f, 10f)]
        public float particleResolutionFactor = 1;

        [Header("Phases")]
        public PhaseConfig[] phases;
        
        [Header("Ghost Particles")]
        [Tooltip("Liquid phase assigned to outer boundary ghost particles. Clamped to valid phase range at runtime.")]
        public LiquidPhase ghostPhase = LiquidPhase.Water;
        [Tooltip("Liquid phase assigned to rectangular obstacle ghost particles. Use Wax to make wax wet/merge with the obstacle/coil area.")]
        public LiquidPhase obstacleGhostPhase = LiquidPhase.Wax;
        [Tooltip("Width of the centered lower-boundary section that receives the obstacle ghost phase. Set to 0 to use the merge coil or heat source width.")]
        [Min(0f)] public float lowerGhostPhaseWidth = 0f;

        [System.Serializable]
        public class PhaseConfig
        {
            public string name = "Water";
            public float targetDensity = 234;
            public float viscosity = 0.03f;
            [Tooltip("Fractional viscosity change per degree relative to ambient temperature. Positive values make hot fluid less viscous and cold fluid more viscous.")]
            public float viscosityTemperatureSensitivity = 0.0f;
            public float thermalExpansion = 0.0f;
            [Tooltip("Thermal conductivity controls heat diffusion rate between particles. Higher values = faster heat spreading.")]
            public float thermalConductivity = 0.5f;
            [Tooltip("Specific heat capacity: how much energy is required to raise temperature by one degree. Higher values = more thermal inertia (slower heating).")]
            [Min(0.01f)] public float specificHeatCapacity = 1.0f;
            // Non-coalescence tuning per-phase
            //[HideInInspector]
            [Tooltip("Radius multiplier for non-coalescence repulsion. 1 = smoothing radius.")]
            [Range(0f, 8f)] public float nonCoalescenceRadiusMultiplier = 1.0f;

            //[HideInInspector]
            [Tooltip("Multiplier for non-coalescence repulsion (scaled by phase surface tension)")]
            public float nonCoalescenceStrength = 1.0f;
        }

        [Range(0f, 10f)]
        public float phaseSeparation = 0.3f;

        public float[] phaseCohesionValues = new float[] { 0.5f, -0.1f, 0.5f };
        [Tooltip("Cohesion override for same-phase particles that belong to different blobs. Negative values repel.")]
        public float blobBlobCohesion = -0.1f;
        [Tooltip("Carrier phase pushed into the gap between two nearby blob IDs. For the lava lamp setup this is usually the water phase.")]
        public LiquidPhase carrierWedgePhase = LiquidPhase.Water;
        [Tooltip("Search distance multiplier for pushing carrier fluid into wax-blob gaps. 1 = smoothing radius.")]
        [Range(0f, 8f)] public float carrierWedgeDistanceMultiplier = 0f;
        [Tooltip("Acceleration strength that pushes carrier fluid into the gap between two different blob IDs.")]
        [Min(0f)] public float carrierWedgeStrength = 0f;
        [Tooltip("Multiplier applied to carrier viscosity inside a valid wedge. 1 = no local viscosity boost.")]
        [Min(1f)] public float carrierWedgeViscosityMultiplier = 1f;
        [Tooltip("When enabled, bulk carrier particles with weak color gradients skip the expensive carrier wedge neighbour scan.")]
        public bool carrierWedgeInterfaceOnly = true;
        [Tooltip("Minimum color-gradient magnitude required for carrier wedge when interface-only mode is enabled.")]
        [Min(0f)] public float carrierWedgeInterfaceThreshold = 0.001f;
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
        [Tooltip("Blob-aware treats separate same-phase blobs as separate interfaces. Phase-based ignores blob IDs and only creates CSF interfaces between different phases.")]
        public SurfaceTensionInterfaceMode surfaceTensionInterfaceMode = SurfaceTensionInterfaceMode.BlobAware;
        [Tooltip("Base CSF surface tension used for phase interfaces.")]
        [Min(0f)] public float surfaceTension = 0f;
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
        [Tooltip("Multiplier for heat diffusion between different phases. 1 = same as same-phase transfer, 0 = no cross-phase transfer.")]
        [Range(0f, 1f)] public float crossPhaseThermalDiffusion = 0.999f;
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
                float maxSurfaceTension = Mathf.Max(Mathf.Abs(surfaceTension), Mathf.Abs(blobBlobSurfaceTension), Mathf.Abs(blobSelfSurfaceTension));
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

        readonly ParticleFluidSimulationResources resources = new();
        readonly ParticleFluidSimulationKernels kernels = new();
        readonly ParticleFluidSimulationTiming timing = new();
        readonly ParticleFluidSimulationInput simulationInput = new();
        readonly ParticleFluidPhaseDataUploader phaseDataUploader = new();

        public ComputeBuffer positionBuffer => resources.positionBuffer;
        public ComputeBuffer velocityBuffer => resources.velocityBuffer;
        public ComputeBuffer densityBuffer => resources.densityBuffer;
        public ComputeBuffer phaseBuffer => resources.phaseBuffer;
        public ComputeBuffer ghostFlagBuffer => resources.ghostFlagBuffer;
        public ComputeBuffer blobIdBuffer => resources.blobIdBuffer;
        public ComputeBuffer temperatureBuffer => resources.temperatureBuffer;
        public ComputeBuffer debugDataBuffer => resources.debugDataBuffer;
        public ComputeBuffer debugVectorDataBuffer => resources.debugVectorDataBuffer;
        public ComputeBuffer debugVectorSignBuffer => resources.debugVectorSignBuffer;
        public ComputeBuffer colorGradientBuffer => resources.colorGradientBuffer;

        SpatialHash spatialHash;

        public float[,] interactionMatrix = new float[,]
        {
            { 1.0f, 0.3f },
            { 0.3f, 1.0f }
        };

        // State
        public bool isPaused;
        private Spawner2D.ParticleSpawnData _spawnData;
        private List<float2> _ghostPositions;
        private List<float2> _ghostVelocities;
        private List<int> _ghostPhases;
        private bool _pauseNextFrame;
        private float2[] _velocityReadback;
        private float2[] _densityReadback;
        private float[] _targetDensityReadback;
        private int _blobStepCounter;

        public int NumParticles { get; private set; }
        public int NumFluidParticles { get; private set; }
        public int NumGhostParticles { get; private set; }
        public float CurrentPlaybackSpeed { get; private set; }
        public float CurrentSimulationDeltaTime { get; private set; }
        public float CurrentSimulationSubstepDeltaTime { get; private set; }
        public int CurrentSimulationSubstepCount { get; private set; }
        public float CurrentDisplayRefreshRate { get; private set; }
        private int _resolvedGhostPhase;
        private int _resolvedObstacleGhostPhase;

        // Runtime-change tracking
        private Rendering.ParticleDisplay2D _particleDisplay;
        public ParticleFluidAnalyticBoundary2D analyticBoundary;

        void Awake()
        {
            analyticBoundary ??= GetComponent<ParticleFluidAnalyticBoundary2D>();
            simulationInput.cam = Camera.main;
        }

        void Start()
        {
            kernels.Resolve(compute);

            _particleDisplay = GetComponent<Rendering.ParticleDisplay2D>();
            if (phases == null || phases.Length == 0)
                throw new InvalidOperationException("At least one phase is required.");

            if (heatSource == null)
                heatSource = FindAnyObjectByType<HeatSource2D>();

            float deltaTime = 1 / 60f;
            Time.fixedDeltaTime = deltaTime;

            float resolvedResolutionFactor = particleResolutionFactor;
            _spawnData = spawner2D.GetSpawnData(spawner2D.spawnDensity * resolvedResolutionFactor);
            NumFluidParticles = _spawnData.positions.Length;

            // Generate ghost particles with proper layering
            _ghostPositions = new List<float2>();
            _ghostVelocities = new List<float2>();
            _ghostPhases = new List<int>();
            _resolvedGhostPhase = ClampPhaseIndex(ghostPhase);
            _resolvedObstacleGhostPhase = ClampPhaseIndex(obstacleGhostPhase);
            spawner2D.GenerateGhostParticles(analyticBoundary, boundsSize,  _resolvedGhostPhase, _resolvedObstacleGhostPhase, _ghostPositions, _ghostVelocities, _ghostPhases, ResolveLowerGhostPhaseWidth());
            NumGhostParticles = _ghostPositions.Count;
            NumParticles = NumFluidParticles + NumGhostParticles;
            spatialHash = new SpatialHash(NumParticles);

            resources.AllocateParticleBuffers(NumParticles);
            float[] initialTemps = new float[NumParticles];
            for (int i = 0; i < NumFluidParticles; i++)
                initialTemps[i] = ambientTemperature;
            for (int i = NumFluidParticles; i < NumParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);

            // Initialize to base phase densities for fluid particles; ghosts get rest density
            float[] initialTargetDensities = new float[NumParticles];
            for (int i = 0; i < NumFluidParticles; i++)
                initialTargetDensities[i] = EffectiveTargetDensity(_spawnData.phases[i]);
            for (int i = NumFluidParticles; i < NumParticles; i++)
                initialTargetDensities[i] = EffectiveTargetDensity(_ghostPhases[i - NumFluidParticles]);
            resources.particleTargetDensityBuffer.SetData(initialTargetDensities);

            resources.AllocateSortBuffers(NumParticles);

            UploadPhaseDataIfDirty();

            SetInitialBufferData(_spawnData);

            kernels.BindParticleBuffers(compute, resources);
            kernels.BindSpatialHashBuffers(compute, spatialHash);
            kernels.BindSortBuffers(compute, resources);
            kernels.BindDebugBuffers(compute, resources);

            compute.SetInt("numParticles", NumParticles);
            compute.SetInt("numSpatialParticles", NumParticles);
            compute.SetInt("NumPhases", phases.Length);
            //SettleSimulation();
        }

        void Update()
        {
            UploadPhaseDataIfDirty();

            if (!isPaused)
            {
                ParticleFluidSimulationTiming.Frame frame = timing.ResolveFrame(
                    Time.deltaTime,
                    Time.unscaledDeltaTime,
                    timeScale,
                    unlockedTimeScale,
                    autoIterationsPerFrame,
                    iterationsPerFrame,
                    maxAutoIterationsPerFrame,
                    minSubstepHz);
                ApplyTimingFrame(frame);
                iterationsPerFrame = frame.resolvedIterationsPerFrame;
                RunSimulationFrame(frame.deltaTime, frame.substepCount);
            }
            else
            {
                ApplyTimingFrame(ParticleFluidSimulationTiming.PausedFrame(CurrentDisplayRefreshRate));
            }

            if (_pauseNextFrame)
            {
                isPaused = true;
                _pauseNextFrame = false;
                ApplyTimingFrame(ParticleFluidSimulationTiming.PausedFrame(CurrentDisplayRefreshRate));
            }

            HandleInput();
        }

        void ApplyTimingFrame(ParticleFluidSimulationTiming.Frame frame)
        {
            CurrentSimulationDeltaTime = frame.deltaTime;
            CurrentSimulationSubstepDeltaTime = frame.substepDeltaTime;
            CurrentSimulationSubstepCount = frame.substepCount;
            CurrentPlaybackSpeed = frame.playbackSpeed;
            CurrentDisplayRefreshRate = frame.displayRefreshRate;
        }

        private float GetTemperatureAdjustedViscosity(PhaseConfig phase, float temperature)
        {
            float deltaT = temperature - ambientTemperature;
            float scale = Mathf.Clamp(1.0f - phase.viscosityTemperatureSensitivity * deltaT, 0.05f, 10.0f);
            return phase.viscosity * scale;
        }

        private void OnValidate()
        {
            analyticBoundary ??= GetComponent<ParticleFluidAnalyticBoundary2D>();
            phaseDataUploader.MarkDirty();
        }

        void UploadPhaseDataIfDirty()
        {
            phaseDataUploader.UploadIfDirty(compute, resources, kernels, phases, phaseSeparation, phaseCohesionValues, EffectiveTargetDensity);
        }

        void RunSimulationFrame(float frameTime, int substepCount)
        {
            float timeStep = frameTime / Mathf.Max(1, substepCount);
            UpdateSettings(timeStep);

            for (int i = 0; i < substepCount; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }

        void RunSimulationStep()
        {
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ExternalForces);
            RunSpatial();
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.UpdateTemperature);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.UpdateThermalExpansion);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Density);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ComputeColorGradient);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ThermalBuoyancy);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Pressure);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Viscosity);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Cohesion);
            if (ShouldRecomputeBlobIDs())
            {
                RecomputeBlobIDs();
            }
            bool needsNonCoalescenceDebug = _particleDisplay != null && _particleDisplay.ComputeVectorFieldMode == 2;
            if ((carrierWedgeStrength > 0 && carrierWedgeDistanceMultiplier > 0) || needsNonCoalescenceDebug)
            {
                ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.CarrierWedge);
            }
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Csf); // ADDED
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.UpdatePosition);

        }

        public void RefreshDebugBuffers()
        {
            if (!Application.isPlaying || compute == null || NumParticles <= 0 || positionBuffer == null)
            {
                return;
            }

            UploadPhaseDataIfDirty();
            UpdateSettings(0f);
            RunSpatial();
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Density);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ComputeColorGradient);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ThermalBuoyancy);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Viscosity);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.CarrierWedge);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Csf);
        }

        void RunSpatial()
        {
            // Hash/reorder all particles so static ghost particles can participate in neighbour sampling
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.SpatialHash);
            spatialHash.Run();
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Reorder);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.Copyback);
        }

        bool ShouldRecomputeBlobIDs()
        {
            int interval = Mathf.Max(1, blobIdUpdateInterval);
            _blobStepCounter++;
            return _blobStepCounter % interval == 0;
        }

        void RecomputeBlobIDs()
        {
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.InitializeBlobIds);
            int iterations = Mathf.Max(1, blobPropagationIterations);
            for (int i = 0; i < iterations; i++)
            {
                ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.PropagateBlobIds);
                ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.CopyBlobIds);
            }
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.ClearBlobSizes);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.CountBlobSizes);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.MarkSingleParticleBlobs);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: kernels.CopyBlobIdsToPrevious);
        }

        void UpdateSettings(float deltaTime)
        {
            compute.SetFloat("deltaTime", deltaTime);
            compute.SetFloat("gravity", gravity);
            compute.SetFloat("collisionDamping", collisionDamping);
            float effectiveSmoothingRadius = EffectiveSmoothingRadius;
            compute.SetFloat("smoothingRadius", effectiveSmoothingRadius);
            compute.SetFloat("pressureMultiplier", pressureMultiplier);
            compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
            compute.SetFloat("interfaceViscosityMultiplier", interfaceViscosityMultiplier);
            compute.SetVector("boundsSize", boundsSize);
            compute.SetFloat("obstacleY", analyticBoundary.obstacleY);
            compute.SetBool("useEllipticalBounds", analyticBoundary.useEllipticalBounds);
            compute.SetVector("ellipseBoundsSize", analyticBoundary.ellipseBoundsSize);
            compute.SetVector("ellipseBoundsCenter", analyticBoundary.ellipseBoundsCenter);
            compute.SetInt("wallFilmPhase", PhaseFilterToIndex(wallFilmPhase));
            compute.SetFloat("wallFilmDistance", wallFilmDistance);
            compute.SetFloat("wallFilmStrength", wallFilmStrength);
            compute.SetInt("wallFilmAttractionPhase", PhaseFilterToIndex(wallFilmAttractionPhase));
            compute.SetFloat("wallFilmAttractionStrength", wallFilmAttractionStrength);
            compute.SetFloat("wallFilmMaxAcceleration", wallFilmMaxAcceleration);

            compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(effectiveSmoothingRadius, 8)));
            compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(effectiveSmoothingRadius, 5)));
            compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(effectiveSmoothingRadius, 4)));
            compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(effectiveSmoothingRadius, 5) * Mathf.PI));
            compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(effectiveSmoothingRadius, 4) * Mathf.PI));
            compute.SetFloat("edgeForce", edgeForce);
            compute.SetFloat("edgeForceDst", edgeForceDst);
            compute.SetFloat("wallPressureStrength", wallPressureStrength);
            compute.SetFloat("wallPressureRadius", wallPressureRadius);
            compute.SetInt("numFluidParticles", NumFluidParticles);
            compute.SetInt("numSpatialParticles", NumParticles);

            compute.SetFloat("ambientTemperature", ambientTemperature);
            compute.SetFloat("crossPhaseThermalDiffusion", crossPhaseThermalDiffusion);
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
            float surfaceTensionScale = 1f / Mathf.Sqrt(particleResolutionFactor);
            compute.SetFloat("surfaceTension", surfaceTension * surfaceTensionScale);
            compute.SetInt("surfaceTensionInterfaceMode", (int)surfaceTensionInterfaceMode);
            compute.SetFloat("surfaceTensionThreshold", surfaceTensionThreshold);
            compute.SetFloat("blobBlobSurfaceTension", blobBlobSurfaceTension * surfaceTensionScale);
            compute.SetFloat("blobSelfSurfaceTension", blobSelfSurfaceTension * surfaceTensionScale);
            compute.SetInt("blobSelfSurfaceTensionPhase", PhaseFilterToIndex(blobSelfSurfaceTensionPhase));
            compute.SetFloat("maxSurfaceTensionCurvature", maxSurfaceTensionCurvature);
            compute.SetInt("debugVisualizationMode", _particleDisplay != null ? (int)_particleDisplay.debugMode : 0);
            compute.SetInt("debugVectorFieldMode", _particleDisplay != null ? _particleDisplay.ComputeVectorFieldMode : 0);

            ParticleFluidSimulationInput.Interaction interaction = simulationInput.PollInteraction(interactionStrength);

            compute.SetVector("interactionInputPoint", interaction.position);
            compute.SetFloat("interactionInputStrength", interaction.strength);
            compute.SetFloat("interactionInputRadius", interactionRadius);
            compute.SetFloat("blobBlobCohesion", blobBlobCohesion);
            compute.SetInt("carrierWedgePhase", ClampPhaseIndex(carrierWedgePhase));
            compute.SetFloat("carrierWedgeDistanceMultiplier", carrierWedgeDistanceMultiplier);
            compute.SetFloat("carrierWedgeStrength", carrierWedgeStrength);
            compute.SetFloat("carrierWedgeViscosityMultiplier", carrierWedgeViscosityMultiplier);
            compute.SetBool("carrierWedgeInterfaceOnly", carrierWedgeInterfaceOnly);
            compute.SetFloat("carrierWedgeInterfaceThreshold", carrierWedgeInterfaceThreshold);
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

        float ResolveLowerGhostPhaseWidth()
        {
            if (lowerGhostPhaseWidth > 0f)
            {
                return lowerGhostPhaseWidth;
            }

            HeatSource2D source = blobMergeCoil != null ? blobMergeCoil : heatSource;
            if (source != null && source.isActiveAndEnabled)
            {
                return Mathf.Max(0f, source.Size.x);
            }

            return 0f;
        }

        public float EffectiveSmoothingRadius => smoothingRadius / Mathf.Sqrt(particleResolutionFactor);

        float EffectiveTargetDensity(int phaseIndex)
        {
            return EffectiveTargetDensity(phases[Mathf.Clamp(phaseIndex, 0, phases.Length - 1)]);
        }

        float EffectiveTargetDensity(PhaseConfig phase)
        {
            return phase.targetDensity * particleResolutionFactor;
        }

        void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
        {
            // Combine fluid and ghost particles into single arrays
            float2[] allPositions = new float2[NumParticles];
            float2[] allVelocities = new float2[NumParticles];
            int[] allPhases = new int[NumParticles];
            uint[] allGhostFlags = new uint[NumParticles];

            // Fluid particles first
            Array.Copy(spawnData.positions, 0, allPositions, 0, NumFluidParticles);
            Array.Copy(spawnData.velocities, 0, allVelocities, 0, NumFluidParticles);
            for (int i = 0; i < NumFluidParticles; i++)
            {
                allPhases[i] = spawnData.phases[i];
                allGhostFlags[i] = 0;
            }

            // Ghost particles after
            for (int i = 0; i < NumGhostParticles; i++)
            {
                allPositions[NumFluidParticles + i] = _ghostPositions[i];
                allVelocities[NumFluidParticles + i] = _ghostVelocities[i];
                allPhases[NumFluidParticles + i] = _ghostPhases[i];
                allGhostFlags[NumFluidParticles + i] = 1;
            }

            positionBuffer.SetData(allPositions);
            resources.predictedPositionBuffer.SetData(allPositions);
            velocityBuffer.SetData(allVelocities);
            phaseBuffer.SetData(allPhases);
            ghostFlagBuffer.SetData(allGhostFlags);
            uint[] initialBlobIds = new uint[NumParticles];
            blobIdBuffer.SetData(initialBlobIds);
            resources.blobIdPreviousBuffer.SetData(initialBlobIds);

            // ADDED: reset temperatures to ambient on reset
            float[] initialTemps = new float[NumParticles];
            for (int i = 0; i < NumParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);

            float[] initialTargetDensities = new float[NumParticles];
            for (int i = 0; i < NumFluidParticles; i++)
                initialTargetDensities[i] = EffectiveTargetDensity(spawnData.phases[i]);
            for (int i = NumFluidParticles; i < NumParticles; i++)
                initialTargetDensities[i] = EffectiveTargetDensity(_ghostPhases[i - NumFluidParticles]);
            resources.particleTargetDensityBuffer.SetData(initialTargetDensities);
            _blobStepCounter = 0;
        }

        void HandleInput()
        {
            ParticleFluidSimulationInput.Commands commands = simulationInput.PollCommands();
            if (commands.togglePause)
            {
                isPaused = !isPaused;
            }

            if (commands.stepFrame)
            {
                isPaused = false;
                _pauseNextFrame = true;
            }

            if (commands.reset)
            {
                isPaused = true;
                SetInitialBufferData(_spawnData);
                RunSimulationStep();
                SetInitialBufferData(_spawnData);
            }
        }

        void OnDestroy()
        {
            resources.Release();
            spatialHash?.Release();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0, 1, 0, 0.4f);
            
            if (!analyticBoundary.useEllipticalBounds)
            {
                Gizmos.DrawWireCube(Vector2.zero, boundsSize);
            }

            if (Application.isPlaying)
            {
                ParticleFluidSimulationInput.Interaction interaction = simulationInput.PollInteraction(interactionStrength);
                if (interaction.isActive)
                {
                    Gizmos.color = interaction.isPull ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(interaction.position, interactionRadius);
                }
            }
        }
    }
}
