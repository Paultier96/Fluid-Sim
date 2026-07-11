#ifndef FLUID_SIM_2D_BOUNDARY_INCLUDED
#define FLUID_SIM_2D_BOUNDARY_INCLUDED

void GetEllipseSurfaceData(float2 pos, float2 center, float2 radii, out float2 closestPoint, out float2 outwardNormal, out float signedDistance)
{
	float2 localPos = pos - center;
	float2 p = abs(localPos);
	float2 ab = max(radii, 1e-6);
	float a2 = ab.x * ab.x;
	float b2 = ab.y * ab.y;
	float2 signLocal = float2((localPos.x < 0) ? -1.0 : 1.0, (localPos.y < 0) ? -1.0 : 1.0);

	// Handle center explicitly (direction undefined there).
	if (p.x < 1e-8 && p.y < 1e-8)
	{
		float2 closestLocalCenter = float2(0, ab.y);
		closestPoint = center + closestLocalCenter;
		outwardNormal = float2(0, 1);
		signedDistance = -ab.y;
		return;
	}
	
	float ellipseValue = (p.x * p.x) / a2 + (p.y * p.y) / b2;
	bool inside = (ellipseValue < 1.0);
	
	// Solve nearest-point parameter t using Newton's method:
	// f(t) = (a^2 px^2)/(t+a^2)^2 + (b^2 py^2)/(t+b^2)^2 - 1 = 0
	float minAxis2 = min(a2, b2);
	float t = inside ? (-0.5 * minAxis2) : 0.0;
	for (int i = 0; i < 8; i++)
	{
		float ta = max(t + a2, 1e-8);
		float tb = max(t + b2, 1e-8);
		float invTa2 = 1.0 / (ta * ta);
		float invTb2 = 1.0 / (tb * tb);
		float f = (a2 * p.x * p.x) * invTa2 + (b2 * p.y * p.y) * invTb2 - 1.0;
		float df = -2.0 * (a2 * p.x * p.x) / (ta * ta * ta) - 2.0 * (b2 * p.y * p.y) / (tb * tb * tb);
		float step = f / min(df, -1e-8);
		t -= step;
		if (inside)
		{
			t = clamp(t, -minAxis2 + 1e-6, -1e-7);
		}
		else
		{
			t = max(t, 0.0);
		}
	}
	
	float2 closestAbs = float2(
		(a2 * p.x) / max(t + a2, 1e-8),
		(b2 * p.y) / max(t + b2, 1e-8)
	);
	float2 closestLocal = closestAbs * signLocal;
	closestPoint = center + closestLocal;
	
	float2 normalVec = float2(
		closestLocal.x / a2,
		closestLocal.y / b2
	);
	float normalLen = length(normalVec);
	outwardNormal = (normalLen > 1e-6) ? (normalVec / normalLen) : normalize(localPos + 1e-6);
	
	float unsignedDist = length(localPos - closestLocal);
	signedDistance = inside ? -unsignedDist : unsignedDist;
}

float SignedDistanceToEllipse(float2 pos, float2 center, float2 radii)
{
	float2 closestPoint;
	float2 outwardNormal;
	float signedDistance;
	GetEllipseSurfaceData(pos, center, radii, closestPoint, outwardNormal, signedDistance);
	return signedDistance;
}

bool IsInsideBlobMergeCoil(float2 pos)
{
	float2 halfSize = blobMergeCoilSize * 0.5;
	if (halfSize.x <= 0 || halfSize.y <= 0) return false;

	float2 relPos = pos - blobMergeCoilPos;
	if (blobMergeCoilShape == 0)
	{
		return abs(relPos.x) < halfSize.x && abs(relPos.y) < halfSize.y;
	}

	float2 normalizedOffset = relPos / max(halfSize, float2(1e-6, 1e-6));
	return dot(normalizedOffset, normalizedOffset) < 1.0;
}

float SampleBlobMergeCoilWeight(float2 pos)
{
	float2 halfSize = blobMergeCoilSize * 0.5;
	if (halfSize.x <= 0 || halfSize.y <= 0) return 0.0;

	float2 relPos = pos - blobMergeCoilPos;
	float normalizedDistance = 0;
	if (blobMergeCoilShape == 0)
	{
		normalizedDistance = max(abs(relPos.x) / max(halfSize.x, 1e-6), abs(relPos.y) / max(halfSize.y, 1e-6));
	}
	else
	{
		float2 normalizedOffset = relPos / max(halfSize, float2(1e-6, 1e-6));
		normalizedDistance = length(normalizedOffset);
	}

	return 1.0 - smoothstep(0.0, 1.0, normalizedDistance);
}

