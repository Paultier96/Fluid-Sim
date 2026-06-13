Shader "Hidden/Particle2DJumpFloodMaterial" {
	Properties {
		_ResultTex ("Result Texture", 2D) = "black" {}
		_PayloadTex ("Payload Texture", 2D) = "black" {}
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always

		CGINCLUDE
		#include "UnityCG.cginc"

struct appdata {
	float4 vertex : POSITION;
	float2 uv : TEXCOORD0;
};

struct v2f {
	float2 uv : TEXCOORD0;
	float4 vertex : SV_POSITION;
};

sampler2D _ResultTex;
sampler2D _PayloadTex;
sampler2D _NormalPayloadTex;
sampler2D MaterialAlbedoTex;
float4 _ResultTex_TexelSize;
float4x4 _InverseViewProjection;
float2 ellipseBoundsCenter;
float2 ellipseBoundsSize;
float2 boundsSize;
float obstacleY;
int useEllipticalBounds;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	return o;
}

float2 WorldFromUV(float2 uv)
{
	float4 clip = float4(uv * 2.0 - 1.0, 0.0, 1.0);
	float4 world = mul(_InverseViewProjection, clip);
	return world.xy / max(world.w, 0.0001);
}

float EllipseCutSignedDistance(float2 worldPos)
{
	float2 radii = max(abs(ellipseBoundsSize), 0.0001);
	float2 rel = worldPos - ellipseBoundsCenter;
	float2 q = rel / radii;
	float qLen = max(length(q), 0.0001);
	float ellipseGradientLength = length(float2(q.x / radii.x, q.y / radii.y)) / qLen;
	float ellipseDistance = (1.0 - qLen) / max(ellipseGradientLength, 0.0001);
	float cutDistance = worldPos.y - obstacleY;
	return min(ellipseDistance, cutDistance);
}

float RectSignedDistance(float2 worldPos)
{
	float2 halfSize = max(abs(boundsSize) * 0.5, 0.0001);
	float2 distanceToEdge = halfSize - abs(worldPos);
	return min(distanceToEdge.x, distanceToEdge.y);
}

float BoundsMask(float2 worldPos)
{
	float signedDistance = useEllipticalBounds != 0
		? EllipseCutSignedDistance(worldPos)
		: RectSignedDistance(worldPos);
	float aa = max(fwidth(signedDistance), 0.0001);
	return smoothstep(0.0, aa, signedDistance);
}

bool ResolveJumpFloodMaterial(v2f i, out float alpha, out float phaseT, out float3 normal, out float3 albedo)
{
	float4 seed = tex2D(_ResultTex, i.uv);
	alpha = seed.w >= 0.0 ? BoundsMask(WorldFromUV(i.uv)) : 0.0;
	phaseT = saturate(seed.w);
	albedo = tex2D(_PayloadTex, i.uv).rgb;
	normal = normalize(tex2D(_NormalPayloadTex, i.uv).rgb * 2.0 - 1.0);
	return alpha > 0.0001;
}

float4 fragMaterialAlbedo(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal;
	float3 albedo;
	if (!ResolveJumpFloodMaterial(i, alpha, phaseT, normal, albedo))
	{
		return 0.0;
	}

	return float4(albedo, alpha);
}

float4 fragMaterialNormal(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal;
	float3 albedo;
	if (!ResolveJumpFloodMaterial(i, alpha, phaseT, normal, albedo))
	{
		return 0.0;
	}

	return float4(saturate(normal * 0.5 + 0.5), phaseT);
}

float4 fragMaterialNormal1(v2f i) : SV_Target
{
	float alpha;
	float phaseT;
	float3 normal;
	float3 albedo;
	if (!ResolveJumpFloodMaterial(i, alpha, phaseT, normal, albedo))
	{
		return 0.0;
	}

	return float4(saturate(normal * 0.5 + 0.5), alpha);
}

float4 fragUnlitAlbedo(v2f i) : SV_Target
{
	float4 materialAlbedo = tex2D(MaterialAlbedoTex, i.uv);
	if (materialAlbedo.a <= 0.0001)
	{
		discard;
	}

	return materialAlbedo;
}
		ENDCG

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialAlbedo
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialNormal
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragMaterialNormal1
			ENDCG
		}

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragUnlitAlbedo
			ENDCG
		}
	}
}
