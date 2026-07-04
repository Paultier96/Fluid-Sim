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
		sampler2D MaterialTransportTex;
		float scatterStrengthA;
		float lightIntensity;

		struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

		v2f vert(appdata v)
		{
			v2f o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			return o;
		}

		float4 SampleTransportAtUv(float2 uv)
		{
			return tex2Dlod(MaterialTransportTex, float4(uv, 0, 0));
		}

		float PhaseT(float2 uv)
		{
			return SampleTransportAtUv(uv).a;
		}

		float OuterAlpha(float2 uv)
		{
			return SampleTransportAtUv(uv).b;
		}

		float Phase0Support(float2 uv)
		{
			float outerAlpha = OuterAlpha(uv);
			float phaseT = PhaseT(uv);
			float phase0 = 1.0 - smoothstep(0.45, 0.65, phaseT);
			return phase0 * outerAlpha;
		}

		float4 fragInit(v2f i) : SV_Target
		{
			float phase0Support = Phase0Support(i.uv);
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
