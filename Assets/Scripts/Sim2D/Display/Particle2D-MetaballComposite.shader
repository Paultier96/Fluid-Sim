Shader "Hidden/Particle2DMetaballComposite" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always
		Blend SrcAlpha OneMinusSrcAlpha

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

			// Single RGBA texture: RG = phase0 (weighted data, density), BA = phase1 (weighted data, density)
			sampler2D CombinedTex;
			sampler2D ColourMap;
			sampler2D ColourMap2;
			float densityThreshold;
			float edgeSoftness;
			float phaseBlendWidth;
			int debugMode;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 combined = tex2D(CombinedTex, i.uv);
				float density0 = combined.g;
				float density1 = combined.a;
				float density = max(density0, density1);
				float alpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
				if (alpha <= 0.0001) discard;

				float phaseT = smoothstep(-phaseBlendWidth, phaseBlendWidth, density1 - density0);

				float data0 = combined.r / max(density0, 0.0001);
				float data1 = combined.b / max(density1, 0.0001);

				float3 colour0, colour1;
				if (debugMode != 0)
				{
					if (debugMode == 1)
					{
						float2 grad = float2(data0, data1);
						float2 mapped = saturate(0.5 + grad * 0.5);
						return float4(mapped.x, mapped.y, 1, alpha);
					}

					float curvature = data0;
					float t = saturate(0.5 + curvature * 0.5);
					float3 negCol = float3(1.0, 0.0, 0.0);
					float3 zeroCol = float3(0.0, 0.0, 1.0);
					float3 posCol = float3(0.0, 1.0, 0.0);
					float3 curvCol = (t < 0.5) ? lerp(negCol, zeroCol, t * 2.0) : lerp(zeroCol, posCol, (t - 0.5) * 2.0);
					return float4(curvCol, alpha);
				}
				else
				{
					colour0 = tex2D(ColourMap,  float2(data0, 0.5)).rgb;
					colour1 = tex2D(ColourMap2, float2(data1, 0.5)).rgb;
				}

				return float4(lerp(colour0, colour1, phaseT), alpha);
			}
			ENDCG
		}
	}
}
