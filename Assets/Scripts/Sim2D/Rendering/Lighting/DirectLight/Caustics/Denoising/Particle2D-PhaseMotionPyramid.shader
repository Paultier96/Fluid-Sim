Shader "Hidden/Particle2DPhaseMotionPyramid" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
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
		sampler2D _LowMipTex;
		float4 _MainTex_TexelSize;
		float pyramidBlendStrength;

		v2f vert(appdata v)
		{
			v2f o;
			o.vertex = UnityObjectToClipPos(v.vertex);
			o.uv = v.uv;
			return o;
		}

		float4 fragDownsample(v2f i) : SV_Target
		{
			float2 texel = _MainTex_TexelSize.xy;
			float2 offsets[4] = {
				float2(-0.5, -0.5),
				float2( 0.5, -0.5),
				float2(-0.5,  0.5),
				float2( 0.5,  0.5)
			};

			float4 sum = 0.0;
			[unroll]
			for (int idx = 0; idx < 4; idx++)
			{
				sum += tex2D(_MainTex, i.uv + texel * offsets[idx]);
			}

			return sum * 0.25;
		}

		float4 fragUpsample(v2f i) : SV_Target
		{
			float4 high = tex2D(_MainTex, i.uv);
			float4 low = tex2D(_LowMipTex, i.uv);
			float blend = saturate(pyramidBlendStrength);
			float4 merged = lerp(high, low, blend);
			merged.a = 1.0;
			return merged;
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
			#pragma fragment fragUpsample
			ENDCG
		}
	}
}
