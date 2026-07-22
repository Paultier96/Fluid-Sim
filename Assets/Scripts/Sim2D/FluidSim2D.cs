using Seb.Helpers;
using System;
using Seb.Fluid2D.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Seb.Fluid2D.Simulation
{
    [RequireComponent(typeof(ParticleFluidAnalyticBoundary2D))]
    public class FluidSim2D : MonoBehaviour
    {
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
        [Tooltip("Automatically adjusts simulation iterations per rendered frame from monitor refresh rate, time scale, and measured frame performance.")]
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
        
        [Serializable]
        public class PhaseConfig
        {
            public string name = "Water";
            public float targetDensity = 234;
            public float viscosity = 0.03f;
            [Tooltip("Fractional viscosity change per degree relative to ambient temperature. Positive values make hot fluid less viscous and cold fluid more viscous.")]
            public float viscosityTemperatureSensitivity;
            public float thermalExpansion;
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

        public float[] phaseCohesionValues = { 0.5f, -0.1f, 0.5f };
        [Tooltip("Cohesion override for same-phase particles that belong to different blobs. Negative values repel.")]
        public float blobBlobCohesion = -0.1f;
        [Tooltip("Carrier phase pushed into the gap between two nearby blob IDs. For the lava lamp setup this is usually the water phase.")]
        public LiquidPhase carrierWedgePhase = LiquidPhase.Water;
        [Tooltip("Search distance multiplier for pushing carrier fluid into wax-blob gaps. 1 = smoothing radius.")]
        [Range(0f, 8f)] public float carrierWedgeDistanceMultiplier;
        [Tooltip("Acceleration strength that pushes carrier fluid into the gap between two different blob IDs.")]
        [Min(0f)] public float carrierWedgeStrength;
        [Tooltip("Multiplier applied to carrier viscosity inside a valid wedge. 1 = no local viscosity boost.")]
        [Min(1f)] public float carrierWedgeViscosityMultiplier = 1f;
        [Tooltip("When enabled, bulk carrier particles with weak color gradients skip the expensive carrier wedge neighbour scan.")]
        public bool carrierWedgeInterfaceOnly = true;
        [Tooltip("Minimum color-gradient magnitude required for carrier wedge when interface-only mode is enabled.")]
        [Min(0f)] public float carrierWedgeInterfaceThreshold = 0.001f;
        [Tooltip("Multiplier applied to carrier wedge strength inside the blob merge coil area. 0 disables the wedge in the coil, 1 leaves it unchanged.")]
        [Range(0f, 1f)] public float carrierWedgeCoilStrengthMultiplier;
        [Tooltip("Optional cap for carrier wedge acceleration. Set to 0 for no cap.")]
        [Min(0f)] public float carrierWedgeMaxAcceleration;
        [Tooltip("Maximum dot product between directions to the two blobs. Lower values require a clearer V-shaped gap.")]
        [Range(-1f, 1f)] public float carrierWedgeMaxDirectionDot = 0.5f;

        [Header("Blob Connectivity")]
        [Tooltip("Simulation steps between blob-ID recomputation. 1 = every step.")]
        [Min(1)] public int blobIdUpdateInterval = 1;
        [Tooltip("Label-propagation passes used when recomputing blob IDs.")]
        [Min(1)] public int blobPropagationIterations = 8;
        [Tooltip("When enabled, existing blobs can split anywhere, but different blobs only merge inside the configured coil area.")]
        public bool restrictBlobMergingToCoil;
        [Tooltip("Area where separate blobs are allowed to merge. If unset, the heat source area is used.")]
        public HeatSource2D blobMergeCoil;
        [Tooltip("Damping applied to velocity moving away from the merge coil center. Useful for making wax linger without resisting entry.")]
        [Min(0f)] public float coilVelocityDamping;
        [Tooltip("Acceleration toward the merge coil center for the selected phase. Set to 0 to disable.")]
        [Min(0f)] public float coilAttractionStrength;
        [Tooltip("Phase affected by coil velocity damping and attraction.")]
        public LiquidPhaseFilter coilVelocityDampingPhase = LiquidPhaseFilter.Wax;

        [Header("Surface Tension")]
        [Tooltip("Blob-aware treats separate same-phase blobs as separate interfaces. Phase-based ignores blob IDs and only creates CSF interfaces between different phases.")]
        public SurfaceTensionInterfaceMode surfaceTensionInterfaceMode = SurfaceTensionInterfaceMode.BlobAware;
        [Tooltip("Base CSF surface tension used for phase interfaces.")]
        [Min(0f)] public float surfaceTension = 100f;
        public float surfaceTensionThreshold = 0.1f;
        public float blobBlobSurfaceTension;
        [Tooltip("Extra blob-ID based surface tension. This tries to minimize each blob's own perimeter, independent of the surrounding phase. Set to 0 to disable.")]
        [Min(0f)] public float blobSelfSurfaceTension;
        [Tooltip("Phase affected by blob self surface tension.")]
        public LiquidPhaseFilter blobSelfSurfaceTensionPhase = LiquidPhaseFilter.All;
        [Min(0.0001f)] public float maxSurfaceTensionCurvature = 10f;

        [Header("Temperature")]
        public float ambientTemperature = 20f;
        [Tooltip("Multiplier for heat diffusion between different phases. 1 = same as same-phase transfer, 0 = no cross-phase transfer.")]
        [Range(0f, 1f)] public float crossPhaseThermalDiffusion = 0.999f;
        [Tooltip("Multiplier for heat transfer to ghost particles at boundaries. Higher = faster cooling at walls.")]
        [Min(0.1f)] public float ghostCoolingMultiplier = 1.0f;
        [Tooltip("Global Newton cooling toward ambient temperature. Set to 0 to disable.")]
        [Min(0f)] public float ambientCoolingRate;
        [Tooltip("Extra cooling toward ambient for particles close to the simulation bounds.")]
        [Min(0f)] public float wallCoolingRate = 0f;
        [Tooltip("Distance from the bounds over which wall cooling fades. Set to 0 to use the smoothing radius.")]
        [Min(0f)] public float wallCoolingDistance = 0f;

        [Header("Heat Source")]
        public HeatSource2D heatSource;

        public float HeatSourceTemperature => heatSource != null && heatSource.isActiveAndEnabled ? heatSource.temperature : ambientTemperature;

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D spawner2D;

        internal readonly ParticleFluidSimulationResources resources = new();
        private readonly ParticleFluidSimulationKernels _kernels = new();
        private readonly ParticleFluidSimulationTiming _timing = new();
        private readonly ParticleFluidPhaseDataUploader _phaseDataUploader = new();
        internal readonly ParticleFluidSimulationDebug simulationDebug = new();
        private readonly ParticleFluidSimulationSettingsUploader _settingsUploader = new();
        private readonly ParticleFluidBlobDetector _blobDetector = new();
        private SpatialHash _spatialHash;

        public float[,] interactionMatrix = {
            { 1.0f, 0.3f },
            { 0.3f, 1.0f }
        };

        // State
        public bool isPaused;
        internal Spawner2D.ParticleSpawnData spawnData;
        private Spawner2D.GhostParticleData _ghostData;
        private bool _pauseNextFrame;
        private float2[] _velocityReadback;
        private float2[] _densityReadback;
        private float[] _targetDensityReadback;

        public int NumParticles { get; private set; }
        public float CurrentPlaybackSpeed { get; private set; }
        public float CurrentSimulationDeltaTime { get; private set; }
        public float CurrentSimulationSubstepDeltaTime { get; private set; }
        public int CurrentSimulationSubstepCount { get; private set; }
        public float CurrentDisplayRefreshRate { get; private set; }

        // Runtime-change tracking
        private ParticleDisplay2D _particleDisplay;
        public ParticleFluidAnalyticBoundary2D analyticBoundary;

        void Awake()
        {
            analyticBoundary ??= GetComponent<ParticleFluidAnalyticBoundary2D>();
            spawner2D ??= GetComponent<Spawner2D>();
        }

        void OnEnable()
        {
            ParticleFluidInteractionCursor2D.Actions.Player.Pause.performed += TogglePause;
        }

        void OnDisable()
        {
            ParticleFluidInteractionCursor2D.Actions.Player.Pause.performed -= TogglePause;
        }

        void Start()
        {
            _kernels.Resolve(compute);

            _particleDisplay = GetComponent<ParticleDisplay2D>();
            if (phases == null || phases.Length == 0)
                throw new InvalidOperationException("At least one phase is required.");

            if (heatSource == null)
                heatSource = FindAnyObjectByType<HeatSource2D>();

            Time.fixedDeltaTime = 1 / 60f;
            float resolvedResolutionFactor = particleResolutionFactor;
            spawnData = spawner2D.GetSpawnData(spawner2D.spawnDensity * resolvedResolutionFactor);
            _ghostData = spawner2D.GetGhostParticleData();
            NumParticles = spawnData.positions.Length + _ghostData.Count;
            _spatialHash = new SpatialHash(NumParticles);

            resources.AllocateParticleBuffers(NumParticles);
            resources.AllocateSortBuffers(NumParticles);
            UploadPhaseDataIfDirty();
            SetInitialBufferData();

            _kernels.BindParticleBuffers(compute, resources);
            _kernels.BindSpatialHashBuffers(compute, _spatialHash);
            _kernels.BindSortBuffers(compute, resources);
            _kernels.BindDebugBuffers(compute, resources);

            compute.SetInt("numParticles", NumParticles);
            compute.SetInt("NumPhases", phases.Length);
        }

        void Update()
        {
            UploadPhaseDataIfDirty();

            if (!isPaused)
            {
                ParticleFluidSimulationTiming.Frame frame = _timing.ResolveFrame(
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

            HandlePolledInput();
        }

        void TogglePause(InputAction.CallbackContext context)
        {
            isPaused = !isPaused;
        }

        void ApplyTimingFrame(ParticleFluidSimulationTiming.Frame frame)
        {
            CurrentSimulationDeltaTime = frame.deltaTime;
            CurrentSimulationSubstepDeltaTime = frame.substepDeltaTime;
            CurrentSimulationSubstepCount = frame.substepCount;
            CurrentPlaybackSpeed = frame.playbackSpeed;
            CurrentDisplayRefreshRate = frame.displayRefreshRate;
        }

        private void OnValidate()
        {
            analyticBoundary ??= GetComponent<ParticleFluidAnalyticBoundary2D>();
            _phaseDataUploader.MarkDirty();
        }

        void UploadPhaseDataIfDirty()
        {
            _phaseDataUploader.UploadIfDirty(compute, resources, _kernels, phases, particleResolutionFactor, phaseSeparation, phaseCohesionValues);
        }

        void RunSimulationFrame(float frameTime, int substepCount)
        {
            float timeStep = frameTime / Mathf.Max(1, substepCount);
            _settingsUploader.Upload(compute, this, simulationDebug, _particleDisplay, timeStep);

            for (int i = 0; i < substepCount; i++)
            {
                RunSimulationStep();
            }
        }

        void RunSimulationStep()
        {
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.ExternalForces);
            RunSpatial();
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.UpdateTemperature);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.UpdateThermalExpansion);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Density);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.ComputeColorGradient);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.ThermalBuoyancy);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Pressure);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Viscosity);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Cohesion);
            _blobDetector.RecomputeIfDue(compute, _kernels, NumParticles, blobIdUpdateInterval, blobPropagationIterations);
            bool needsNonCoalescenceDebug = _particleDisplay != null && (_particleDisplay.vectorField?.ComputeMode ?? 0) == 2;
            if ((carrierWedgeStrength > 0 && carrierWedgeDistanceMultiplier > 0) || needsNonCoalescenceDebug)
            {
                ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.CarrierWedge);
            }
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Csf);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.UpdatePosition);

        }

        public void RefreshDebugBuffers()
        {
            simulationDebug.RefreshBuffers(
                compute,
                _kernels,
                NumParticles,
                resources.positionBuffer,
                UploadPhaseDataIfDirty,
                deltaTime => _settingsUploader.Upload(compute, this, simulationDebug, _particleDisplay, deltaTime),
                RunSpatial);
        }

        void RunSpatial()
        {
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.SpatialHash);
            _spatialHash.Run();
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Reorder);
            ComputeHelper.Dispatch(compute, NumParticles, kernelIndex: _kernels.Copyback);
        }

        public float EffectiveSmoothingRadius => smoothingRadius / Mathf.Sqrt(particleResolutionFactor);


        float EffectiveTargetDensity(PhaseConfig phase)
        {
            return phase.targetDensity * particleResolutionFactor;
        }

        float[] BuildEffectiveTargetDensities()
        {
            float[] targetDensities = new float[phases.Length];
            for (int i = 0; i < targetDensities.Length; i++)
            {
                targetDensities[i] = EffectiveTargetDensity(phases[i]);
            }

            return targetDensities;
        }

        void SetInitialBufferData()
        {
            Spawner2D.InitialParticleData initialData = Spawner2D.CreateInitialParticleData(spawnData, _ghostData, BuildEffectiveTargetDensities(), ambientTemperature);
            if (initialData.positions.Length != NumParticles)
            {
                throw new InvalidOperationException($"Initial particle data count ({initialData.positions.Length}) does not match allocated simulation particle count ({NumParticles}).");
            }

            resources.positionBuffer.SetData(initialData.positions);
            resources.predictedPositionBuffer.SetData(initialData.positions);
            resources.velocityBuffer.SetData(initialData.velocities);
            resources.phaseBuffer.SetData(initialData.phases);
            resources.ghostFlagBuffer.SetData(initialData.ghostFlags);
            resources.temperatureBuffer.SetData(initialData.temperatures);
            resources.particleTargetDensityBuffer.SetData(initialData.targetDensities);
            _blobDetector.Reset(resources, NumParticles);
        }

        void HandlePolledInput()
        {
            InputSystem_Actions.PlayerActions player = ParticleFluidInteractionCursor2D.Actions.Player;
            if (player.StepFrame.WasPressedThisFrame())
            {
                isPaused = false;
                _pauseNextFrame = true;
            }

            if (player.Reset.WasPressedThisFrame())
            {
                isPaused = true;
                SetInitialBufferData();
                RunSimulationStep();
                SetInitialBufferData();
            }
        }

        void OnDestroy()
        {
            resources.Release();
            _spatialHash?.Release();
        }
    }
}
