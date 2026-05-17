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
			sampler2D NormalTex;
			sampler2D ColourMap;
			sampler2D ColourMap2;
			sampler2D DebugHeatMap;
			sampler2D DebugSignedHeatMap;
			float densityThreshold;
			float edgeSoftness;
			float phaseBlendWidth;
			float metaballRefractionStrength;
			float metaballRefractionEdgeFade;
			int debugMode;
			float ditherStrength;
			float3 particleLightDirection;
			float4 particleLightColor;
			float particleAmbientLight;
			float particleDirectionalLightIntensity;
			float particleNormalStrength;
			float4 particleSpecularColor;
			float particleSpecularIntensity;
			float particleSpecularPower;
			float4 particleFresnelColor;
			float particleFresnelIntensity;
			float particleFresnelPower;
			float2 particleGlowDirection;
			float4 particleGlowColor;
			float particleGlowIntensity;
			float particleGlowPower;
			float particleTransmissionIntensity;
			float particleTransmissionPower;
			float particleEdgeDarkening;
			float particleEdgeDarkeningPower;

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float InterleavedGradientNoise(float2 pixel)
			{
				float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
				return frac(magic.z * frac(dot(pixel, magic.xy)));
			}

			float Dither01(float t, float noise)
			{
				return saturate(t + (noise - 0.5) * ditherStrength);
			}

			float3 NormalFromXY(float2 normalXY)
			{
				normalXY *= particleNormalStrength;
				float lenSq = dot(normalXY, normalXY);
				if (lenSq > 0.999)
				{
					normalXY *= rsqrt(lenSq) * 0.999;
					lenSq = dot(normalXY, normalXY);
				}
				float normalZ = sqrt(saturate(1.0 - lenSq));
				return normalize(float3(normalXY, normalZ));
			}

			float3 GetPhaseNormal(float4 normalPacked, float density0, float density1, bool usePhase1)
			{
				float phaseDensity = usePhase1 ? density1 : density0;
				float2 encodedNormalXY = usePhase1
					? normalPacked.ba / max(density1, 0.0001)
					: normalPacked.rg / max(density0, 0.0001);
				float2 normalXY = phaseDensity > 0.0001 ? encodedNormalXY * 2.0 - 1.0 : 0.0;
				return NormalFromXY(normalXY);
			}

			float3 GetBlendedPhaseNormal(float4 normalPacked, float density0, float density1, float phaseT)
			{
				float3 normal0 = GetPhaseNormal(normalPacked, density0, density1, false);
				float3 normal1 = GetPhaseNormal(normalPacked, density0, density1, true);
				return normalize(lerp(normal0, normal1, phaseT));
			}

			float3 ApplyParticleLighting(float3 colour, float2 uv, float density0, float density1, float phaseT)
			{
				float4 normalPacked = tex2D(NormalTex, uv);
				float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
				float lightDirLength = max(length(particleLightDirection), 0.0001);
				float3 lightDir = particleLightDirection / lightDirLength;
				float directionalLight = saturate(dot(normal, lightDir)) * particleDirectionalLightIntensity;
				float3 lighting = particleAmbientLight + particleLightColor.rgb * directionalLight;
				float3 viewDir = float3(0.0, 0.0, 1.0);
				float3 halfVector = lightDir + viewDir;
				float3 halfDir = halfVector / max(length(halfVector), 0.0001);
				float specular = pow(saturate(dot(normal, halfDir)), max(particleSpecularPower, 1.0)) * particleSpecularIntensity;
				float fresnel = pow(saturate(1.0 - dot(normal, viewDir)), max(particleFresnelPower, 0.1)) * particleFresnelIntensity;
				float2 glowDir = particleGlowDirection / max(length(particleGlowDirection), 0.0001);
				float directionalGlow = pow(saturate(dot(normal.xy, glowDir)), max(particleGlowPower, 0.1)) * saturate(1.0 - normal.z) * particleGlowIntensity;
				float transmission = pow(saturate(dot(-normal, float3(lightDir.xy,0))), max(particleTransmissionPower, 0.1)) * particleTransmissionIntensity;
				float edgeT = pow(saturate(1.0 - normal.z), max(particleEdgeDarkeningPower, 0.1)) * particleEdgeDarkening;
				colour *= 1.0 - edgeT;
				return saturate(
					colour * lighting
					+ particleSpecularColor.rgb * specular
					+ particleFresnelColor.rgb * fresnel
					+ particleGlowColor.rgb * directionalGlow
					+ colour * transmission
				);
			}

			float4 frag(v2f i) : SV_Target
			{
				float4 combined = tex2D(CombinedTex, i.uv);
				float density0 = combined.g;
				float density1 = combined.a;
				float density = max(density0, density1);
				float alpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
				if (alpha <= 0.0001) discard;

				float phaseDelta = density1 - density0;
				float phaseAA = max(0.5 * fwidth(phaseDelta) * max(phaseBlendWidth, 0.0001), 0.00001);
				float phaseT = smoothstep(-phaseAA, phaseAA, phaseDelta);

				float data0 = combined.r / max(density0, 0.0001);
				float data1 = combined.b / max(density1, 0.0001);
				float noise = InterleavedGradientNoise(i.vertex.xy);

				float3 colour0, colour1;
				if (debugMode != 0)
				{
					if (debugMode == 6)
					{
						float3 blobCol = combined.rgb / max(combined.a, 0.0001);
						return float4(saturate(blobCol), alpha);
					}

					if (debugMode == 1)
					{
						float4 normalPacked = tex2D(NormalTex, i.uv);
						float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
						float3 encodedNormal = saturate(0.5 + normal / 2.0);
						#if defined(UNITY_COLORSPACE_GAMMA)
							return float4(encodedNormal, alpha);
						#else
							return float4(GammaToLinearSpace(encodedNormal), alpha);
						#endif
					}

					if (debugMode == 2)
					{
						float curvature = data0;
						return float4(tex2D(DebugSignedHeatMap, float2(saturate(0.5 + curvature * 0.5), 0.5)).rgb, alpha);
					}

					if (debugMode == 3)
					{
						float viscosity = Dither01(lerp(data0, data1, phaseT), noise);
						return float4(tex2D(DebugHeatMap, float2(viscosity, 0.5)).rgb, alpha);
					}

					if (debugMode == 4)
					{
						float densityVal = Dither01(lerp(data0, data1, phaseT), noise);
						return float4(tex2D(DebugHeatMap, float2(densityVal, 0.5)).rgb, alpha);
					}

					if (debugMode == 5)
					{
						float tempVal = Dither01(lerp(data0, data1, phaseT), noise);
						return float4(tex2D(DebugHeatMap, float2(tempVal, 0.5)).rgb, alpha);
					}

					float2 force = float2(data0, data1);
					float2 mapped = saturate(0.5 + force * 0.5);
					float mag = saturate(length(force));
					return float4(mapped, mag, alpha);
				}
				else
				{
					float4 normalPacked = tex2D(NormalTex, i.uv);
					float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
					float refractionMask = smoothstep(0.0, max(metaballRefractionEdgeFade, 0.0001), density - densityThreshold);
					float2 refractedUv = saturate(i.uv - normal.xy * metaballRefractionStrength * refractionMask);
					float4 refractedCombined = tex2D(CombinedTex, refractedUv);
					float refractedData0 = refractedCombined.g > 0.0001 ? refractedCombined.r / refractedCombined.g : data0;
					float refractedData1 = refractedCombined.a > 0.0001 ? refractedCombined.b / refractedCombined.a : data1;

					colour0 = tex2D(ColourMap,  float2(Dither01(refractedData0, noise), 0.5)).rgb;
					colour1 = tex2D(ColourMap2, float2(Dither01(refractedData1, noise), 0.5)).rgb;
				}

				float3 colour = ApplyParticleLighting(lerp(colour0, colour1, phaseT), i.uv, density0, density1, phaseT);
				return float4(colour, alpha);
			}
			ENDCG
		}
	}
}
