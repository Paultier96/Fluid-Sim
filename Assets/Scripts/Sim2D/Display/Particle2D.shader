Shader "Instanced/Particle2D" {
	Properties {
		
	}
	SubShader {

		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			StructuredBuffer<float2> Positions2D;
			StructuredBuffer<float2> Velocities;
			StructuredBuffer<float2> DensityData;
			StructuredBuffer<int> Phases;
			StructuredBuffer<float> Temperatures; // ADDED


			float scale;
			float4 colA;
			Texture2D<float4> ColourMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;

			// Phase colours (set from C#)
			float4 phase0Color;
			float4 phase1Color;
			float tempMin; // ADDED: set from C# or just hardcode for now
			float tempMax; // ADDED

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				float speed = length(Velocities[instanceID]);
				float speedT = saturate(speed / velocityMax);
				float colT = speedT;
				
				float3 centreWorld = float3(Positions2D[instanceID], 0);
				float3 worldVertPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * scale);
				float3 objectVertPos = mul(unity_WorldToObject, float4(worldVertPos.xyz, 1));

				v2f o;
				o.uv = v.texcoord;
				o.pos = UnityObjectToClipPos(objectVertPos);

				int pid = Phases[instanceID];
				float3 phaseCol = pid == 0 ? phase0Color.rgb : phase1Color.rgb;
				float temp = Temperatures[instanceID];
				float tempT = saturate((temp - tempMin) / max(tempMax - tempMin, 0.001));
				float3 coldCol = float3(0, 0.3, 1);   // blue = cold
				float3 hotCol  = float3(1, 0.2, 0);   // red = hot
				float3 tempCol = lerp(coldCol, hotCol, tempT);
				// Blend: mostly phase color, tinted by temperature
				o.colour = lerp(phaseCol, tempCol, 1);


				return o;
			}


			float4 frag (v2f i) : SV_Target
			{
				float2 centreOffset = (i.uv.xy - 0.5) * 2;
				float sqrDst = dot(centreOffset, centreOffset);
				float delta = fwidth(sqrt(sqrDst));
				float alpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);

				float3 colour = i.colour;
				return float4(colour, alpha);
			}

			ENDCG
		}
	}
}