float2 DirectionToBlobMergeCoilCenter(float2 pos)
{
	float2 toCenter = blobMergeCoilPos - pos;
	float dst = length(toCenter);
	return (dst > 1e-6) ? toCenter / dst : float2(0, 0);
}

bool TryGetAnalyticWallDistanceAndNormal(float2 pos, out float wallDist, out float2 inwardNormal)
{
	wallDist = 1e9;
	inwardNormal = 0;

	if (useEllipticalBounds)
	{
		float2 closestPoint;
		float2 outwardNormal;
		float signedDist;
		GetEllipseSurfaceData(pos, ellipseBoundsCenter, ellipseBoundsSize, closestPoint, outwardNormal, signedDist);
		if (signedDist < 0)
		{
			wallDist = -signedDist;
			inwardNormal = -outwardNormal;
		}
	}
	else
	{
		float2 halfSize = boundsSize * 0.5;
		float2 edgeDst = halfSize - abs(pos);
		if (edgeDst.x <= edgeDst.y)
		{
			wallDist = edgeDst.x;
			inwardNormal = float2(-sign(pos.x), 0);
		}
		else
		{
			wallDist = edgeDst.y;
			inwardNormal = float2(0, -sign(pos.y));
		}
	}

	return wallDist >= 0 && dot(inwardNormal, inwardNormal) > 0;
}

bool TryGetObstacleFloorDistanceAndNormal(float2 pos, out float wallDist, out float2 inwardNormal)
{
	wallDist = pos.y - obstacleY;
	inwardNormal = float2(0, 1);
	return wallDist >= 0;
}

bool TryGetNearestWallDistanceAndNormal(float2 pos, bool includeObstacleFloor, bool rejectBelowObstacleFloor, out float wallDist, out float2 inwardNormal)
{
	if (!TryGetAnalyticWallDistanceAndNormal(pos, wallDist, inwardNormal))
	{
		return false;
	}

	if (includeObstacleFloor && useEllipticalBounds)
	{
		float floorDist = pos.y - obstacleY;
		if (floorDist < wallDist)
		{
			if (floorDist <= 0)
			{
				return !rejectBelowObstacleFloor;
			}

			wallDist = floorDist;
			inwardNormal = float2(0, 1);
		}
	}

	return wallDist >= 0 && dot(inwardNormal, inwardNormal) > 0;
}

float GetNearestWallDistance(float2 pos, bool includeObstacleFloor)
{
	float wallDist;
	float2 inwardNormal;
	return TryGetNearestWallDistanceAndNormal(pos, includeObstacleFloor, false, wallDist, inwardNormal) ? wallDist : 1e9;
}

float2 CalculateBoundaryRepulsion(float2 pos)
{
	float2 boundaryRepel = float2(0, 0);
	if (edgeForce <= 0 || edgeForceDst <= 0)
	{
		return boundaryRepel;
	}

	if (useEllipticalBounds)
	{
		float2 closestPoint;
		float2 outwardNormal;
		float signedDist;
		GetEllipseSurfaceData(pos, ellipseBoundsCenter, ellipseBoundsSize, closestPoint, outwardNormal, signedDist);
		if (signedDist < 0)
		{
			float distToBoundary = -signedDist;
			if (distToBoundary < edgeForceDst)
			{
				float strength = edgeForce * (edgeForceDst - distToBoundary) / edgeForceDst;
				boundaryRepel += -outwardNormal * strength;
			}
		}

		float floorDist;
		float2 floorNormal;
		if (TryGetObstacleFloorDistanceAndNormal(pos, floorDist, floorNormal) && floorDist < edgeForceDst)
		{
			float strength = edgeForce * (edgeForceDst - floorDist) / edgeForceDst;
			boundaryRepel += floorNormal * strength;
		}
	}
	else
	{
		float2 halfSize = boundsSize * 0.5;
		float2 edgeDst = halfSize - abs(pos);
		if (edgeDst.x < edgeForceDst)
		{
			float strength = edgeForce * (edgeForceDst - edgeDst.x) / edgeForceDst;
			boundaryRepel.x += -sign(pos.x) * strength;
		}
		if (edgeDst.y < edgeForceDst)
		{
			float strength = edgeForce * (edgeForceDst - edgeDst.y) / edgeForceDst;
			boundaryRepel.y += -sign(pos.y) * strength;
		}
	}

	return boundaryRepel;
}

