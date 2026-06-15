Shader "Hidden/Particle2DParticleFluidColorBleed" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
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

sampler2D _MainTex;
sampler2D _ParticleFluidSourceTex;
sampler2D _ParticleFluidBleedTex;
sampler2D MaterialAlbedoTex;
sampler2D MaterialNormalTex;
sampler2D MaterialNormalTex1;
float4 _MainTex_TexelSize;
float _BleedStrength;
float _BleedRadius;
float _BleedSelfSubtract;
float _BleedNormalWeight;
int particleFluidCompositeRegionEnabled;
float4 particleFluidCompositeUvRect;
int particleFluidClipRegionEnabled;
float4 particleFluidClipRect;

v2f vert(appdata v)
{
	v2f o;
	o.vertex = UnityObjectToClipPos(v.vertex);
	o.uv = v.uv;
	if (particleFluidClipRegionEnabled != 0)
	{
		float2 clipMin = particleFluidClipRect.xy * 2.0 - 1.0;
		float2 clipMax = (particleFluidClipRect.xy + particleFluidClipRect.zw) * 2.0 - 1.0;
		o.vertex.xy = lerp(clipMin, clipMax, v.uv) * o.vertex.w;
	}
	return o;
}

float FluidMask(float2 uv)
{
	return tex2D(MaterialAlbedoTex, uv).a;
}

bool TryGetMaterialUv(float2 screenUv, out float2 materialUv)
{
	if (particleFluidCompositeRegionEnabled == 0)
	{
		materialUv = screenUv;
		return true;
	}

	float2 localUv = (screenUv - particleFluidCompositeUvRect.xy) / max(particleFluidCompositeUvRect.zw, float2(0.000001, 0.000001));
	materialUv = localUv;
	return all(localUv >= 0.0) && all(localUv <= 1.0);
}

float3 MaterialNormal(float2 uv)
{
	float4 normal0 = tex2D(MaterialNormalTex, uv);
	float4 normal1 = tex2D(MaterialNormalTex1, uv);
	float phaseT = saturate(normal0.a);
	return normalize(lerp(normal0.rgb, normal1.rgb, phaseT) * 2.0 - 1.0);
}

float NormalBleedWeight(float2 uv)
{
	float3 normal = MaterialNormal(uv);
	float edgeWeight = pow(saturate(length(normal.xy)), 0.5);
	return lerp(1.0, edgeWeight, saturate(_BleedNormalWeight));
}

float Gaussian(float x, float sigma)
{
	return exp(-(x * x) / max(2.0 * sigma * sigma, 0.0001));
}

float Luminance(float3 colour)
{
	return dot(colour, float3(0.2126, 0.7152, 0.0722));
}

float3 DiffuseBleedSource(float2 uv, float3 premultipliedColour, float mask)
{
	float3 sourceColour = mask > 0.0001 ? premultipliedColour / mask : 0.0;
	float3 albedo = tex2D(MaterialAlbedoTex, uv).rgb;
	float albedoLuminance = max(Luminance(albedo), 0.0001);
	float sourceLuminance = max(Luminance(sourceColour), 0.0);
	return albedo * min(sourceLuminance / albedoLuminance, 4.0);
}

float4 fragDownsample(v2f i) : SV_Target
{
	float mask = saturate(FluidMask(i.uv));
	float normalWeight = NormalBleedWeight(i.uv);
	float weight = mask * normalWeight;
	float3 premultipliedColour = tex2D(_ParticleFluidSourceTex, i.uv).rgb;
	float3 colour = DiffuseBleedSource(i.uv, premultipliedColour, mask);
	return float4(colour * weight, weight);
}

float4 Blur(v2f i, float2 direction)
{
	int tapRadius = min((int)ceil(_BleedRadius), 96);
	float sigma = max(_BleedRadius / 2.5, 0.001);
	float4 sum = 0.0;
	float weightSum = 0.0;

	for (int tap = -96; tap <= 96; tap++)
	{
		if (abs(tap) > tapRadius)
		{
			continue;
		}

		float weight = Gaussian((float)tap, sigma);
		float2 uv = i.uv + direction * _MainTex_TexelSize.xy * tap;
		sum += tex2D(_MainTex, uv) * weight;
		weightSum += weight;
	}

	return sum / max(weightSum, 0.0001);
}

float4 fragBlurHorizontal(v2f i) : SV_Target
{
	return Blur(i, float2(1.0, 0.0));
}

float4 fragBlurVertical(v2f i) : SV_Target
{
	return Blur(i, float2(0.0, 1.0));
}

float4 fragComposite(v2f i) : SV_Target
{
	float2 materialUv;
	if (!TryGetMaterialUv(i.uv, materialUv))
	{
		discard;
	}

	float4 source = tex2D(_ParticleFluidSourceTex, materialUv);
	float mask = saturate(FluidMask(materialUv));
	if (mask <= 0.0001 || _BleedStrength <= 0.0001)
	{
		return source;
	}

	float4 blurred = tex2D(_ParticleFluidBleedTex, materialUv);
	float3 bleedColour = blurred.a > 0.0001 ? blurred.rgb / blurred.a : 0.0;
	float receiverWeight = NormalBleedWeight(materialUv);
	float3 sourceColour = DiffuseBleedSource(materialUv, source.rgb, mask);
	float3 bleed = max(bleedColour - sourceColour * saturate(_BleedSelfSubtract), 0.0);
	return float4(source.rgb + bleed * (_BleedStrength * mask * receiverWeight), mask);
}

		ENDCG

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragDownsample
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBlurHorizontal
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBlurVertical
			ENDCG
		}

		Pass {
			Blend One OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragComposite
			ENDCG
		}
	}
}
