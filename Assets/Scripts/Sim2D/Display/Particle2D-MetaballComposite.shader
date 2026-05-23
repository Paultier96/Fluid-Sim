Shader "Hidden/Particle2DMetaballComposite" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
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

		sampler2D CombinedTex;
		sampler2D NormalTex;
		sampler2D ColourMap;
		sampler2D ColourMap2;
		sampler2D DebugHeatMap;
		sampler2D DebugSignedHeatMap;
		sampler2D _MainTex;
		sampler2D BloomTex;
		float4 _MainTex_TexelSize;
		float densityThreshold;
		float edgeSoftness;
		float phaseBlendWidth;
		float phase0RenderBias;
		float phaseBiasNormalStrength;
		float metaballRefractionStrength;
		float metaballRefractionEdgeFade;
		float metaballIridescenceIntensity;
		float metaballIridescenceScale;
		int debugMode;
		float ditherStrength;
		float customBloomThreshold;
		float customBloomSoftKnee;
		float customBloomIntensity;
		float customBloomResponse;
		float customBloomSampleScale;
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
		float4 particleSubsurfaceColor;
		float particleSubsurfaceIntensity;
		float particleSubsurfacePower;
		float particleSubsurfaceThickness;
		float particleSubsurfaceEdgeBoost;

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

		float3 NormalFromXY(float2 normalXY, float normalStrengthMultiplier)
		{
			normalXY *= particleNormalStrength * max(normalStrengthMultiplier, 0.0);
			float lenSq = dot(normalXY, normalXY);
			if (lenSq > 0.999)
			{
				normalXY *= rsqrt(lenSq) * 0.999;
				lenSq = dot(normalXY, normalXY);
			}
			float normalZ = sqrt(saturate(1.0 - lenSq));
			return normalize(float3(normalXY, normalZ));
		}

		float GetPhaseNormalStrength(bool usePhase1)
		{
			float signedBias = clamp(phase0RenderBias, -1.0, 1.0) * phaseBiasNormalStrength;
			return usePhase1 ? 1.0 + signedBias : 1.0 - signedBias;
		}

		float BlobColourWeight(float3 blobColourSum)
		{
			return max(max(blobColourSum.r, blobColourSum.g), blobColourSum.b);
		}

		float Phase0Density(float4 combined)
		{
			return debugMode == 6 ? BlobColourWeight(combined.rgb) : combined.g;
		}

		float3 GetPhaseNormal(float4 normalPacked, float density0, float density1, bool usePhase1)
		{
			float phaseDensity = usePhase1 ? density1 : density0;
			float2 encodedNormalXY = usePhase1
				? normalPacked.ba / max(density1, 0.0001)
				: normalPacked.rg / max(density0, 0.0001);
			float2 normalXY = phaseDensity > 0.0001 ? encodedNormalXY * 2.0 - 1.0 : 0.0;
			return NormalFromXY(normalXY, GetPhaseNormalStrength(usePhase1));
		}

		float3 GetBlendedPhaseNormal(float4 normalPacked, float density0, float density1, float phaseT)
		{
			float3 normal0 = GetPhaseNormal(normalPacked, density0, density1, false);
			float3 normal1 = GetPhaseNormal(normalPacked, density0, density1, true);
			return normalize(lerp(normal0, normal1, phaseT));
		}

		float NormalizedData(float weightedData, float weight, float fallback)
		{
			return weight > 0.0001 ? weightedData / weight : fallback;
		}

		float3 SampleGradientColour(float2 uv, float fallbackData0, float fallbackData1, float phaseT, float noise)
		{
			float4 combined = tex2D(CombinedTex, uv);
			float data0 = NormalizedData(combined.r, combined.g, fallbackData0);
			float data1 = NormalizedData(combined.b, combined.a, fallbackData1);
			float3 colour0 = tex2D(ColourMap,  float2(Dither01(data0, noise), 0.5)).rgb;
			float3 colour1 = tex2D(ColourMap2, float2(Dither01(data1, noise), 0.5)).rgb;
			return lerp(colour0, colour1, phaseT);
		}

		float3 IridescenceRamp(float phase)
		{
			float3 offsets = float3(0.0, 0.33, 0.67);
			return 0.5 + 0.5 * cos(6.2831853 * (phase + offsets));
		}

		float3 ApplyIridescence(float3 colour, float3 normal)
		{
			if (metaballIridescenceIntensity <= 0.000001)
			{
				return colour;
			}

			float grazing = saturate(1.0 - normal.z);
			float fresnelMask = pow(grazing, 0.75);
			float filmPhase = grazing * metaballIridescenceScale;
			float3 rainbow = IridescenceRamp(frac(filmPhase));
			float amount = saturate(metaballIridescenceIntensity * fresnelMask);
			return lerp(colour, colour * (0.65 + rainbow * 0.7), amount);
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
			float density = max(density0, density1);
			float thickness = saturate((density - densityThreshold) / max(particleSubsurfaceThickness, 0.0001));
			float thinRegion = 1.0 - thickness;
			float subsurfacePhaseMask = 1.0 - phaseT;
			float subsurfaceBacklight = pow(saturate(dot(-normal, float3(lightDir.xy, 0.0))), max(particleSubsurfacePower, 0.1));
			float subsurfaceThicknessMask = lerp(thickness, thinRegion, particleSubsurfaceEdgeBoost);
			float subsurface = subsurfacePhaseMask * subsurfaceBacklight * subsurfaceThicknessMask * particleSubsurfaceIntensity;
			colour *= 1.0 - edgeT;
			return
				colour * lighting
				+ particleSpecularColor.rgb * specular
				+ particleFresnelColor.rgb * fresnel
				+ particleGlowColor.rgb * directionalGlow
				+ colour * transmission
				+ particleSubsurfaceColor.rgb * subsurface
			;
		}

		bool ResolveMetaball(v2f i, out float alpha, out float phaseT, out float density0, out float density1, out float3 litColour)
		{
			float4 combined = tex2D(CombinedTex, i.uv);
			density0 = Phase0Density(combined);
			density1 = combined.a;
			float density = max(density0, density1);
			alpha = smoothstep(max(densityThreshold - edgeSoftness, 0), densityThreshold + edgeSoftness, density);
			if (alpha <= 0.0001)
			{
				phaseT = 0.0;
				litColour = 0.0;
				return false;
			}

			float phaseRatio = density1 / max(density0 + density1, 0.0001);
			float phaseBoundary = saturate(0.5 + phase0RenderBias * 0.5);
			float phaseDelta = phaseRatio - phaseBoundary;
			float phaseAA = max(0.5 * fwidth(phaseRatio) * max(phaseBlendWidth, 0.0001), 0.00001);
			phaseT = smoothstep(-phaseAA, phaseAA, phaseDelta);

			float data0 = combined.r / max(density0, 0.0001);
			float data1 = combined.b / max(density1, 0.0001);
			float noise = InterleavedGradientNoise(i.vertex.xy);

			if (debugMode != 0)
			{
				if (debugMode == 6)
				{
					float blobWeight = max(density0, 0.0001);
					float3 blobCol = combined.rgb / blobWeight;
					litColour = saturate(lerp(blobCol, float3(0, 0, 0), phaseT));
					return true;
				}

				if (debugMode == 1)
				{
					float4 normalPacked = tex2D(NormalTex, i.uv);
					float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
					float3 encodedNormal = saturate(0.5 + normal / 2.0);
					#if defined(UNITY_COLORSPACE_GAMMA)
						litColour = encodedNormal;
					#else
						litColour = GammaToLinearSpace(encodedNormal);
					#endif
					return true;
				}

				if (debugMode == 2)
				{
					float curvature = data0;
					litColour = tex2D(DebugSignedHeatMap, float2(saturate(0.5 + curvature * 0.5), 0.5)).rgb;
					return true;
				}

				if (debugMode == 3)
				{
					float viscosity = Dither01(lerp(data0, data1, phaseT), noise);
					litColour = tex2D(DebugHeatMap, float2(viscosity, 0.5)).rgb;
					return true;
				}

				if (debugMode == 4)
				{
					float densityVal = Dither01(lerp(data0, data1, phaseT), noise);
					litColour = tex2D(DebugHeatMap, float2(densityVal, 0.5)).rgb;
					return true;
				}

				if (debugMode == 5)
				{
					float tempVal = Dither01(lerp(data0, data1, phaseT), noise);
					litColour = tex2D(DebugHeatMap, float2(tempVal, 0.5)).rgb;
					return true;
				}

				float2 force = float2(data0, data1);
				float2 mapped = saturate(0.5 + force * 0.5);
				float mag = saturate(length(force));
				litColour = float3(mapped, mag);
				return true;
			}

			float4 normalPacked = tex2D(NormalTex, i.uv);
			float3 normal = GetBlendedPhaseNormal(normalPacked, density0, density1, phaseT);
			float refractionMask = smoothstep(0.0, max(metaballRefractionEdgeFade, 0.0001), density - densityThreshold);
			float2 refractedUv = saturate(i.uv - normal.xy * metaballRefractionStrength * refractionMask);
			float4 refractedCombined = tex2D(CombinedTex, refractedUv);
			float refractedData0 = refractedCombined.g > 0.0001 ? refractedCombined.r / refractedCombined.g : data0;
			float refractedData1 = refractedCombined.a > 0.0001 ? refractedCombined.b / refractedCombined.a : data1;
			float3 refractedColour = SampleGradientColour(refractedUv, refractedData0, refractedData1, phaseT, noise);
			litColour = ApplyParticleLighting(refractedColour, i.uv, density0, density1, phaseT);
			litColour = ApplyIridescence(litColour, normal);
			return true;
		}

		float3 ExtractBloom(float3 colour)
		{
			float brightness = max(max(colour.r, colour.g), colour.b);
			float threshold = GammaToLinearSpace(float3(max(customBloomThreshold, 0.0), 0.0, 0.0)).r;
			float knee = threshold * saturate(customBloomSoftKnee) + 0.00001;
			float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
			soft = soft * soft * (0.25 / knee);
			float contribution = max(soft, brightness - threshold);
			float response = max(customBloomResponse, 0.01);
			float shapedContribution = pow(max(contribution, 0.0), response);
			return colour * (shapedContribution / max(brightness, 0.0001));
		}

		float4 frag(v2f i) : SV_Target
		{
			float alpha;
			float phaseT;
			float density0;
			float density1;
			float3 colour;
			if (!ResolveMetaball(i, alpha, phaseT, density0, density1, colour))
			{
				discard;
			}

			return float4(colour, alpha);
		}

		float4 fragBloomExtract(v2f i) : SV_Target
		{
			float alpha;
			float phaseT;
			float density0;
			float density1;
			float3 colour;
			if (debugMode != 0 || !ResolveMetaball(i, alpha, phaseT, density0, density1, colour))
			{
				return 0.0;
			}
			return float4(ExtractBloom(colour) * alpha, 1.0);
		}

		float4 fragBloomComposite(v2f i) : SV_Target
		{
			return float4(tex2D(BloomTex, i.uv).rgb * customBloomIntensity, 1.0);
		}

		float4 DownsampleBox13(float2 uv)
		{
			float2 texel = _MainTex_TexelSize.xy;
			float4 sum = tex2D(_MainTex, uv) * 0.125;
			sum += tex2D(_MainTex, uv + texel * float2(-1, -1)) * 0.0625;
			sum += tex2D(_MainTex, uv + texel * float2( 0, -1)) * 0.125;
			sum += tex2D(_MainTex, uv + texel * float2( 1, -1)) * 0.0625;
			sum += tex2D(_MainTex, uv + texel * float2(-1,  0)) * 0.125;
			sum += tex2D(_MainTex, uv + texel * float2( 1,  0)) * 0.125;
			sum += tex2D(_MainTex, uv + texel * float2(-1,  1)) * 0.0625;
			sum += tex2D(_MainTex, uv + texel * float2( 0,  1)) * 0.125;
			sum += tex2D(_MainTex, uv + texel * float2( 1,  1)) * 0.0625;
			sum += tex2D(_MainTex, uv + texel * float2(-2,  0)) * 0.03125;
			sum += tex2D(_MainTex, uv + texel * float2( 2,  0)) * 0.03125;
			sum += tex2D(_MainTex, uv + texel * float2( 0, -2)) * 0.03125;
			sum += tex2D(_MainTex, uv + texel * float2( 0,  2)) * 0.03125;
			return sum;
		}

		float4 UpsampleTent(float2 uv)
		{
			float2 texel = _MainTex_TexelSize.xy * max(customBloomSampleScale, 0.5);
			float4 sum = tex2D(_MainTex, uv) * 4.0;
			sum += tex2D(_MainTex, uv + texel * float2(-1,  0)) * 2.0;
			sum += tex2D(_MainTex, uv + texel * float2( 1,  0)) * 2.0;
			sum += tex2D(_MainTex, uv + texel * float2( 0, -1)) * 2.0;
			sum += tex2D(_MainTex, uv + texel * float2( 0,  1)) * 2.0;
			sum += tex2D(_MainTex, uv + texel * float2(-1, -1));
			sum += tex2D(_MainTex, uv + texel * float2( 1, -1));
			sum += tex2D(_MainTex, uv + texel * float2(-1,  1));
			sum += tex2D(_MainTex, uv + texel * float2( 1,  1));
			return sum / 16.0;
		}

		float4 fragBloomDownsample(v2f i) : SV_Target
		{
			return DownsampleBox13(i.uv);
		}

		float4 fragBloomUpsample(v2f i) : SV_Target
		{
			return UpsampleTent(i.uv) + tex2D(BloomTex, i.uv);
		}
		ENDCG

		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBloomExtract
			ENDCG
		}

		Pass {
			Blend One One
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBloomComposite
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBloomDownsample
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragBloomUpsample
			ENDCG
		}

	}
}
