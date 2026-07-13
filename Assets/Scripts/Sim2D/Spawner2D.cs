using System.Collections.Generic;
using Seb.Fluid2D.Simulation;
using UnityEngine;
using Unity.Mathematics;

public class Spawner2D : MonoBehaviour
{
	public enum SpawnMode
	{
		ManualRegions,
		FillSimulationBounds
	}

	public SpawnMode spawnMode = SpawnMode.ManualRegions;
	public float spawnDensity;
	public float ghostDensity = 300;
	public Vector2 initialVelocity;
	public float jitterStr;
	public SpawnRegion[] spawnRegions;

	[Header("Bounds Fill")]
	[Range(0f, 1f)] public float lowerPhaseAreaRatio = 0.2f;
	public FluidSim2D.LiquidPhase lowerPhase = FluidSim2D.LiquidPhase.Wax;
	public FluidSim2D.LiquidPhase upperPhase = FluidSim2D.LiquidPhase.Water;
	[Tooltip("In bounds-fill mode, spawn extra particles deeper in the gravity field to approximate the pressure-compressed settled density gradient.")]
	public bool useHydrostaticSpawnDensity = true;
	[Tooltip("Multiplier for the estimated hydrostatic density gradient used only for spawning.")]
	[Min(0f)] public float hydrostaticSpawnGradientStrength = 1f;
	[Tooltip("Upper clamp for hydrostatic spawn density. 1 disables the gradient even if enabled.")]
	[Min(1f)] public float maxHydrostaticSpawnDensityMultiplier = 2f;

	[Header("Ghost Particles")]
	[Tooltip("Liquid phase assigned to outer boundary ghost particles. Clamped to valid phase range at runtime.")]
	public FluidSim2D.LiquidPhase ghostPhase = FluidSim2D.LiquidPhase.Water;
	[Tooltip("Liquid phase assigned to rectangular obstacle ghost particles. Use Wax to make wax wet/merge with the obstacle/coil area.")]
	public FluidSim2D.LiquidPhase obstacleGhostPhase = FluidSim2D.LiquidPhase.Wax;
	[Tooltip("Width of the centered lower-boundary section that receives the obstacle ghost phase. Set to 0 to use the merge coil or heat source width.")]
	[Min(0f)] public float lowerGhostPhaseWidth = 0f;

	[Header("Debug Info")]
	public FluidSim2D sim;
	public int spawnParticleCount;
	public int lowerPhaseParticleCount;
	public int upperPhaseParticleCount;
	public float filledBoundsArea;
	public float filledBoundsDensity;

	public ParticleSpawnData GetSpawnData(float? spawnDensityOverride = null)
	{
		var rng = new Unity.Mathematics.Random(42);
		float resolvedSpawnDensity = Mathf.Max(0f, spawnDensityOverride ?? spawnDensity);
		EnsureSimulationReference();

		if (spawnMode == SpawnMode.FillSimulationBounds && sim != null)
		{
			return GetBoundsFillSpawnData(resolvedSpawnDensity, ref rng);
		}

		List<float2> allPoints = new();
		List<float2> allVelocities = new();
		List<int> allIndices = new();
		List<int> allPhases = new();
		if (spawnRegions == null)
		{
			return new ParticleSpawnData(0);
		}

		for (int regionIndex = 0; regionIndex < spawnRegions.Length; regionIndex++)
		{
			SpawnRegion region = spawnRegions[regionIndex];
			float2[] points = SpawnInRegion(region, resolvedSpawnDensity);

			for (int i = 0; i < points.Length; i++)
			{
				float angle = (float)rng.NextDouble() * 3.14f * 2;
				float2 dir = new float2(Mathf.Cos(angle), Mathf.Sin(angle));
				float2 jitter = dir * jitterStr * ((float)rng.NextDouble() - 0.5f);
				allPoints.Add(points[i] + jitter);
				allVelocities.Add(initialVelocity);
				allIndices.Add(regionIndex);
				allPhases.Add(region.phaseID);
			}
		}

		ParticleSpawnData data = new()
		{
			positions = allPoints.ToArray(),
			velocities = allVelocities.ToArray(),
			spawnIndices = allIndices.ToArray(),
			phases = allPhases.ToArray(),
		};

		return data;
	}

