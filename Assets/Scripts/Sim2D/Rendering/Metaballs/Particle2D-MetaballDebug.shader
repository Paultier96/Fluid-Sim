Shader "Hidden/Particle2DMetaballDebug" {
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		#include "../Lighting/Shared/ParticleFluidCommon.hlsl"

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D CombinedTex;
sampler2D MaterialNormalTex;
sampler2D MaterialTransportTex;
sampler2D VelocityTex;
sampler2D GradientAtlas;
sampler2D DebugTex0;
int debugMode;
int debugShowClipping;
float debugGradientMax;
float motionDebugDeltaTime;
float particleCausticDebugExposure;
float hashGridCellSize;
float2 hashGridWorldCenter;
float2 hashGridWorldSize;
int showHashGridOverlay;
int hashGridOverlayOnly;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = TransformObjectToHClip(v.vertex.xyz);
	o.uv = v.uv;
	return o;
}

float3 HeatMapClipColour(float t)
{
	float3 clipColour = t < 0.0 ? float3(0.0, 1.0, 1.0) : float3(1.0, 0.0, 1.0);
	#if defined(UNITY_COLORSPACE_GAMMA)
		return clipColour;
	#else
		return SRGBToLinear(clipColour);
	#endif
}

static const float GradientAtlasHeatRow = 0.625;
static const float GradientAtlasSignedHeatRow = 0.875;

float3 SampleDebugHeatMap(float value, float row)
{
	float3 colour = tex2D(GradientAtlas, float2(saturate(value), row)).rgb;
	if (debugShowClipping != 0 && (value < 0.0 || value > 1.0))
	{
		return HeatMapClipColour(value);
	}
	return colour;
}

float BlobColourWeight(float3 blobColourSum)
{
	return max(max(blobColourSum.r, blobColourSum.g), blobColourSum.b);
}

float HashGridLine(float2 uv)
{
	float cellSize = max(hashGridCellSize, 0.0001);
	float2 worldPos = hashGridWorldCenter + (uv - 0.5) * hashGridWorldSize;
	float2 gridPos = worldPos / cellSize;
	float2 cellUv = frac(gridPos);
	float edgeDistance = min(min(cellUv.x, 1.0 - cellUv.x), min(cellUv.y, 1.0 - cellUv.y));
	float lineWidth = max(fwidth(gridPos.x), fwidth(gridPos.y)) * 1.25;
	return 1.0 - smoothstep(0.0, lineWidth, edgeDistance);
}

bool ResolveMetaball(v2f i, out float alpha, out float3 litColour)
{
	float2 materialUv = i.uv;
	float4 materialTransport = tex2D(MaterialTransportTex, materialUv);
	alpha = saturate(materialTransport.g);
	if (alpha <= 0.0001)
	{
		litColour = 0.0;
		return false;
	}

	float scalarData = materialTransport.r;
	float phaseT = saturate(materialTransport.b);
	
	if (debugMode == 1) // Normals
	{
		litColour = SRGBToLinear(tex2D(MaterialNormalTex, materialUv).rgb);
		return true;
	}

	if (debugMode == 2) // Curvature
	{
		litColour = SampleDebugHeatMap(0.5 + scalarData * 0.5, GradientAtlasSignedHeatRow);
		return true;
	}

	if (debugMode == 3 || debugMode == 4 || debugMode == 5) // Viscosity / Density / Temperature
	{
		litColour = SampleDebugHeatMap(scalarData, GradientAtlasHeatRow);
		return true;
	}
	
	if (debugMode == 6) // Blob ids
	{
		float4 combined = tex2D(CombinedTex, materialUv);
		float density0 = BlobColourWeight(combined.rgb);
		float blobWeight = max(density0, 0.0001);
		float3 blobCol = combined.rgb / blobWeight;
		litColour = saturate(lerp(blobCol, float3(0, 0, 0), phaseT));
		return true;
	}

	litColour = 0.0;
	return false;
}

float3 ApplyMotionClipMarker(float rawMotionMagnitude, float3 colour)
{
	if (debugShowClipping != 0 && rawMotionMagnitude > 1.0)
	{
		return HeatMapClipColour(rawMotionMagnitude);
	}

	return colour;
}

float4 frag(v2f i) : SV_Target
{
	float gridLine = showHashGridOverlay != 0 ? HashGridLine(i.uv) : 0.0;
	if (gridLine > 0.001)
	{
		return float4(float3(0.15, 0.9, 1.0), gridLine * 0.85);
	}
	if (hashGridOverlayOnly != 0)
	{
		discard;
	}

	float2 causticUv = i.uv;
	if (debugMode == 7) // Caustics
	{
		return float4(tex2D(DebugTex0, causticUv).rgb / max(particleCausticDebugExposure, 0.0001), 1.0);
	}
	if (debugMode == 8 || debugMode == 9) // Soft light / Raw radiance cascade
	{
		return float4(tex2D(DebugTex0, causticUv).rgb, 1.0);
	}
	if (debugMode == 10) // Particle motion
	{
		float2 materialUv = i.uv;
		float4 packedVelocity = tex2D(VelocityTex, materialUv);
		float2 weightedVelocity = packedVelocity.rg;
		float weight = packedVelocity.b;
		if (weight <= 0.0001)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		float2 velocity = weight > 0.0001 ? weightedVelocity / weight : 0.0;
		float2 debugMotion = velocity * max(motionDebugDeltaTime, 0.0);
		float motionScale = max(debugGradientMax, 0.0001);
		float rawMotionMagnitude = length(debugMotion) * motionScale;
		float motionMagnitude = saturate(rawMotionMagnitude);
		float2 motionDirection = length(debugMotion) > 0.0000001 ? normalize(debugMotion) : 0.0;
		float2 motionColour = saturate(0.5 + motionDirection * 0.5);
		float3 debugColour = float3(motionColour * motionMagnitude, motionMagnitude);
		debugColour = weight > 0.0001 ? ApplyMotionClipMarker(rawMotionMagnitude, debugColour) : debugColour;
		return float4(debugColour, 1.0);
	}
	float alpha;
	float3 colour;
	if (!ResolveMetaball(i, alpha, colour))
	{
		discard;
	}

	return float4(colour, alpha);
}
		ENDHLSL

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDHLSL
		}
	}
}
