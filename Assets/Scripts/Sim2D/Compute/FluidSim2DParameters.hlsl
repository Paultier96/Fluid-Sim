#ifndef FLUID_SIM_2D_PARAMETERS_INCLUDED
#define FLUID_SIM_2D_PARAMETERS_INCLUDED

static const int NumThreads = 64;
static const uint UnassignedBlobID = 0xFFFFFFFEu;
static const uint IgnoredPhaseBlobID = 0xFFFFFFFFu;

// Buffers
RWStructuredBuffer<float2> Positions;
RWStructuredBuffer<float2> PredictedPositions;
RWStructuredBuffer<float2> Velocities;
RWStructuredBuffer<float2> Densities;
RWStructuredBuffer<uint> Phases;
RWStructuredBuffer<uint> IsGhost;
RWStructuredBuffer<uint> BlobIDs;
RWStructuredBuffer<uint> BlobIDsPrevious;
RWStructuredBuffer<uint> BlobIDsScratch;
RWStructuredBuffer<uint> BlobSizes;
RWStructuredBuffer<float> Temperatures;
RWStructuredBuffer<float> ParticleTargetDensities;
StructuredBuffer<float2> PositionsRO;
StructuredBuffer<float2> PredictedPositionsRO;
StructuredBuffer<float2> VelocitiesRO;
StructuredBuffer<uint> IsGhostRO;
StructuredBuffer<float> TemperaturesRO;
StructuredBuffer<float> ParticleTargetDensitiesRO;
StructuredBuffer<float> PhaseCohesionMatrix;
const float blobBlobCohesion;
const int carrierWedgePhase;
const float carrierWedgeDistanceMultiplier;
const float carrierWedgeStrength;
const float carrierWedgeInterfaceThreshold;
const float carrierWedgeCoilStrengthMultiplier;
const float carrierWedgeMaxAcceleration;
const float carrierWedgeMaxDirectionDot;
const float surfaceTension;
const int surfaceTensionInterfaceMode; // 0 = blob-aware, 1 = phase-based
const float surfaceTensionThreshold; // min gradient magnitude to apply force
const float blobBlobSurfaceTension;
const float blobSelfSurfaceTension;
const int blobSelfSurfaceTensionPhase;
const float maxSurfaceTensionCurvature;

// Non-coalescence (interface repulsion) per-phase parameters
StructuredBuffer<float> PhaseNonCoalescenceRadiusMultiplier;
StructuredBuffer<float> PhaseNonCoalescenceStrength;
RWStructuredBuffer<float2> DebugData; // debug colour output (mode-dependent): gradient.xy, curvature.xx, or viscosity.xx
RWStructuredBuffer<float2> DebugVectorData; // vector overlay output: surface tension force.xy, non-coalescence force.xy, curvature normal.xy, or convection.xy
RWStructuredBuffer<float> DebugVectorSign; // signed scalar for vector overlay colouring, used by curvature combs
RWStructuredBuffer<float2> ColorGradients; // per-particle color gradient
StructuredBuffer<float2> ColorGradientsRO;
StructuredBuffer<float2> DensitiesRO;
StructuredBuffer<uint> PhasesRO;
StructuredBuffer<uint> BlobIDsRO;
RWStructuredBuffer<uint2> CellBlobSummaries;
StructuredBuffer<uint2> CellBlobSummariesRO;

// Spatial hashing
RWStructuredBuffer<uint> SpatialKeys;
RWStructuredBuffer<uint> SpatialOffsets;
StructuredBuffer<uint> SpatialKeysRO;
StructuredBuffer<uint> SpatialOffsetsRO;
StructuredBuffer<uint> SortedIndices;