	ParticleSpawnData GetBoundsFillSpawnData(float resolvedSpawnDensity, ref Unity.Mathematics.Random rng)
	{
		float ratio = Mathf.Clamp01(lowerPhaseAreaRatio);
		int lowerPhaseIndex = ClampPhaseIndex((int)lowerPhase);
		int upperPhaseIndex = ClampPhaseIndex((int)upperPhase);
		float lowerTargetDensity = GetPhaseTargetDensity(lowerPhaseIndex);
		float upperTargetDensity = GetPhaseTargetDensity(upperPhaseIndex);
		float referenceTargetDensity = GetSpawnDensityReferenceTarget(lowerTargetDensity, upperTargetDensity);
		float lowerSpawnDensity = resolvedSpawnDensity * lowerTargetDensity / referenceTargetDensity;
		float upperSpawnDensity = resolvedSpawnDensity * upperTargetDensity / referenceTargetDensity;
		float splitY = CalculatePhaseSplitY(sim, ratio);

		List<float2> lowerPoints = GenerateBoundsFillPoints(float.NegativeInfinity, splitY, lowerSpawnDensity);
		List<float2> upperPoints = GenerateBoundsFillPoints(splitY, float.PositiveInfinity, upperSpawnDensity);
		List<float2> allPoints = new(lowerPoints.Count + upperPoints.Count);
		List<float2> allVelocities = new(lowerPoints.Count + upperPoints.Count);
		List<int> allIndices = new(lowerPoints.Count + upperPoints.Count);
		List<int> allPhases = new(lowerPoints.Count + upperPoints.Count);

		AddBoundsFillPoints(lowerPoints, lowerPhaseIndex, 0, ref rng, allPoints, allVelocities, allIndices, allPhases);
		AddBoundsFillPoints(upperPoints, upperPhaseIndex, 1, ref rng, allPoints, allVelocities, allIndices, allPhases);
		lowerPhaseParticleCount = lowerPoints.Count;
		upperPhaseParticleCount = upperPoints.Count;
		spawnParticleCount = allPoints.Count;
		filledBoundsArea = CalculateFluidBoundsArea(sim);
		filledBoundsDensity = filledBoundsArea > 0 ? spawnParticleCount / filledBoundsArea : 0f;

		return new ParticleSpawnData
		{
			positions = allPoints.ToArray(),
			velocities = allVelocities.ToArray(),
			spawnIndices = allIndices.ToArray(),
			phases = allPhases.ToArray(),
		};
	}

	void AddBoundsFillPoints(List<float2> sourcePoints, int phaseIndex, int spawnIndex, ref Unity.Mathematics.Random rng, List<float2> allPoints, List<float2> allVelocities, List<int> allIndices, List<int> allPhases)
	{
		for (int i = 0; i < sourcePoints.Count; i++)
		{
			float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
			float2 dir = new float2(Mathf.Cos(angle), Mathf.Sin(angle));
			float2 jitter = dir * jitterStr * ((float)rng.NextDouble() - 0.5f);
			allPoints.Add(sourcePoints[i] + jitter);
			allVelocities.Add(initialVelocity);
			allIndices.Add(spawnIndex);
			allPhases.Add(phaseIndex);
		}
	}

	public int GetGhostParticleCount(Vector2 boundsSize, Vector2 ellipseBoundsSize, bool useEllipticalBounds, float spacing)
	{
		if (useEllipticalBounds)
		{
			return EstimateEllipsePerimeterParticles(ellipseBoundsSize * 0.5f, spacing);
		}
		else
		{
			return EstimateRectanglePerimeterParticles(boundsSize, spacing);
		}
	}

	int EstimateRectanglePerimeterParticles(Vector2 boundsSize, float spacing)
	{
		float perimeter = 2 * (boundsSize.x + boundsSize.y);
		return Mathf.CeilToInt(perimeter / spacing);
	}

	int EstimateEllipsePerimeterParticles(Vector2 ellipseBoundsSize, float spacing)
	{
		float a = ellipseBoundsSize.x;
		float b = ellipseBoundsSize.y;
		float h = (a - b) * (a - b) / ((a + b) * (a + b));
		float perimeter = Mathf.PI * (a + b) * (1 + 3 * h / (10 + Mathf.Sqrt(4 - 3 * h)));
		return Mathf.CeilToInt(perimeter / spacing);
	}

