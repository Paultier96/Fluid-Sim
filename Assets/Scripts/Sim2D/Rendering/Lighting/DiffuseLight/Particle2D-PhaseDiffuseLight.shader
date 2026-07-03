Shader "Hidden/Particle2DPhaseDiffuseLight"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "black" {}
	}

	SubShader
	{
		Cull Off
		ZWrite Off
		ZTest Always

		CGINCLUDE
		#include "UnityCG.cginc"
		#include "../Shared/ParticleFluidCommon.hlsl"
		#include "../Shared/ParticleFluidPhaseClassification.hlsl"

		sampler2D _MainTex;
		float4 _MainTex_TexelSize;
		sampler2D SharpCausticsTex;
		sampler2D CombinedTex;
		float densityThreshold;
		float edgeSoftness;
		float phaseBlendWidth;
		float phase0RenderBias;
		float scatterStrengthA;
		float lightIntensity;
		int useEllipticalBounds;
		float2 ellipseBoundsCenter;
		float2 ellipseBoundsSize;
		float obstacleY;
		float analyticBoundaryExpansion;
		#include "../Shared/ParticleFluidAnalyticBoundary.hlsl"
		float2 softLightWorldCenter;
		float2 softLightWorldSize;
		float4 softLightSourceUvRect;
		float2 softLightSize;

		struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

		v2f vert(appdata v)
		{
			v2f o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			return o;
		}

		float2 SourceUvFromSoftLightUv(float2 uv)
		{
			return softLightSourceUvRect.xy + uv * softLightSourceUvRect.zw;
		}

		float4 SampleCombinedAtUv(float2 uv)
		{
			return tex2Dlod(CombinedTex, float4(SourceUvFromSoftLightUv(uv), 0, 0));
		}

		float2 PhaseDensities(float2 uv)
		{
			return ParticleFluidPhaseDensities(SampleCombinedAtUv(uv));
		}

		float PhaseT(float2 uv, float2 densities)
		{
			float totalDensity = densities.x + densities.y;
			float phaseRatio = ParticleFluidPhaseRatio(densities);
			float phaseBoundary = ParticleFluidPhaseBoundary(phase0RenderBias);
			if (totalDensity < densityThreshold)
			{
				return phaseRatio;
			}

			float2 texel = 1.0 / max(softLightSize, float2(1.0, 1.0));
			float phaseRatioRight = ParticleFluidPhaseRatio(PhaseDensities(saturate(uv + float2(texel.x, 0.0))));
			float phaseRatioLeft = ParticleFluidPhaseRatio(PhaseDensities(saturate(uv - float2(texel.x, 0.0))));
			float phaseRatioUp = ParticleFluidPhaseRatio(PhaseDensities(saturate(uv + float2(0.0, texel.y))));
			float phaseRatioDown = ParticleFluidPhaseRatio(PhaseDensities(saturate(uv - float2(0.0, texel.y))));
			float phaseGradient = abs(phaseRatioRight - phaseRatioLeft) + abs(phaseRatioUp - phaseRatioDown);
			float phaseAA = max(0.25 * phaseGradient * max(phaseBlendWidth, 0.0001), 0.00001);
			return smoothstep(-phaseAA, phaseAA, phaseRatio - phaseBoundary);
		}

		float OuterAlpha(float2 uv, float2 densities)
		{
			float density = max(densities.x, densities.y);
			float particleAlpha = smoothstep(max(densityThreshold - edgeSoftness, 0.0), densityThreshold + edgeSoftness, density);
			if (useEllipticalBounds == 0)
			{
				return particleAlpha;
			}

			float boundsDistance = OuterAnalyticBoundaryDistance(ParticleFluidWorldFromUv(uv, softLightWorldCenter, softLightWorldSize));
			float2 worldTexel = max(softLightWorldSize / max(softLightSize, float2(1.0, 1.0)), float2(0.0001, 0.0001));
			float boundsAA = max(max(worldTexel.x, worldTexel.y), 0.0001);
			float boundsAlpha = smoothstep(boundsAA, -boundsAA, boundsDistance);
			return min(particleAlpha, boundsAlpha);
		}

		float Phase0Support(float2 uv, float2 densities)
		{
			float outerAlpha = OuterAlpha(uv, densities);
			float phaseT = PhaseT(uv, densities);
			float phase0 = 1.0 - smoothstep(0.45, 0.65, phaseT);
			return phase0 * outerAlpha;
		}

		float4 fragInit(v2f i) : SV_Target
		{
			float2 densities = PhaseDensities(i.uv);
			float phase0Support = Phase0Support(i.uv, densities);
			float3 caustics = tex2Dlod(SharpCausticsTex, float4(i.uv, 0, 0)).rgb;
			float scatterStrength = max(scatterStrengthA, 0.0) * max(lightIntensity, 0.0);
			return float4(caustics * (phase0Support * scatterStrength), phase0Support);
		}
		ENDCG

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragInit
			#pragma target 3.0
			ENDCG
		}
	}
}