// Settings
const uint numParticles;
const int numFluidParticles;  // Only first numFluidParticles are actual fluid
const float gravity;
const float deltaTime;
const float temperatureDeltaTime;
const float collisionDamping;
const float smoothingRadius;
const float edgeForce;      // magnitude of repulsive acceleration at zero distance
const float edgeForceDst;   // distance from boundary over which repulsion fades to zero
const int wallFilmPhase;
const float wallFilmDistance;
const float wallFilmStrength;
const int wallFilmAttractionPhase;
const float wallFilmAttractionStrength;
const float wallFilmMaxAcceleration;
const float wallPressureStrength; // pressure-support contribution near walls
const float wallPressureRadius;   // support distance from wall (0 => smoothingRadius)
StructuredBuffer<float> PhaseTargetDensities;
const float pressureMultiplier;
const float nearPressureMultiplier;
StructuredBuffer<float> PhaseViscosities;
StructuredBuffer<float> PhaseViscosityTemperatureSensitivity;
const float interfaceViscosityMultiplier;
const float2 boundsSize;
const float2 interactionInputPoint;
const float2 interactionInputVelocity;
const float interactionInputStrength;
const float interactionInputRadius;
const float cursorVelocityTransferStrength;
const bool cursorTemperatureBrushActive;
const float cursorTemperatureBrushRadius;
const float cursorTemperatureBrushTarget;
const float cursorTemperatureBrushTransferRate;

// Add new per-phase thermal expansion settings
StructuredBuffer<float> PhaseThermalExpansion; // how much density changes per degree

StructuredBuffer<float> PhaseInteractionMatrix;
int NumPhases;
int debugVisualizationMode;
int debugVectorFieldMode;
const float ambientTemperature;
const float crossPhaseThermalDiffusion;
StructuredBuffer<float> PhaseThermalConductivity;
StructuredBuffer<float> PhaseSpecificHeatCapacity;
const float heatCoolingRate;
const float ghostCoolingMultiplier;
const float ambientCoolingRate;
const float wallCoolingRate;
const float wallCoolingDistance;
const float heatSourceTransferRate;
const float2 heatSourcePos;
const float2 heatSourceSize;
const int heatSourceShape; // 0 = rectangular, 1 = elliptical
const float heatSourceTemperature;
const float heatSourceFalloffPower;
const float buoyancyInversionStrength;
const float buoyancyInversionClamp;
const bool restrictBlobMergingToCoil;
const float2 blobMergeCoilPos;
const float2 blobMergeCoilSize;
const int blobMergeCoilShape; // 0 = rectangular, 1 = elliptical
const float coilVelocityDamping;
const float coilAttractionStrength;
const int coilVelocityDampingPhase;

const float obstacleY;

const float2 ellipseBoundsCenter;
const bool useEllipticalBounds;

bool IsInvalidParticle(uint particleIndex)
{
	return particleIndex >= numParticles;
}

bool IsFluidParticle(uint particleIndex)
{
	return !IsInvalidParticle(particleIndex) && IsGhost[particleIndex] == 0;
}

bool IsFluidParticleRO(uint particleIndex)
{
	return !IsInvalidParticle(particleIndex) && IsGhostRO[particleIndex] == 0;
}

bool IsGhostParticle(uint particleIndex)
{
	return !IsInvalidParticle(particleIndex) && IsGhost[particleIndex] != 0;
}

bool IsPhaseValue(uint particlePhase, uint expectedPhase)
{
	return particlePhase == expectedPhase;
}

bool IsPhase(uint particleIndex, uint expectedPhase)
{
	return IsPhaseValue(Phases[particleIndex], expectedPhase);
}

bool IsPhaseRO(uint particleIndex, uint expectedPhase)
{
	return IsPhaseValue(PhasesRO[particleIndex], expectedPhase);
}

bool IsCarrierWedgePhase(uint phase)
{
	return carrierWedgePhase >= 0 && (int)phase == carrierWedgePhase;
}

bool IsAssignedBlob(uint blobId)
{
	return blobId < UnassignedBlobID;
}

bool IsUnassignedBlob(uint blobId)
{
	return blobId == UnassignedBlobID;
}

bool IsIgnoredBlob(uint blobId)
{
	return blobId == IgnoredPhaseBlobID;
}

bool IsSameBlob(uint blobA, uint blobB)
{
	return blobA == blobB;
}

#endif