float2 CalculateWallFilmAcceleration(float2 pos, uint phase)
{
	if ((wallFilmStrength <= 0 && wallFilmAttractionStrength <= 0) || wallFilmDistance <= 0) return 0;

	float wallDist;
	float2 inwardNormal;
	if (!TryGetAnalyticWallDistanceAndNormal(pos, wallDist, inwardNormal)) return 0;
	if (wallDist >= wallFilmDistance) return 0;

	float filmT = 1.0 - saturate(wallDist / max(wallFilmDistance, 1e-6));
	float strength = 0;
	if (wallFilmPhase < 0 || wallFilmPhase == (int)phase)
	{
		strength += wallFilmStrength;
	}
	if (wallFilmAttractionPhase < 0 || wallFilmAttractionPhase == (int)phase)
	{
		strength -= wallFilmAttractionStrength;
	}
	if (abs(strength) <= 1e-6) return 0;

	float2 acceleration = inwardNormal * strength * filmT * filmT;
	if (wallFilmMaxAcceleration > 0)
	{
		float mag = length(acceleration);
		if (mag > wallFilmMaxAcceleration)
		{
			acceleration *= wallFilmMaxAcceleration / max(mag, 1e-6);
		}
	}
	return acceleration;
}

float2 CalculateWallPressureSupportForce(float2 pos, float density, float pressure, float nearPressure, uint particleIndex, float epsilon)
{
    if (wallPressureStrength <= 0) return 0;

    float supportRadius = (wallPressureRadius > 0) ? wallPressureRadius : smoothingRadius;
    if (supportRadius <= 0) return 0;

    float wallDist;
    float2 inwardNormal;
    if (!TryGetNearestWallDistanceAndNormal(pos, true, true, wallDist, inwardNormal)) return 0;
    if (wallDist <= 0) return 0;

    if (wallDist >= supportRadius || dot(inwardNormal, inwardNormal) <= 0) return 0;

    float targetDensity = max(ParticleTargetDensities[particleIndex], epsilon);
    float densityDeficit = max(targetDensity - density, 0);
    float boundaryPressure = (max(pressure, 0) + nearPressure + densityDeficit * pressureMultiplier) * wallPressureStrength;
    if (boundaryPressure <= 0) return 0;

	float boundaryKernel = 1.0 - saturate(wallDist / supportRadius);
    return inwardNormal * boundaryKernel * boundaryPressure / targetDensity;
}

void CollideWithObstacleFloor(inout float2 pos, inout float2 vel)
{
	if (pos.y < obstacleY)
	{
		pos.y = obstacleY;
		if (vel.y < 0)
		{
			vel.y *= -1 * collisionDamping;
		}
	}
}

void CollideWithEllipseBounds(inout float2 pos, inout float2 vel)
{
	float2 posRelative = pos - ellipseBoundsCenter;
	float2 radii = ellipseBoundsSize;
	float2 normalizedPos = posRelative / radii;
	float normalizedDist = length(normalizedPos);
	
	if (normalizedDist <= 1.0)
	{
		return;
	}

	float2 projectedNormalized = normalize(normalizedPos);
	float2 boundaryPos = projectedNormalized * radii;
	pos = ellipseBoundsCenter + boundaryPos;
	
	// For an ellipse, the outward normal is proportional to (x/a^2, y/b^2).
	float2 normalVector = boundaryPos / (radii * radii);
	float2 normal = normalize(normalVector);
	
	vel -= 2.0 * dot(vel, normal) * normal;
	vel *= collisionDamping;
}

void CollideWithRectBounds(inout float2 pos, inout float2 vel)
{
	const float2 halfSize = boundsSize * 0.5;
	float2 edgeDst = halfSize - abs(pos);

	if (edgeDst.x <= 0)
	{
		pos.x = halfSize.x * sign(pos.x);
		vel.x *= -1 * collisionDamping;
	}
	if (edgeDst.y <= 0)
	{
		pos.y = halfSize.y * sign(pos.y);
		vel.y *= -1 * collisionDamping;
	}
}

#endif
