using Seb.Helpers;
using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Header("Interaction Settings")]
        public float interactionRadius;
        public float interactionStrength;

        [Header("Phases")]
        public PhaseConfig[] phases;

        [System.Serializable]
        public class PhaseConfig
        {
            public string name = "Water";
            public float targetDensity = 234;
            public float viscosity = 0.03f;
            public Color color = Color.blue;
        }

        [Range(0f, 1f)]
        public float phaseSeparation = 0.3f;

        public float[] phaseCohesionValues = new float[] { 0.5f, -0.1f, 0.5f };

        // ADDED: temperature settings
        [Header("Temperature")]
        public float ambientTemperature = 20f;
        public float heatDiffusionRate = 0.5f;
        public float heatCoolingRate = 0.1f;
        public float heatSourceTemperature = 100f;
        public float heatSinkTemperature = 10f;
        public Vector2 heatSourcePos = new Vector2(0, -5f);
        public float heatSourceRadius = 2f;

        [Header("References")]
        public ComputeShader compute;
        public Spawner2D spawner2D;

        // Buffers
        public ComputeBuffer positionBuffer { get; private set; }
        public ComputeBuffer velocityBuffer { get; private set; }
        public ComputeBuffer densityBuffer { get; private set; }
        public ComputeBuffer phaseBuffer { get; private set; }
        public ComputeBuffer temperatureBuffer { get; private set; }

        ComputeBuffer sortTarget_Position;
        ComputeBuffer sortTarget_PredicitedPosition;
        ComputeBuffer sortTarget_Velocity;
        ComputeBuffer sortTarget_Phases;
        ComputeBuffer sortTarget_Temperatures;

        ComputeBuffer phaseTargetDensityBuffer;
        ComputeBuffer phaseViscosityBuffer;
        ComputeBuffer phaseInteractionBuffer;

        ComputeBuffer predictedPositionBuffer;
        SpatialHash spatialHash;

        public float[,] interactionMatrix = new float[,]
        {
            { 1.0f, 0.3f },
            { 0.3f, 1.0f }
        };

        // Kernel IDs
        const int externalForcesKernel = 0;
        const int spatialHashKernel = 1;
        const int reorderKernel = 2;
        const int copybackKernel = 3;
        const int densityKernel = 4;
        const int pressureKernel = 5;
        const int viscosityKernel = 6;
        const int updatePositionKernel = 7;
        const int updateTemperatureKernel = 8;
        const int reorderTemperatureKernel = 9;
        const int copybackTemperatureKernel = 10;

        // State
        bool isPaused;
        Spawner2D.ParticleSpawnData spawnData;
        bool pauseNextFrame;

        public int numParticles { get; private set; }

        // Runtime-change tracking
        Rendering.ParticleDisplay2D particleDisplay;
        int prevPhaseCount = -1;
        float prevPhaseSeparation = float.NaN;
        float[] prevPhaseTargetDensities;
        float[] prevPhaseViscosities;
        Color[] prevPhaseColors;

        void Start()
        {
            particleDisplay = GetComponent<Rendering.ParticleDisplay2D>();
            if (phases != null)
                particleDisplay?.SetPhaseColors(phases.Select(p => p.color).ToArray());

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

            // ADDED: create temperature buffer, initialize all particles to ambient
            temperatureBuffer = ComputeHelper.CreateStructuredBuffer<float>(numParticles);
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);

            sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
            sortTarget_Phases = ComputeHelper.CreateStructuredBuffer<int>(numParticles);
            sortTarget_Temperatures = ComputeHelper.CreateStructuredBuffer<float>(numParticles);


            CreateOrUpdatePhaseBuffers(initial: true);

            SetInitialBufferData(spawnData);

            // Bind buffers
            ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseBuffer, "Phases", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel, updatePositionKernel, reorderKernel, copybackKernel);

            ComputeHelper.SetBuffer(compute, temperatureBuffer, "Temperatures", updateTemperatureKernel, reorderTemperatureKernel, copybackTemperatureKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Temperatures, "SortTarget_Temperatures", reorderTemperatureKernel, copybackTemperatureKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel, reorderTemperatureKernel);

            ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel);
            ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updateTemperatureKernel);

            ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, sortTarget_Phases, "SortTarget_Phases", reorderKernel, copybackKernel);
            ComputeHelper.SetBuffer(compute, phaseViscosityBuffer, "PhaseViscosities", viscosityKernel);
            ComputeHelper.SetBuffer(compute, phaseTargetDensityBuffer, "PhaseTargetDensities", externalForcesKernel, densityKernel, pressureKernel, viscosityKernel);

            compute.SetInt("numParticles", numParticles);
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

        void CreateOrUpdatePhaseBuffers(bool initial)
        {
            if (phases == null) return;

            int phaseCount = phases.Length;

            float[] currentTargetDensities = phases.Select(p => p.targetDensity).ToArray();
            float[] currentViscosities = phases.Select(p => p.viscosity).ToArray();
            Color[] currentColors = phases.Select(p => p.color).ToArray();
            bool densitiesChanged = !FloatArrayEquals(currentTargetDensities, prevPhaseTargetDensities);
            bool viscositiesChanged = !FloatArrayEquals(currentViscosities, prevPhaseViscosities);
            bool colorsChanged = !ColorArrayEquals(currentColors, prevPhaseColors);
            bool countChanged = phaseCount != prevPhaseCount;
            bool sepChanged = !Mathf.Approximately(prevPhaseSeparation, phaseSeparation);

            if (!initial && !densitiesChanged && !viscositiesChanged && !colorsChanged && !countChanged && !sepChanged)
                return;

            if (colorsChanged || countChanged || initial)
            {
                particleDisplay?.SetPhaseColors(currentColors);
                prevPhaseColors = currentColors;
            }

            if (countChanged && phaseTargetDensityBuffer != null)
            {
                phaseTargetDensityBuffer.Release();
                phaseViscosityBuffer?.Release();
                phaseInteractionBuffer?.Release();

                phaseTargetDensityBuffer = null;
                phaseViscosityBuffer = null;
                phaseInteractionBuffer = null;
            }

            if (phaseTargetDensityBuffer == null)
                phaseTargetDensityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            if (densitiesChanged || initial || countChanged)
                phaseTargetDensityBuffer.SetData(currentTargetDensities);

            if (phaseViscosityBuffer == null)
                phaseViscosityBuffer = new ComputeBuffer(phaseCount, sizeof(float));
            if (viscositiesChanged || initial || countChanged)
                phaseViscosityBuffer.SetData(currentViscosities);

            float[] flat = new float[phaseCount * phaseCount];
            for (int y = 0; y < phaseCount; y++)
                for (int x = 0; x < phaseCount; x++)
                    flat[y * phaseCount + x] = x == y ? 1.0f : phaseSeparation;

            if (phaseInteractionBuffer == null)
                phaseInteractionBuffer = new ComputeBuffer(flat.Length, sizeof(float));
            phaseInteractionBuffer.SetData(flat);

            if (compute != null)
            {
                compute.SetBuffer(externalForcesKernel, "PhaseTargetDensities", phaseTargetDensityBuffer);
                compute.SetBuffer(densityKernel, "PhaseTargetDensities", phaseTargetDensityBuffer);
                compute.SetBuffer(pressureKernel, "PhaseTargetDensities", phaseTargetDensityBuffer);
                compute.SetBuffer(viscosityKernel, "PhaseTargetDensities", phaseTargetDensityBuffer);

                ComputeHelper.SetBuffer(compute, phaseViscosityBuffer, "PhaseViscosities", viscosityKernel);
                ComputeHelper.SetBuffer(compute, phaseInteractionBuffer, "PhaseInteractionMatrix", pressureKernel);
            }

            prevPhaseCount = phaseCount;
            prevPhaseSeparation = phaseSeparation;
            prevPhaseTargetDensities = currentTargetDensities;
            prevPhaseViscosities = currentViscosities;
        }

        bool FloatArrayEquals(float[] a, float[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            const float eps = 1e-6f;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i] - b[i]) > eps) return false;
            return true;
        }

        bool ColorArrayEquals(Color[] a, Color[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
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
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updateTemperatureKernel);
        }

        void RunSpatial()
        {
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
            spatialHash.Run();
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderTemperatureKernel);
            ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackTemperatureKernel);
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

            compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
            compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
            compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
            compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
            compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

            // ADDED: temperature settings
            compute.SetFloat("ambientTemperature", ambientTemperature);
            compute.SetFloat("heatDiffusionRate", heatDiffusionRate);
            compute.SetFloat("heatCoolingRate", heatCoolingRate);
            compute.SetFloat("heatSourceTemperature", heatSourceTemperature);
            compute.SetFloat("heatSinkTemperature", heatSinkTemperature);
            compute.SetVector("heatSourcePos", heatSourcePos);
            compute.SetFloat("heatSourceRadius", heatSourceRadius);

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

            // ADDED: reset temperatures to ambient on reset
            float[] initialTemps = new float[numParticles];
            for (int i = 0; i < numParticles; i++)
                initialTemps[i] = ambientTemperature;
            temperatureBuffer.SetData(initialTemps);
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
            if (temperatureBuffer != null) temperatureBuffer.Release();

            if (sortTarget_Position != null) sortTarget_Position.Release();
            if (sortTarget_Velocity != null) sortTarget_Velocity.Release();
            if (sortTarget_PredicitedPosition != null) sortTarget_PredicitedPosition.Release();
            if (sortTarget_Phases != null) sortTarget_Phases.Release();
            if (sortTarget_Temperatures != null) sortTarget_Temperatures.Release();

            if (phaseInteractionBuffer != null) phaseInteractionBuffer.Release();
            if (phaseViscosityBuffer != null) phaseViscosityBuffer.Release();
            if (phaseTargetDensityBuffer != null) phaseTargetDensityBuffer.Release();

            spatialHash?.Release();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0, 1, 0, 0.4f);
            Gizmos.DrawWireCube(Vector2.zero, boundsSize);
            Gizmos.DrawWireCube(obstacleCentre, obstacleSize);

            // ADDED: draw heat source in editor
            Gizmos.color = new Color(1, 0.3f, 0, 0.5f);
            Gizmos.DrawWireSphere(heatSourcePos, heatSourceRadius);

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
    }
}