	public GhostParticleData GetGhostParticleData()
	{
		EnsureSimulationReference();
		List<float2> positions = new();
		List<float2> velocities = new();
		List<int> phases = new();

		if (sim == null || sim.analyticBoundary == null)
		{
			return new GhostParticleData(positions.ToArray(), velocities.ToArray(), phases.ToArray());
		}

		int boundsGhostPhase = ClampPhaseIndex((int)ghostPhase);
		int lowerGhostPhase = ClampPhaseIndex((int)obstacleGhostPhase);
		GenerateGhostParticles(sim.analyticBoundary, boundsGhostPhase, lowerGhostPhase, positions, velocities, phases, ResolveLowerGhostPhaseWidth());
		return new GhostParticleData(positions.ToArray(), velocities.ToArray(), phases.ToArray());
	}

	void GenerateGhostParticles(ParticleFluidAnalyticBoundary2D analyticBoundary, int boundsGhostPhase, int lowerGhostPhase, List<float2> outPositions, List<float2> outVelocities, List<int> outPhases, float lowerGhostPhaseWidth = 0f)
	{
		float spacing = Mathf.Sqrt(1f / (ghostDensity * sim.particleResolutionFactor));
		int numLayers = Mathf.CeilToInt(sim.EffectiveSmoothingRadius / spacing);

		outPositions.Clear();
		outVelocities.Clear();
		outPhases.Clear();

		if (analyticBoundary.useEllipticalBounds)
		{
			GenerateEllipseGhostParticles(analyticBoundary.BoundsCenter, analyticBoundary.EllipseRadii, spacing, numLayers, boundsGhostPhase, lowerGhostPhase, outPositions, outVelocities, outPhases, analyticBoundary.obstacleY, lowerGhostPhaseWidth);
		}
		else
		{
			GenerateRectangleGhostParticles(analyticBoundary.boundsSize, spacing, numLayers, boundsGhostPhase, outPositions, outVelocities, outPhases);
		}
	}

	float ResolveLowerGhostPhaseWidth()
	{
		if (lowerGhostPhaseWidth > 0f)
		{
			return lowerGhostPhaseWidth;
		}

		HeatSource2D source = sim.blobMergeCoil != null ? sim.blobMergeCoil : sim.heatSource;
		if (source != null && source.isActiveAndEnabled)
		{
			return Mathf.Max(0f, source.Size.x);
		}

		return 0f;
	}

	void GenerateRectangleGhostParticles(Vector2 boundsSize, float spacing, int numLayers, int ghostPhase, List<float2> outPositions, List<float2> outVelocities, List<int> outPhases)
	{
		float halfX = boundsSize.x * 0.5f;
		float halfY = boundsSize.y * 0.5f;

		// Generate ghost particles for each layer
		for (int layer = 1; layer <= numLayers; layer++)
		{
			float layerDist = layer * spacing;

			// Top and bottom edges
			AddHorizontalGhostLine(-halfX, halfX, halfY + layerDist, spacing, ghostPhase, outPositions, outVelocities, outPhases);
			AddHorizontalGhostLine(-halfX, halfX, -halfY - layerDist, spacing, ghostPhase, outPositions, outVelocities, outPhases);

			// Left and right edges (excluding corners to avoid duplication)
			for (float y = -halfY + spacing; y < halfY; y += spacing)
			{
				int samplesAtY = Mathf.Max(1, Mathf.RoundToInt(GetHydrostaticSpawnDensityMultiplier(y)));
				for (int i = 0; i < samplesAtY; i++)
				{
					float yOffset = ((i + 0.5f) / samplesAtY - 0.5f) * spacing;
					outPositions.Add(new float2(-halfX - layerDist, y + yOffset));
					outPositions.Add(new float2(halfX + layerDist, y + yOffset));
					outVelocities.Add(float2.zero);
					outVelocities.Add(float2.zero);
					outPhases.Add(ghostPhase);
					outPhases.Add(ghostPhase);
				}
			}
		}
	}

	void AddHorizontalGhostLine(float startX, float endX, float y, float spacing, int phase, List<float2> outPositions, List<float2> outVelocities, List<int> outPhases)
	{
		float width = endX - startX;
		if (width <= 0f || spacing <= 0f)
		{
			return;
		}

		float rowDensity = ghostDensity * sim.particleResolutionFactor * GetHydrostaticSpawnDensityMultiplier(y);
		int rowCount = Mathf.Max(1, Mathf.RoundToInt(width * rowDensity * spacing));
		for (int i = 0; i < rowCount; i++)
		{
			float t = (i + 0.5f) / rowCount;
			outPositions.Add(new float2(Mathf.Lerp(startX, endX, t), y));
			outVelocities.Add(float2.zero);
			outPhases.Add(phase);
		}
	}

