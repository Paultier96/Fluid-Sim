using Seb.Fluid2D.Rendering;
using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
    internal sealed class ParticleFluidSimulationSettingsUploader
    {
        private static readonly int BoundsSize = Shader.PropertyToID("boundsSize");
        private static readonly int DeltaTime = Shader.PropertyToID("deltaTime");
        private static readonly int Gravity = Shader.PropertyToID("gravity");
        private static readonly int CollisionDamping = Shader.PropertyToID("collisionDamping");
        private static readonly int SmoothingRadius = Shader.PropertyToID("smoothingRadius");
        private static readonly int PressureMultiplier = Shader.PropertyToID("pressureMultiplier");
        private static readonly int NearPressureMultiplier = Shader.PropertyToID("nearPressureMultiplier");
        private static readonly int InterfaceViscosityMultiplier = Shader.PropertyToID("interfaceViscosityMultiplier");
        private static readonly int NumFluidParticles = Shader.PropertyToID("numFluidParticles");
        private static readonly int ObstacleY = Shader.PropertyToID("obstacleY");
        private static readonly int UseEllipticalBounds = Shader.PropertyToID("useEllipticalBounds");
        private static readonly int EllipseBoundsCenter = Shader.PropertyToID("ellipseBoundsCenter");
        private static readonly int WallFilmPhase = Shader.PropertyToID("wallFilmPhase");
        private static readonly int WallFilmDistance = Shader.PropertyToID("wallFilmDistance");
        private static readonly int WallFilmStrength = Shader.PropertyToID("wallFilmStrength");
        private static readonly int WallFilmAttractionPhase = Shader.PropertyToID("wallFilmAttractionPhase");
        private static readonly int WallFilmAttractionStrength = Shader.PropertyToID("wallFilmAttractionStrength");
        private static readonly int WallFilmMaxAcceleration = Shader.PropertyToID("wallFilmMaxAcceleration");
        private static readonly int EdgeForce = Shader.PropertyToID("edgeForce");
        private static readonly int EdgeForceDst = Shader.PropertyToID("edgeForceDst");
        private static readonly int WallPressureStrength = Shader.PropertyToID("wallPressureStrength");
        private static readonly int WallPressureRadius = Shader.PropertyToID("wallPressureRadius");
        private static readonly int Poly6ScalingFactor = Shader.PropertyToID("Poly6ScalingFactor");
        private static readonly int SpikyPow3ScalingFactor = Shader.PropertyToID("SpikyPow3ScalingFactor");
        private static readonly int SpikyPow2ScalingFactor = Shader.PropertyToID("SpikyPow2ScalingFactor");
        private static readonly int SpikyPow3DerivativeScalingFactor = Shader.PropertyToID("SpikyPow3DerivativeScalingFactor");
        private static readonly int SpikyPow2DerivativeScalingFactor = Shader.PropertyToID("SpikyPow2DerivativeScalingFactor");
        private static readonly int AmbientTemperature = Shader.PropertyToID("ambientTemperature");
        private static readonly int CrossPhaseThermalDiffusion = Shader.PropertyToID("crossPhaseThermalDiffusion");
        private static readonly int GhostCoolingMultiplier = Shader.PropertyToID("ghostCoolingMultiplier");
        private static readonly int AmbientCoolingRate = Shader.PropertyToID("ambientCoolingRate");
        private static readonly int WallCoolingRate = Shader.PropertyToID("wallCoolingRate");
        private static readonly int WallCoolingDistance = Shader.PropertyToID("wallCoolingDistance");
        private static readonly int BuoyancyInversionStrength = Shader.PropertyToID("buoyancyInversionStrength");
        private static readonly int BuoyancyInversionClamp = Shader.PropertyToID("buoyancyInversionClamp");
        private static readonly int HeatSourcePos = Shader.PropertyToID("heatSourcePos");
        private static readonly int HeatSourceSize = Shader.PropertyToID("heatSourceSize");
        private static readonly int HeatSourceShape = Shader.PropertyToID("heatSourceShape");
        private static readonly int HeatSourceTemperature = Shader.PropertyToID("heatSourceTemperature");
        private static readonly int HeatSourceTransferRate = Shader.PropertyToID("heatSourceTransferRate");
        private static readonly int HeatSourceFalloffPower = Shader.PropertyToID("heatSourceFalloffPower");
        private static readonly int RestrictBlobMergingToCoil = Shader.PropertyToID("restrictBlobMergingToCoil");
        private static readonly int BlobMergeCoilPos = Shader.PropertyToID("blobMergeCoilPos");
        private static readonly int BlobMergeCoilSize = Shader.PropertyToID("blobMergeCoilSize");
        private static readonly int BlobMergeCoilShape = Shader.PropertyToID("blobMergeCoilShape");
        private static readonly int CoilVelocityDamping = Shader.PropertyToID("coilVelocityDamping");
        private static readonly int CoilAttractionStrength = Shader.PropertyToID("coilAttractionStrength");
        private static readonly int CoilVelocityDampingPhase = Shader.PropertyToID("coilVelocityDampingPhase");
        private static readonly int SurfaceTension = Shader.PropertyToID("surfaceTension");
        private static readonly int SurfaceTensionInterfaceMode = Shader.PropertyToID("surfaceTensionInterfaceMode");
        private static readonly int SurfaceTensionThreshold = Shader.PropertyToID("surfaceTensionThreshold");
        private static readonly int BlobBlobSurfaceTension = Shader.PropertyToID("blobBlobSurfaceTension");
        private static readonly int BlobSelfSurfaceTension = Shader.PropertyToID("blobSelfSurfaceTension");
        private static readonly int BlobSelfSurfaceTensionPhase = Shader.PropertyToID("blobSelfSurfaceTensionPhase");
        private static readonly int MaxSurfaceTensionCurvature = Shader.PropertyToID("maxSurfaceTensionCurvature");
        private static readonly int InteractionInputPoint = Shader.PropertyToID("interactionInputPoint");
        private static readonly int InteractionInputVelocity = Shader.PropertyToID("interactionInputVelocity");
        private static readonly int InteractionInputStrength = Shader.PropertyToID("interactionInputStrength");
        private static readonly int InteractionInputRadius = Shader.PropertyToID("interactionInputRadius");
        private static readonly int CursorVelocityTransferStrength = Shader.PropertyToID("cursorVelocityTransferStrength");
        private static readonly int CursorTemperatureBrushActive = Shader.PropertyToID("cursorTemperatureBrushActive");
        private static readonly int CursorTemperatureBrushRadius = Shader.PropertyToID("cursorTemperatureBrushRadius");
        private static readonly int CursorTemperatureBrushTarget = Shader.PropertyToID("cursorTemperatureBrushTarget");
        private static readonly int CursorTemperatureBrushTransferRate = Shader.PropertyToID("cursorTemperatureBrushTransferRate");
        private static readonly int BlobBlobCohesion = Shader.PropertyToID("blobBlobCohesion");
        private static readonly int CarrierWedgePhase = Shader.PropertyToID("carrierWedgePhase");
        private static readonly int CarrierWedgeDistanceMultiplier = Shader.PropertyToID("carrierWedgeDistanceMultiplier");
        private static readonly int CarrierWedgeStrength = Shader.PropertyToID("carrierWedgeStrength");
        private static readonly int CarrierWedgeViscosityMultiplier = Shader.PropertyToID("carrierWedgeViscosityMultiplier");
        private static readonly int CarrierWedgeInterfaceOnly = Shader.PropertyToID("carrierWedgeInterfaceOnly");
        private static readonly int CarrierWedgeInterfaceThreshold = Shader.PropertyToID("carrierWedgeInterfaceThreshold");
        private static readonly int CarrierWedgeCoilStrengthMultiplier = Shader.PropertyToID("carrierWedgeCoilStrengthMultiplier");
        private static readonly int CarrierWedgeMaxAcceleration = Shader.PropertyToID("carrierWedgeMaxAcceleration");
        private static readonly int CarrierWedgeMaxDirectionDot = Shader.PropertyToID("carrierWedgeMaxDirectionDot");

        public void Upload(
            ComputeShader compute,
            FluidSim2D sim,
            ParticleFluidSimulationInput simulationInput,
            ParticleFluidSimulationDebug simulationDebug,
            ParticleDisplay2D particleDisplay,
            float deltaTime)
        {
            compute.SetFloat(DeltaTime, deltaTime);
            compute.SetFloat(Gravity, sim.gravity);
            compute.SetFloat(CollisionDamping, sim.collisionDamping);
            compute.SetFloat(SmoothingRadius, sim.EffectiveSmoothingRadius);
            compute.SetFloat(PressureMultiplier, sim.pressureMultiplier);
            compute.SetFloat(NearPressureMultiplier, sim.nearPressureMultiplier);
            compute.SetFloat(InterfaceViscosityMultiplier, sim.interfaceViscosityMultiplier);

            UploadBoundarySettings(compute, sim);
            UploadKernelSettings(compute, sim.EffectiveSmoothingRadius);
            compute.SetInt(NumFluidParticles, sim.spawnData.positions.Length);

            UploadThermalSettings(compute, sim);
            UploadHeatSourceSettings(compute, sim.heatSource);
            UploadMergeCoilSettings(compute, sim);
            UploadSurfaceTensionSettings(compute, sim);
            simulationDebug.UploadSettings(compute, particleDisplay);
            UploadInteractionSettings(compute, sim, simulationInput, particleDisplay);
            UploadCarrierWedgeSettings(compute, sim);
        }

        static void UploadBoundarySettings(ComputeShader compute, FluidSim2D sim)
        {
            compute.SetVector(BoundsSize, sim.analyticBoundary.boundsSize);
            compute.SetFloat(ObstacleY, sim.analyticBoundary.obstacleY);
            compute.SetBool(UseEllipticalBounds, sim.analyticBoundary.useEllipticalBounds);
            compute.SetVector(EllipseBoundsCenter, sim.analyticBoundary.BoundsCenter);
            compute.SetInt(WallFilmPhase, PhaseFilterToIndex(sim, sim.wallFilmPhase));
            compute.SetFloat(WallFilmDistance, sim.wallFilmDistance);
            compute.SetFloat(WallFilmStrength, sim.wallFilmStrength);
            compute.SetInt(WallFilmAttractionPhase, PhaseFilterToIndex(sim, sim.wallFilmAttractionPhase));
            compute.SetFloat(WallFilmAttractionStrength, sim.wallFilmAttractionStrength);
            compute.SetFloat(WallFilmMaxAcceleration, sim.wallFilmMaxAcceleration);
            compute.SetFloat(EdgeForce, sim.edgeForce);
            compute.SetFloat(EdgeForceDst, sim.edgeForceDst);
            compute.SetFloat(WallPressureStrength, sim.wallPressureStrength);
            compute.SetFloat(WallPressureRadius, sim.wallPressureRadius);
        }

        static void UploadKernelSettings(ComputeShader compute, float smoothingRadius)
        {
            compute.SetFloat(Poly6ScalingFactor, 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
            compute.SetFloat(SpikyPow3ScalingFactor, 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
            compute.SetFloat(SpikyPow2ScalingFactor, 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
            compute.SetFloat(SpikyPow3DerivativeScalingFactor, 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
            compute.SetFloat(SpikyPow2DerivativeScalingFactor, 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));
        }

        static void UploadThermalSettings(ComputeShader compute, FluidSim2D sim)
        {
            compute.SetFloat(AmbientTemperature, sim.ambientTemperature);
            compute.SetFloat(CrossPhaseThermalDiffusion, sim.crossPhaseThermalDiffusion);
            compute.SetFloat(GhostCoolingMultiplier, sim.ghostCoolingMultiplier);
            compute.SetFloat(AmbientCoolingRate, sim.ambientCoolingRate);
            compute.SetFloat(WallCoolingRate, sim.wallCoolingRate);
            compute.SetFloat(WallCoolingDistance, sim.wallCoolingDistance);
            compute.SetFloat(BuoyancyInversionStrength, sim.buoyancyInversionStrength);
            compute.SetFloat(BuoyancyInversionClamp, Mathf.Max(0f, sim.buoyancyInversionClamp));
        }

        static void UploadHeatSourceSettings(ComputeShader compute, HeatSource2D heatSource)
        {
            if (heatSource == null || !heatSource.isActiveAndEnabled)
            {
                return;
            }

            compute.SetVector(HeatSourcePos, heatSource.Position);
            compute.SetVector(HeatSourceSize, heatSource.Size);
            compute.SetInt(HeatSourceShape, heatSource.shape == HeatSource2D.HeatSourceShape.Rectangular ? 0 : 1);
            compute.SetFloat(HeatSourceTemperature, heatSource.temperature);
            compute.SetFloat(HeatSourceTransferRate, heatSource.transferRate);
            compute.SetFloat(HeatSourceFalloffPower, heatSource.falloffPower);
        }

        static void UploadMergeCoilSettings(ComputeShader compute, FluidSim2D sim)
        {
            HeatSource2D mergeCoilSource = sim.blobMergeCoil != null ? sim.blobMergeCoil : sim.heatSource;
            bool hasMergeCoilSource = mergeCoilSource != null && mergeCoilSource.isActiveAndEnabled;
            bool restrictMergingToCoil = sim.restrictBlobMergingToCoil && hasMergeCoilSource;
            bool uploadMergeCoilArea = (restrictMergingToCoil || sim.carrierWedgeCoilStrengthMultiplier < 1f || sim.coilVelocityDamping > 0f || sim.coilAttractionStrength > 0f) && hasMergeCoilSource;
            Vector2 mergeCoilPos = Vector2.zero;
            Vector2 mergeCoilSize = Vector2.zero;
            int mergeCoilShape = 1;
            if (uploadMergeCoilArea)
            {
                mergeCoilPos = mergeCoilSource.Position;
                mergeCoilSize = mergeCoilSource.Size;
                mergeCoilShape = mergeCoilSource.shape == HeatSource2D.HeatSourceShape.Rectangular ? 0 : 1;
            }

            compute.SetBool(RestrictBlobMergingToCoil, restrictMergingToCoil);
            compute.SetVector(BlobMergeCoilPos, mergeCoilPos);
            compute.SetVector(BlobMergeCoilSize, mergeCoilSize);
            compute.SetInt(BlobMergeCoilShape, mergeCoilShape);
            compute.SetFloat(CoilVelocityDamping, sim.coilVelocityDamping);
            compute.SetFloat(CoilAttractionStrength, sim.coilAttractionStrength);
            compute.SetInt(CoilVelocityDampingPhase, PhaseFilterToIndex(sim, sim.coilVelocityDampingPhase));
        }

        static void UploadSurfaceTensionSettings(ComputeShader compute, FluidSim2D sim)
        {
            float surfaceTensionScale = 1f / Mathf.Sqrt(sim.particleResolutionFactor);
            compute.SetFloat(SurfaceTension, sim.surfaceTension * surfaceTensionScale);
            compute.SetInt(SurfaceTensionInterfaceMode, (int)sim.surfaceTensionInterfaceMode);
            compute.SetFloat(SurfaceTensionThreshold, sim.surfaceTensionThreshold);
            compute.SetFloat(BlobBlobSurfaceTension, sim.blobBlobSurfaceTension * surfaceTensionScale);
            compute.SetFloat(BlobSelfSurfaceTension, sim.blobSelfSurfaceTension * surfaceTensionScale);
            compute.SetInt(BlobSelfSurfaceTensionPhase, PhaseFilterToIndex(sim, sim.blobSelfSurfaceTensionPhase));
            compute.SetFloat(MaxSurfaceTensionCurvature, sim.maxSurfaceTensionCurvature);
        }

        static void UploadInteractionSettings(
            ComputeShader compute,
            FluidSim2D sim,
            ParticleFluidSimulationInput simulationInput,
            ParticleDisplay2D particleDisplay)
        {
            ParticleFluidSimulationInput.Interaction interaction = simulationInput.PollInteraction(particleDisplay,
                sim.interactionStrength,
                sim.cursorHeatBrushTemperature,
                sim.cursorCoolBrushTemperature);
            compute.SetVector(InteractionInputPoint, interaction.position);
            compute.SetVector(InteractionInputVelocity, interaction.velocity);
            compute.SetFloat(InteractionInputStrength, interaction.strength);
            compute.SetFloat(InteractionInputRadius, sim.interactionRadius);
            compute.SetFloat(CursorVelocityTransferStrength, sim.cursorVelocityTransferStrength);
            compute.SetBool(CursorTemperatureBrushActive, interaction.heatBrushActive);
            compute.SetFloat(CursorTemperatureBrushRadius, sim.cursorTemperatureBrushRadius > 0f ? sim.cursorTemperatureBrushRadius : sim.interactionRadius);
            compute.SetFloat(CursorTemperatureBrushTarget, interaction.heatBrushTargetTemperature);
            compute.SetFloat(CursorTemperatureBrushTransferRate, sim.cursorTemperatureBrushTransferRate * interaction.heatBrushStrength);
        }

        static void UploadCarrierWedgeSettings(ComputeShader compute, FluidSim2D sim)
        {
            compute.SetFloat(BlobBlobCohesion, sim.blobBlobCohesion);
            compute.SetInt(CarrierWedgePhase, ClampPhaseIndex(sim, sim.carrierWedgePhase));
            compute.SetFloat(CarrierWedgeDistanceMultiplier, sim.carrierWedgeDistanceMultiplier);
            compute.SetFloat(CarrierWedgeStrength, sim.carrierWedgeStrength);
            compute.SetFloat(CarrierWedgeViscosityMultiplier, sim.carrierWedgeViscosityMultiplier);
            compute.SetBool(CarrierWedgeInterfaceOnly, sim.carrierWedgeInterfaceOnly);
            compute.SetFloat(CarrierWedgeInterfaceThreshold, sim.carrierWedgeInterfaceThreshold);
            compute.SetFloat(CarrierWedgeCoilStrengthMultiplier, sim.carrierWedgeCoilStrengthMultiplier);
            compute.SetFloat(CarrierWedgeMaxAcceleration, sim.carrierWedgeMaxAcceleration);
            compute.SetFloat(CarrierWedgeMaxDirectionDot, sim.carrierWedgeMaxDirectionDot);
        }

        static int ClampPhaseIndex(FluidSim2D sim, FluidSim2D.LiquidPhase phase)
        {
            return Mathf.Clamp((int)phase, 0, sim.phases.Length - 1);
        }

        static int PhaseFilterToIndex(FluidSim2D sim, FluidSim2D.LiquidPhaseFilter phase)
        {
            return phase == FluidSim2D.LiquidPhaseFilter.All
                ? -1
                : Mathf.Clamp((int)phase, 0, sim.phases.Length - 1);
        }
    }
}
