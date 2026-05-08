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

			float3 HeatMapColor(float t)
			{
				t = saturate(t);
				float3 c0 = float3(0.0, 0.0, 0.0);
				float3 c1 = float3(0.125, 0.0, 0.549);
				float3 c2 = float3(0.8, 0.0, 0.466);
				float3 c3 = float3(1.0, 0.843, 0.0);
				float3 c4 = float3(1.0, 1.0, 1.0);

				if (t < 0.25) return lerp(c0, c1, t * 4.0);
				if (t < 0.5) return lerp(c1, c2, (t - 0.25) * 4.0);
				if (t < 0.75) return lerp(c2, c3, (t - 0.5) * 4.0);
				return lerp(c3, c4, (t - 0.75) * 4.0);
			}

			float3 SignedHeatMapColor(float t)
			{
				float a = saturate(abs(t));
				float3 pos = lerp(float3(0.0, 0.0, 0.0),
				                  float3(0.0, 0.2, 1.0),
				                  saturate(a * 2.0))
				           + lerp(float3(0.0, 0.0, 0.0),
				                  float3(0.0, 1.0, 1.0),
				                  saturate(a * 2.0 - 1.0));

				float3 neg = lerp(float3(0.0, 0.0, 0.0),
				                  float3(1.0, 0.1, 0.0),
				                  saturate(a * 2.0))
				           + lerp(float3(0.0, 0.0, 0.0),
				                  float3(1.0, 0.6, 0.0),
				                  saturate(a * 2.0 - 1.0));

				return t < 0.0 ? neg : pos;
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
					if (debugMode == 7)
					{
						float3 blobCol = combined.rgb / max(combined.a, 0.0001);
						return float4(saturate(blobCol), alpha);
					}

					if (debugMode == 1)
					{
						float2 grad = float2(data0, data1);
						float z = sqrt(saturate(1.0 - dot(grad, grad)));
						float3 normal = float3(grad, z);
						return float4(saturate(0.5 + normal / 2.0), alpha);
					}

					if (debugMode == 2)
					{
						float curvature = data0;
						return float4(SignedHeatMapColor(curvature), alpha);
					}

					if (debugMode == 3)
					{
						float2 force = float2(data0, data1);
						float2 mapped = saturate(0.5 + force * 0.5);
						float mag = saturate(length(force));
						return float4(mapped, mag, alpha);
					}

					if (debugMode == 4)
					{
						float viscosity = lerp(data0, data1, phaseT);
						return float4(HeatMapColor(viscosity), alpha);
					}

					if (debugMode == 5)
					{
						float densityVal = lerp(data0, data1, phaseT);
						return float4(HeatMapColor(densityVal), alpha);
					}

					if (debugMode == 6)
					{
						float tempVal = lerp(data0, data1, phaseT);
						return float4(HeatMapColor(tempVal), alpha);
					}

					float2 force = float2(data0, data1);
					float2 mapped = saturate(0.5 + force * 0.5);
					float mag = saturate(length(force));
					return float4(mapped, mag, alpha);
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
