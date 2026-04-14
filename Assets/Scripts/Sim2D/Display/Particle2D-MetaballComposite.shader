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

			sampler2D Phase0Tex;
			sampler2D Phase1Tex;
			float densityThreshold;
			float edgeSoftness;
			float opacity;
			float phaseBlendWidth;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 phase0 = tex2D(Phase0Tex, i.uv);
				float4 phase1 = tex2D(Phase1Tex, i.uv);
				float density0 = phase0.a;
				float density1 = phase1.a;
				float density = max(density0, density1);
				float alpha = smoothstep(densityThreshold - edgeSoftness, densityThreshold + edgeSoftness, density) * opacity;
				if (alpha <= 0.0001) discard;

				float phaseT = smoothstep(-phaseBlendWidth, phaseBlendWidth, density1 - density0);
				float3 colour0 = phase0.rgb / max(density0, 0.0001);
				float3 colour1 = phase1.rgb / max(density1, 0.0001);
				float3 colour = lerp(colour0, colour1, phaseT);
				return float4(colour, alpha);
			}
			ENDCG
		}
	}
}
