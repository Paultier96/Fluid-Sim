#ifndef PARTICLE_FLUID_ANALYTIC_BOUNDARY_INCLUDED
#define PARTICLE_FLUID_ANALYTIC_BOUNDARY_INCLUDED

int useEllipticalBounds;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float obstacleY;
float analyticBoundaryExpansion;

float2 BoundaryDistances(float2 worldPos)
{
	float2 radii = max(abs(ellipseBoundsSize) * 0.5, 0.0001);
	float2 rel = worldPos - ellipseBoundsCenter;
	float2 q = rel / radii;
	float qLen = max(length(q), 0.0001);
	float ellipseGradientLength = length(float2(q.x / radii.x, q.y / radii.y)) / qLen;
	float ellipseDistance = (qLen - 1.0) / max(ellipseGradientLength, 0.0001);
	float cutDistance = obstacleY - worldPos.y;
	return float2(ellipseDistance, cutDistance);
}

float OffsetBoundaryDistance(float2 distances, float expansion)
{
	if (expansion <= 0.0001)
	{
		return max(distances.x, distances.y);
	}

	float outsideDistance = length(max(distances, 0.0)) + min(max(distances.x, distances.y), 0.0);
	return outsideDistance - expansion;
}

float AnalyticBoundaryDistance(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	return max(distances.x, distances.y);
}

float InnerAnalyticBoundaryDistance(float2 worldPos)
{
	return AnalyticBoundaryDistance(worldPos);
}

float OuterAnalyticBoundaryDistance(float2 worldPos)
{
	return OffsetBoundaryDistance(BoundaryDistances(worldPos), max(analyticBoundaryExpansion, 0.0));
}

float OuterAnalyticBoundaryAlphaFromUv(float2 uv, float2 worldCenter, float2 worldSize)
{
	float distance = OuterAnalyticBoundaryDistance(ParticleFluidWorldFromUv(uv, worldCenter, worldSize));
	float aa = max(fwidth(distance), 0.0001);
	return smoothstep(aa, -aa, distance);
}

float OuterAnalyticBoundaryAlphaFromDomainUv(float2 uv)
{
	return OuterAnalyticBoundaryAlphaFromUv(uv, domainWorldCenter, domainWorldSize);
}

float2 EllipseBoundaryNormal(float2 worldPos)
{
	float2 radii = max(abs(ellipseBoundsSize) * 0.5, 0.0001);
	float2 rel = worldPos - ellipseBoundsCenter;
	float2 q = rel / radii;
	return length(q) > 0.0001
		? normalize(float2(q.x / radii.x, q.y / radii.y))
		: float2(0.0, 1.0);
}

float2 AnalyticBoundaryNormal(float2 worldPos)
{
	float2 distances = BoundaryDistances(worldPos);
	float2 ellipseNormal = EllipseBoundaryNormal(worldPos);
	float2 cutNormal = float2(0.0, -1.0);
	return distances.x > distances.y ? ellipseNormal : cutNormal;
}

float2 OffsetBoundaryNormal(float2 worldPos, float expansion)
{
	if (expansion <= 0.0001)
	{
		return AnalyticBoundaryNormal(worldPos);
	}

	float2 distances = BoundaryDistances(worldPos);
	float2 outsideDistances = max(distances, 0.0);
	if (outsideDistances.x > 0.0 && outsideDistances.y > 0.0)
	{
		return normalize(EllipseBoundaryNormal(worldPos) * outsideDistances.x + float2(0.0, -1.0) * outsideDistances.y);
	}

	return AnalyticBoundaryNormal(worldPos);
}

float2 OuterAnalyticBoundaryNormal(float2 worldPos)
{
	return OffsetBoundaryNormal(worldPos, max(analyticBoundaryExpansion, 0.0));
}

#endif
