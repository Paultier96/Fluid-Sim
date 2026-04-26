Shader "Instanced/Particle2DMetaball" {
	Properties {
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
		Blend One One
		ZWrite Off
		Cull Off

		Pass {
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			StructuredBuffer<float2> Positions2D;
			StructuredBuffer<int> Phases;
			StructuredBuffer<float> Temperatures;
			StructuredBuffer<float2> CSFGradients;

			float scale;
			float tempMin;
			float tempMax;
			float debugGradientMax;
			float metaballSharpness;
			float metaballIntensity;
			int debugMode;

			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float tempT : TEXCOORD1;
				float2 csfDebug : TEXCOORD2;
				nointerpolation float phase : TEXCOORD3;
			};

			v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
			{
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				float temp = Temperatures[instanceID];
				float tempT = saturate((temp - tempMin) / max(tempMax - tempMin, 0.001));
				float2 csfData = CSFGradients[instanceID];

				v2f o;
				o.pos = UnityObjectToClipPos(objectVertPos);
				o.uv = v.texcoord;
				o.tempT = tempT;
				o.csfDebug = csfData;
				o.phase = Phases[instanceID];
				return o;
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv - 0.5) * 2;
				float r2 = dot(p, p);
				if (r2 >= 1.0) discard;

				float kernel = exp(-r2 * max(metaballSharpness, 0.01)) * metaballIntensity;
				if (debugMode != 0)
				{
					// Debug mode packs two weighted values into one texture:
					// R/G = debug X, B/A = debug Y (or curvature duplicated).
					float maxAbsValue = max(debugGradientMax, 0.0001);
					float2 debugData;
					if (debugMode == 1)
						debugData = i.csfDebug;
					else if (debugMode == 2 || debugMode == 4)
						debugData = i.csfDebug.xx / maxAbsValue;
					else
						debugData = i.csfDebug / maxAbsValue;
					return float4(debugData.x * kernel, kernel, debugData.y * kernel, kernel);
				}

				float2 packed = float2(i.tempT * kernel, kernel);

				return i.phase < 0.5 ? float4(packed, 0, 0) : float4(0, 0, packed);
			}

			ENDCG
		}
	}
}
