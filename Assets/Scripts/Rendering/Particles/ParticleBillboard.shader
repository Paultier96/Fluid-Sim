Shader "Fluid/ParticleBillboard" {
	Properties {
		
	}
	SubShader {

		Tags {"Queue"="Geometry" }

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"
			
			StructuredBuffer<float3> Positions;
			StructuredBuffer<float3> Velocities;
			StructuredBuffer<uint> Phases;
			StructuredBuffer<uint> ParticleIDs;
			Texture2D<float4> ColourMap;
			SamplerState linear_clamp_sampler;
			float velocityMax;
			int usePhaseColoring;
			int numPhaseRows;

			float scale;
			float3 colour;

			float4x4 localToWorld;

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 colour : TEXCOORD1;
				float3 normal : NORMAL;
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				v2f o;
				o.uv = v.texcoord;
				o.normal = v.normal;
				
				float3 centreWorld = Positions[instanceID];
				float3 objectVertPos = v.vertex * scale * 2;
				float4 viewPos = mul(UNITY_MATRIX_V, float4(centreWorld, 1)) + float4(objectVertPos, 0);
				o.pos = mul(UNITY_MATRIX_P, viewPos);


				float speed = length(Velocities[instanceID]);
				float speedT = saturate(speed / velocityMax);
				float colT = speedT;
				float phaseT = 0.5;
				if (usePhaseColoring != 0)
				{
					uint originalIndex = ParticleIDs[instanceID];
					uint phase = Phases[originalIndex];
					phaseT = (phase + 0.5) / max((float)numPhaseRows, 1.0);
				}
				o.colour = ColourMap.SampleLevel(linear_clamp_sampler, float2(colT, phaseT), 0);

				return o;
			}

			float4 frag (v2f i) : SV_Target
			{
				float shading = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
				shading = (shading + 0.6) / 1.4;
				return float4(i.colour, 1);
			}

			ENDCG
		}
	}
}