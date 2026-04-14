Shader "Hidden/Particle2DMetaballBlur" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Cull Off
		ZWrite Off
		ZTest Always

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

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
			float4 _MainTex_TexelSize;
			float2 blurDirection;
			float blurRadius;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float Gaussian(float x, float sigma)
			{
				return exp(-(x * x) / max(2.0 * sigma * sigma, 0.0001));
			}

			float4 frag(v2f i) : SV_Target
			{
				int radius = clamp((int)ceil(blurRadius), 0, 32);
				if (radius == 0)
				{
					return tex2D(_MainTex, i.uv);
				}

				float sigma = max(blurRadius / 2.5, 0.001);
				float2 delta = _MainTex_TexelSize.xy * blurDirection;
				float4 sum = 0;
				float weightSum = 0;

				for (int tap = -radius; tap <= radius; tap++)
				{
					float w = Gaussian(tap, sigma);
					sum += tex2D(_MainTex, i.uv + delta * tap) * w;
					weightSum += w;
				}

				return sum / max(weightSum, 0.0001);
			}
			ENDCG
		}
	}
}