	void GenerateEllipseGhostParticles(Vector2 center, Vector2 radii, float spacing, int numLayers, int boundsGhostPhase, int obstacleGhostPhase,
    List<float2> outPositions, List<float2> outVelocities, List<int> outPhases, float obstacleY, float lowerGhostPhaseWidth)
{
    float a = radii.x;
    float b = radii.y;
    float ghostLayerThickness = spacing * numLayers;
    int lutSize = 1000;
    float[] lutT = new float[lutSize + 1];
    float[] lutArc = new float[lutSize + 1];
    float[] lutWeightedArc = new float[lutSize + 1];

    for (int layer = 1; layer <= numLayers; layer++)
    {
        float layerDist = layer * spacing;

        lutT[0] = 0f;
        lutArc[0] = 0f;
        lutWeightedArc[0] = 0f;
        float2 previousPoint = GetOffsetEllipsePoint(0f, layerDist);
        float previousMultiplier = GetHydrostaticSpawnDensityMultiplier(center.y + previousPoint.y);

        for (int lutIndex = 1; lutIndex <= lutSize; lutIndex++)
        {
            float t = lutIndex / (float)lutSize * Mathf.PI * 2;
            float2 point = GetOffsetEllipsePoint(t, layerDist);
            float segmentLength = math.length(point - previousPoint);
            float multiplier = GetHydrostaticSpawnDensityMultiplier(center.y + point.y);
            lutT[lutIndex] = t;
            lutArc[lutIndex] = lutArc[lutIndex - 1] + segmentLength;
            lutWeightedArc[lutIndex] = lutWeightedArc[lutIndex - 1] + segmentLength * (previousMultiplier + multiplier) * 0.5f;
            previousPoint = point;
            previousMultiplier = multiplier;
        }

        float totalWeightedArc = lutWeightedArc[lutSize];
        int numPoints = Mathf.Max(3, Mathf.RoundToInt(totalWeightedArc / spacing));

        for (int i = 0; i < numPoints; i++)
        {
            float targetArc = (i + 0.5f) * (totalWeightedArc / numPoints);

            int lo = 0, hi = lutSize;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lutWeightedArc[mid] < targetArc) lo = mid; else hi = mid;
            }
            float arcFrac = (targetArc - lutWeightedArc[lo]) / Mathf.Max(lutWeightedArc[hi] - lutWeightedArc[lo], 1e-8f);
            float t = Mathf.Lerp(lutT[lo], lutT[hi], arcFrac);
            float2 ghostPos = (float2)center + GetOffsetEllipsePoint(t, layerDist);

            if (ghostPos.y < obstacleY)
            {
                continue;
            }

            outPositions.Add(ghostPos);
            outVelocities.Add(float2.zero);
            outPhases.Add(boundsGhostPhase);
        }
    }

    float2 GetOffsetEllipsePoint(float t, float offset)
    {
        float cosT = Mathf.Cos(t);
        float sinT = Mathf.Sin(t);
        float2 ellipsePoint = new float2(a * cosT, b * sinT);
        float2 normal = math.normalize(new float2(b * cosT, a * sinT));
        return ellipsePoint + normal * offset;
    }

    // Also spawn ghosts below the horizontal obstacle line.
    if (!float.IsNegativeInfinity(obstacleY))
    {
        float expandedA = a + ghostLayerThickness;
        float expandedB = b + ghostLayerThickness;

        for (int layer = 1; layer <= numLayers; layer++)
        {
            float layerDist = layer * spacing;
            float y = obstacleY - layerDist;
            float normalizedY = (y - center.y) / expandedB;
            if (Mathf.Abs(normalizedY) >= 1f)
            {
                continue;
            }

            float halfWidth = expandedA * Mathf.Sqrt(1f - normalizedY * normalizedY);
            float startX = center.x - halfWidth;
            float endX = center.x + halfWidth;
            float width = endX - startX;
            float rowDensity = ghostDensity * sim.particleResolutionFactor * GetHydrostaticSpawnDensityMultiplier(y);
            int rowCount = Mathf.Max(1, Mathf.RoundToInt(width * rowDensity * spacing));
            for (int i = 0; i < rowCount; i++)
            {
                float t = (i + 0.5f) / rowCount;
                float x = Mathf.Lerp(startX, endX, t);
                float2 rel = new float2(x - center.x, y - center.y);
                float normalized = (rel.x * rel.x) / (a * a) + (rel.y * rel.y) / (b * b);
                bool inLowerPhaseRegion = lowerGhostPhaseWidth <= 0f || Mathf.Abs(x - center.x) <= lowerGhostPhaseWidth * 0.5f;
                int phase = normalized < 1f && inLowerPhaseRegion ? obstacleGhostPhase : boundsGhostPhase;
                outPositions.Add(new float2(x, y));
                outVelocities.Add(float2.zero);
                outPhases.Add(phase);
            }
        }
    }
}

	float2[] SpawnInRegion(SpawnRegion region, float resolvedSpawnDensity)
	{
		Vector2 centre = region.position;
		Vector2 size = region.size;
		int i = 0;
		Vector2Int numPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, resolvedSpawnDensity);
		float2[] points = new float2[numPerAxis.x * numPerAxis.y];

		for (int y = 0; y < numPerAxis.y; y++)
		{
			for (int x = 0; x < numPerAxis.x; x++)
			{
				float tx = x / (numPerAxis.x - 1f);
				float ty = y / (numPerAxis.y - 1f);

				float px = (tx - 0.5f) * size.x + centre.x;
				float py = (ty - 0.5f) * size.y + centre.y;
				points[i] = new float2(px, py);
				i++;
			}
		}

		return points;
	}

	static Vector2Int CalculateSpawnCountPerAxisBox2D(Vector2 size, float spawnDensity)
	{
		float area = size.x * size.y;
		int targetTotal = Mathf.CeilToInt(area * spawnDensity);

		float lenSum = size.x + size.y;
		Vector2 t = size / lenSum;
		float m = Mathf.Sqrt(targetTotal / (t.x * t.y));
		int nx = Mathf.CeilToInt(t.x * m);
		int ny = Mathf.CeilToInt(t.y * m);

		return new Vector2Int(nx, ny);
	}

	List<float2> GenerateBoundsFillPoints(float minY, float maxY, float phaseSpawnDensity)
	{
		if (phaseSpawnDensity <= 0f)
		{
			return new List<float2>();
		}

		float spacing = Mathf.Sqrt(1f / phaseSpawnDensity);
		List<float2> points = new();
		FillBoundsRows(minY, maxY, spacing, phaseSpawnDensity, points);
		return points;
	}

	void FillBoundsRows(float minY, float maxY, float spacing, float phaseSpawnDensity, List<float2> points)
	{
		GetSimulationYRange(sim, out float domainMinY, out float domainMaxY);
		float yStart = Mathf.Max(domainMinY, minY);
		float yEnd = Mathf.Min(domainMaxY, maxY);
		if (yEnd <= yStart || spacing <= 0f)
		{
			return;
		}

		for (float y = yStart + spacing * 0.5f; y < yEnd; y += spacing)
		{
			if (!TryGetSimulationXRangeAtY(sim, y, out float minX, out float maxX))
			{
				continue;
			}

			float rowWidth = maxX - minX;
			float rowDensity = phaseSpawnDensity * GetHydrostaticSpawnDensityMultiplier(y);
			int rowCount = Mathf.Max(0, Mathf.RoundToInt(rowWidth * rowDensity * spacing));
			for (int i = 0; i < rowCount; i++)
			{
				float t = (i + 0.5f) / rowCount;
				points.Add(new float2(Mathf.Lerp(minX, maxX, t), y));
			}
		}
	}

	int EstimateBoundsFillParticleCount(float minY, float maxY, float phaseSpawnDensity)
	{
		if (phaseSpawnDensity <= 0f)
		{
			return 0;
		}

		float spacing = Mathf.Sqrt(1f / phaseSpawnDensity);
		GetSimulationYRange(sim, out float domainMinY, out float domainMaxY);
		float yStart = Mathf.Max(domainMinY, minY);
		float yEnd = Mathf.Min(domainMaxY, maxY);
		if (yEnd <= yStart || spacing <= 0f)
		{
			return 0;
		}

		int count = 0;
		for (float y = yStart + spacing * 0.5f; y < yEnd; y += spacing)
		{
			if (!TryGetSimulationXRangeAtY(sim, y, out float minX, out float maxX))
			{
				continue;
			}

			float rowWidth = maxX - minX;
			float rowDensity = phaseSpawnDensity * GetHydrostaticSpawnDensityMultiplier(y);
			count += Mathf.Max(0, Mathf.RoundToInt(rowWidth * rowDensity * spacing));
		}
		return count;
	}

	static void GetSimulationYRange(FluidSim2D sim, out float minY, out float maxY)
	{
		if (sim.analyticBoundary.useEllipticalBounds)
		{
			Vector2 radii = sim.analyticBoundary.EllipseRadii;
			minY = Mathf.Max(sim.analyticBoundary.BoundsCenter.y - radii.y, sim.analyticBoundary.obstacleY);
			maxY = sim.analyticBoundary.BoundsCenter.y + radii.y;
			return;
		}

		minY = -sim.analyticBoundary.boundsSize.y * 0.5f;
		maxY = sim.analyticBoundary.boundsSize.y * 0.5f;
	}

	static bool TryGetSimulationXRangeAtY(FluidSim2D sim, float y, out float minX, out float maxX)
	{
		if (sim.analyticBoundary.useEllipticalBounds)
		{
			Vector2 radii = sim.analyticBoundary.EllipseRadii;
			float ry = Mathf.Max(radii.y, 0.0001f);
			float normalizedY = (y - sim.analyticBoundary.BoundsCenter.y) / ry;
			if (Mathf.Abs(normalizedY) >= 1f || y < sim.analyticBoundary.obstacleY)
			{
				minX = 0f;
				maxX = 0f;
				return false;
			}

			float halfWidth = radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - normalizedY * normalizedY));
			minX = sim.analyticBoundary.BoundsCenter.x - halfWidth;
			maxX = sim.analyticBoundary.BoundsCenter.x + halfWidth;
			return maxX > minX;
		}

		minX = -sim.analyticBoundary.boundsSize.x * 0.5f;
		maxX = sim.analyticBoundary.boundsSize.x * 0.5f;
		return maxX > minX;
	}

	float GetHydrostaticSpawnDensityMultiplier(float y)
	{
		if (!useHydrostaticSpawnDensity || sim == null || Mathf.Abs(sim.gravity) <= 0f || sim.pressureMultiplier <= 0f)
		{
			return 1f;
		}

		GetSimulationYRange(sim, out float domainMinY, out float domainMaxY);
		float freeSurfaceY = sim.gravity < 0f ? domainMaxY : domainMinY;
		float depth = Mathf.Abs(y - freeSurfaceY);
		float densityMultiplier = 1f + hydrostaticSpawnGradientStrength * Mathf.Abs(sim.gravity) * depth / sim.pressureMultiplier;
		return Mathf.Clamp(densityMultiplier, 1f, Mathf.Max(1f, maxHydrostaticSpawnDensityMultiplier));
	}

	public struct ParticleSpawnData
	{
		public float2[] positions;
		public float2[] velocities;
		public int[] spawnIndices;
		public int[] phases;

		public ParticleSpawnData(int num)
		{
			positions = new float2[num];
			velocities = new float2[num];
			spawnIndices = new int[num];
			phases = new int[num];
		}
	}

	public readonly struct GhostParticleData
	{
		public readonly float2[] positions;
		public readonly float2[] velocities;
		public readonly int[] phases;
		public int Count => positions?.Length ?? 0;

		public GhostParticleData(float2[] positions, float2[] velocities, int[] phases)
		{
			this.positions = positions;
			this.velocities = velocities;
			this.phases = phases;
		}
	}

	public readonly struct InitialParticleData
	{
		public readonly float2[] positions;
		public readonly float2[] velocities;
		public readonly int[] phases;
		public readonly uint[] ghostFlags;
		public readonly float[] temperatures;
		public readonly float[] targetDensities;

		public InitialParticleData(float2[] positions, float2[] velocities, int[] phases, uint[] ghostFlags, float[] temperatures, float[] targetDensities)
		{
			this.positions = positions;
			this.velocities = velocities;
			this.phases = phases;
			this.ghostFlags = ghostFlags;
			this.temperatures = temperatures;
			this.targetDensities = targetDensities;
		}
	}

	public static InitialParticleData CreateInitialParticleData(ParticleSpawnData spawnData, GhostParticleData ghostData, float[] phaseTargetDensities, float ambientTemperature)
	{
		int numFluidParticles = spawnData.positions.Length;
		int numGhostParticles = ghostData.Count;
		int numParticles = numFluidParticles + numGhostParticles;
		float2[] positions = new float2[numParticles];
		float2[] velocities = new float2[numParticles];
		int[] phases = new int[numParticles];
		uint[] ghostFlags = new uint[numParticles];
		float[] temperatures = new float[numParticles];
		float[] targetDensities = new float[numParticles];

		System.Array.Copy(spawnData.positions, 0, positions, 0, numFluidParticles);
		System.Array.Copy(spawnData.velocities, 0, velocities, 0, numFluidParticles);
		for (int i = 0; i < numFluidParticles; i++)
		{
			phases[i] = spawnData.phases[i];
			ghostFlags[i] = 0;
			temperatures[i] = ambientTemperature;
			targetDensities[i] = ResolvePhaseTargetDensity(phaseTargetDensities, spawnData.phases[i]);
		}

		for (int i = 0; i < numGhostParticles; i++)
		{
			int particleIndex = numFluidParticles + i;
			positions[particleIndex] = ghostData.positions[i];
			velocities[particleIndex] = ghostData.velocities[i];
			phases[particleIndex] = ghostData.phases[i];
			ghostFlags[particleIndex] = 1;
			temperatures[particleIndex] = ambientTemperature;
			targetDensities[particleIndex] = ResolvePhaseTargetDensity(phaseTargetDensities, ghostData.phases[i]);
		}

		return new InitialParticleData(positions, velocities, phases, ghostFlags, temperatures, targetDensities);
	}

	static float ResolvePhaseTargetDensity(float[] phaseTargetDensities, int phaseIndex)
	{
		if (phaseTargetDensities == null || phaseTargetDensities.Length == 0)
		{
			return 1f;
		}

		return phaseTargetDensities[Mathf.Clamp(phaseIndex, 0, phaseTargetDensities.Length - 1)];
	}

	[System.Serializable]
	public struct SpawnRegion
	{
		public Vector2 position;
		public Vector2 size;
		public Color debugCol;
		public int phaseID;
	}

	void OnValidate()
	{
		EnsureSimulationReference();
		filledBoundsArea = sim != null ? CalculateFluidBoundsArea(sim) : 0f;
		UpdateSpawnDebugInfo();
	}

	void EnsureSimulationReference()
	{
		if (sim == null)
		{
			sim = GetComponent<FluidSim2D>();
		}
	}

	void UpdateSpawnDebugInfo()
	{
		spawnParticleCount = 0;
		lowerPhaseParticleCount = 0;
		upperPhaseParticleCount = 0;

		if (spawnMode == SpawnMode.FillSimulationBounds && sim != null)
		{
			float ratio = Mathf.Clamp01(lowerPhaseAreaRatio);
			int lowerPhaseIndex = ClampPhaseIndex((int)lowerPhase);
			int upperPhaseIndex = ClampPhaseIndex((int)upperPhase);
			float lowerTargetDensity = GetPhaseTargetDensity(lowerPhaseIndex);
			float upperTargetDensity = GetPhaseTargetDensity(upperPhaseIndex);
			float referenceTargetDensity = GetSpawnDensityReferenceTarget(lowerTargetDensity, upperTargetDensity);
			float lowerSpawnDensity = spawnDensity * lowerTargetDensity / referenceTargetDensity;
			float upperSpawnDensity = spawnDensity * upperTargetDensity / referenceTargetDensity;
			float splitY = CalculatePhaseSplitY(sim, ratio);
			lowerPhaseParticleCount = EstimateBoundsFillParticleCount(float.NegativeInfinity, splitY, lowerSpawnDensity);
			upperPhaseParticleCount = EstimateBoundsFillParticleCount(splitY, float.PositiveInfinity, upperSpawnDensity);
			spawnParticleCount = lowerPhaseParticleCount + upperPhaseParticleCount;
		}
		else if (spawnRegions != null)
		{
			foreach (SpawnRegion region in spawnRegions)
			{
				Vector2Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, spawnDensity);
				spawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y;
			}
		}

		filledBoundsDensity = filledBoundsArea > 0 ? spawnParticleCount / filledBoundsArea : 0f;
	}

	static float CalculateFluidBoundsArea(FluidSim2D sim)
	{
		if (sim.analyticBoundary.useEllipticalBounds)
		{
			return CalculateEllipseAreaAboveY(sim.analyticBoundary.BoundsCenter, sim.analyticBoundary.EllipseRadii, sim.analyticBoundary.obstacleY);
		}

		return Mathf.Max(0f, sim.analyticBoundary.boundsSize.x) * Mathf.Max(0f, sim.analyticBoundary.boundsSize.y);
	}

	static float CalculateFluidBoundsAreaInYRange(FluidSim2D sim, float minY, float maxY)
	{
		if (sim.analyticBoundary.useEllipticalBounds)
		{
			Vector2 radii = sim.analyticBoundary.EllipseRadii;
			float domainMinY = Mathf.Max(sim.analyticBoundary.BoundsCenter.y - radii.y, sim.analyticBoundary.obstacleY);
			float domainMaxY = sim.analyticBoundary.BoundsCenter.y + radii.y;
			float clampedMinY = Mathf.Clamp(minY, domainMinY, domainMaxY);
			float clampedMaxY = Mathf.Clamp(maxY, domainMinY, domainMaxY);
			if (clampedMaxY <= clampedMinY)
			{
				return 0f;
			}

			return CalculateEllipseAreaAboveY(sim.analyticBoundary.BoundsCenter, radii, clampedMinY)
			       - CalculateEllipseAreaAboveY(sim.analyticBoundary.BoundsCenter, radii, clampedMaxY);
		}

		float rectMinY = -sim.analyticBoundary.boundsSize.y * 0.5f;
		float rectMaxY = sim.analyticBoundary.boundsSize.y * 0.5f;
		float y0 = Mathf.Clamp(minY, rectMinY, rectMaxY);
		float y1 = Mathf.Clamp(maxY, rectMinY, rectMaxY);
		return Mathf.Max(0f, y1 - y0) * Mathf.Max(0f, sim.analyticBoundary.boundsSize.x);
	}

	static float CalculatePhaseSplitY(FluidSim2D sim, float lowerAreaRatio)
	{
		lowerAreaRatio = Mathf.Clamp01(lowerAreaRatio);
		if (!sim.analyticBoundary.useEllipticalBounds)
		{
			float minY = -sim.analyticBoundary.boundsSize.y * 0.5f;
			float maxY = sim.analyticBoundary.boundsSize.y * 0.5f;
			return Mathf.Lerp(minY, maxY, lowerAreaRatio);
		}

		Vector2 ellipseRadii = sim.analyticBoundary.EllipseRadii;
		float bottomY = Mathf.Max(sim.analyticBoundary.BoundsCenter.y - ellipseRadii.y, sim.analyticBoundary.obstacleY);
		float topY = sim.analyticBoundary.BoundsCenter.y + ellipseRadii.y;
		float totalArea = CalculateEllipseAreaAboveY(sim.analyticBoundary.BoundsCenter, ellipseRadii, bottomY);
		float targetAreaAboveSplit = totalArea * (1f - lowerAreaRatio);
		float lo = bottomY;
		float hi = topY;
		for (int i = 0; i < 32; i++)
		{
			float mid = (lo + hi) * 0.5f;
			float areaAboveMid = CalculateEllipseAreaAboveY(sim.analyticBoundary.BoundsCenter, ellipseRadii, mid);
			if (areaAboveMid > targetAreaAboveSplit)
			{
				lo = mid;
			}
			else
			{
				hi = mid;
			}
		}
		return (lo + hi) * 0.5f;
	}

	static float CalculateEllipseAreaAboveY(Vector2 center, Vector2 radii, float y)
	{
		float rx = Mathf.Max(0f, radii.x);
		float ry = Mathf.Max(0f, radii.y);
		if (rx <= 0f || ry <= 0f)
		{
			return 0f;
		}

		float t = Mathf.Clamp((y - center.y) / ry, -1f, 1f);
		return rx * ry * (Mathf.Acos(t) - t * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)));
	}

	int ClampPhaseIndex(int phaseIndex)
	{
		if (sim == null || sim.phases == null || sim.phases.Length == 0)
		{
			return Mathf.Max(0, phaseIndex);
		}

		return Mathf.Clamp(phaseIndex, 0, sim.phases.Length - 1);
	}

	float GetPhaseTargetDensity(int phaseIndex)
	{
		if (sim == null || sim.phases == null || sim.phases.Length == 0)
		{
			return 1f;
		}

		return Mathf.Max(0.0001f, sim.phases[ClampPhaseIndex(phaseIndex)].targetDensity);
	}

	static float GetSpawnDensityReferenceTarget(float lowerTargetDensity, float upperTargetDensity)
	{
		return Mathf.Max(0.0001f, (lowerTargetDensity + upperTargetDensity) * 0.5f);
	}

	void OnDrawGizmos()
	{
		if (spawnMode == SpawnMode.ManualRegions && !Application.isPlaying)
		{
			if (spawnRegions == null)
			{
				return;
			}

			foreach (SpawnRegion region in spawnRegions)
			{
				Gizmos.color = region.debugCol;
				Gizmos.DrawWireCube(region.position, region.size);
			}
		}
	}
}
