using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class Spawner2D : MonoBehaviour
{
	public float spawnDensity;

	public Vector2 initialVelocity;
	public float jitterStr;
	public SpawnRegion[] spawnRegions;
	public bool showSpawnBoundsGizmos;

	[Header("Debug Info")]
	public int spawnParticleCount;

	public ParticleSpawnData GetSpawnData(float? spawnDensityOverride = null)
	{
		var rng = new Unity.Mathematics.Random(42);
		float resolvedSpawnDensity = Mathf.Max(0f, spawnDensityOverride ?? spawnDensity);

		List<float2> allPoints = new();
		List<float2> allVelocities = new();
		List<int> allIndices = new();
		List<int> allPhases = new();

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

	public int GetGhostParticleCount(Vector2 boundsSize, Vector2 ellipseBoundsSize, bool useEllipticalBounds, float spacing)
	{
		if (useEllipticalBounds)
		{
			return EstimateEllipsePerimeterParticles(ellipseBoundsSize, spacing);
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

	public void GenerateGhostParticles(Vector2 boundsSize, Vector2 ellipseBoundsCenter, Vector2 ellipseBoundsSize, bool useEllipticalBounds, float spacing, int numLayers, int boundsGhostPhase, int obstacleGhostPhase, List<float2> outPositions, List<float2> outVelocities, List<int> outPhases, float obstacleY = float.NegativeInfinity)
	{
		outPositions.Clear();
		outVelocities.Clear();
		outPhases.Clear();

		if (useEllipticalBounds)
		{
			GenerateEllipseGhostParticles(ellipseBoundsCenter, ellipseBoundsSize, spacing, numLayers, boundsGhostPhase, obstacleGhostPhase, outPositions, outVelocities, outPhases, obstacleY);
		}
		else
		{
			GenerateRectangleGhostParticles(boundsSize, spacing, numLayers, boundsGhostPhase, outPositions, outVelocities, outPhases);
		}
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
			for (float x = -halfX; x <= halfX; x += spacing)
			{
				outPositions.Add(new float2(x, halfY + layerDist));
				outPositions.Add(new float2(x, -halfY - layerDist));
				outVelocities.Add(float2.zero);
				outVelocities.Add(float2.zero);
				outPhases.Add(ghostPhase);
				outPhases.Add(ghostPhase);
			}

			// Left and right edges (excluding corners to avoid duplication)
			for (float y = -halfY + spacing; y < halfY; y += spacing)
			{
				outPositions.Add(new float2(-halfX - layerDist, y));
				outPositions.Add(new float2(halfX + layerDist, y));
				outVelocities.Add(float2.zero);
				outVelocities.Add(float2.zero);
				outPhases.Add(ghostPhase);
				outPhases.Add(ghostPhase);
			}
		}
	}

void GenerateEllipseGhostParticles(Vector2 center, Vector2 radii, float spacing, int numLayers, int boundsGhostPhase, int obstacleGhostPhase,
    List<float2> outPositions, List<float2> outVelocities, List<int> outPhases, float obstacleY)
{
    float a = radii.x;
    float b = radii.y;
    float ghostLayerThickness = spacing * numLayers;
    int lutSize = 1000;
    float[] lutT = new float[lutSize + 1];
    float[] lutArc = new float[lutSize + 1];

    for (int layer = 1; layer <= numLayers; layer++)
    {
        float layerDist = layer * spacing;

        lutT[0] = 0f;
        lutArc[0] = 0f;
        float2 previousPoint = GetOffsetEllipsePoint(0f, layerDist);

        for (int lutIndex = 1; lutIndex <= lutSize; lutIndex++)
        {
            float t = lutIndex / (float)lutSize * Mathf.PI * 2;
            float2 point = GetOffsetEllipsePoint(t, layerDist);
            lutT[lutIndex] = t;
            lutArc[lutIndex] = lutArc[lutIndex - 1] + math.length(point - previousPoint);
            previousPoint = point;
        }

        float totalArc = lutArc[lutSize];
        int numPoints = Mathf.Max(3, Mathf.RoundToInt(totalArc / spacing));

        for (int i = 0; i < numPoints; i++)
        {
            float targetArc = (i + 0.5f) * (totalArc / numPoints);

            int lo = 0, hi = lutSize;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lutArc[mid] < targetArc) lo = mid; else hi = mid;
            }
            float arcFrac = (targetArc - lutArc[lo]) / Mathf.Max(lutArc[hi] - lutArc[lo], 1e-8f);
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
            for (float x = startX; x <= endX; x += spacing)
            {
                float2 rel = new float2(x - center.x, y - center.y);
                float normalized = (rel.x * rel.x) / (a * a) + (rel.y * rel.y) / (b * b);
                int phase = normalized < 1f && layer < 3 ? obstacleGhostPhase : 1 - obstacleGhostPhase;
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
		spawnParticleCount = 0;
		foreach (SpawnRegion region in spawnRegions)
		{
			Vector2Int spawnCountPerAxis = CalculateSpawnCountPerAxisBox2D(region.size, spawnDensity);
			spawnParticleCount += spawnCountPerAxis.x * spawnCountPerAxis.y;
		}
	}

	void OnDrawGizmos()
	{
		if (showSpawnBoundsGizmos && !Application.isPlaying)
		{
			foreach (SpawnRegion region in spawnRegions)
			{
				Gizmos.color = region.debugCol;
				Gizmos.DrawWireCube(region.position, region.size);
			}
		}
	}
}
