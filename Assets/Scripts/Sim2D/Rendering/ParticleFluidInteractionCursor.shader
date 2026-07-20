Shader "Hidden/ParticleFluidInteractionCursor"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_ShadowColor ("Shadow Color", Color) = (0, 0, 0, 0.25)
		_ShadowSoftness ("Shadow Softness", Float) = 0.02
		_UvScale ("UV Scale", Float) = 1
		_Radius ("Radius", Float) = 0.85
		_Thickness ("Thickness", Float) = 0.08
		_CursorWorldRadius ("Cursor World Radius", Float) = 1
		_InnerRadiusFraction ("Inner Radius Fraction", Float) = 0.5
		_InnerRotation ("Inner Rotation", Float) = 0
		_OuterDashCount ("Outer Dash Count", Float) = 48
		_OuterDashFill ("Outer Dash Fill", Float) = 0.55
		_Interaction ("Interaction", Float) = 0
		_CursorFamily ("Cursor Family", Int) = 0
		_ForceTickLength ("Force Tick Length", Float) = 0.15
		_ForceTickThicknessMultiplier ("Force Tick Thickness Multiplier", Float) = 0.9
		_MinSegmentScreenThickness ("Min Segment Screen Thickness", Float) = 2
		_TemperatureWaveCount ("Temperature Wave Count", Float) = 8
		_TemperatureWaveAmplitude ("Temperature Wave Amplitude", Float) = 0.75
		_TemperatureWaveSpeed ("Temperature Wave Speed", Float) = 2
		_TemperatureWaveAmplitudeMultiplier ("Temperature Wave Amplitude Multiplier", Float) = 1
	}
	SubShader
	{
		Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Blend SrcAlpha OneMinusSrcAlpha
			Cull Off
			ZWrite Off
			ZTest Always

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			float4 _Color;
			float4 _ShadowColor;
			float _ShadowSoftness;
			float _UvScale;
			float _Radius;
			float _Thickness;
			float _CursorWorldRadius;
			float _InnerRadiusFraction;
			float _InnerRotation;
			float _OuterDashCount;
			float _OuterDashFill;
			float _Interaction;
			int _CursorFamily;
			float _ForceTickLength;
			float _ForceTickThicknessMultiplier;
			float _MinSegmentScreenThickness;
			float _TemperatureWaveCount;
			float _TemperatureWaveAmplitude;
			float _TemperatureWaveSpeed;
			float _TemperatureWaveAmplitudeMultiplier;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = TransformObjectToHClip(v.vertex.xyz);
				o.uv = v.uv;
				return o;
			}

			float StrokeThickness()
			{
				return max(_Thickness * _Radius / max(_CursorWorldRadius, 0.0001), 0.0001);
			}

			float RingMask(float dist, float radius)
			{
				float halfThickness = StrokeThickness() * 0.5;
				float aa = max(fwidth(dist), 0.0001);
				return 1.0 - smoothstep(halfThickness - aa, halfThickness + aa, abs(dist - radius));
			}

			float2 Rotate(float2 p, float angle)
			{
				float s = sin(angle);
				float c = cos(angle);
				return float2(c * p.x - s * p.y, s * p.x + c * p.y);
			}

			float SegmentMask(float2 p, float2 a, float2 b, float thickness)
			{
				float2 ab = b - a;
				float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 0.0001));
				float dist = length(p - (a + ab * t));
				float pixelSize = max(length(ddx(p)), length(ddy(p)));
				float aa = max(max(fwidth(dist), pixelSize), 0.0001);
				float minVisibleThickness = max(_MinSegmentScreenThickness * pixelSize - 2.0 * aa, 0.0001);
				float stableThickness = max(thickness, minVisibleThickness);
				return 1.0 - smoothstep(stableThickness * 0.5 - aa, stableThickness * 0.5 + aa, dist);
			}

			float SoftSegmentShadowMask(float2 p, float2 a, float2 b, float thickness)
			{
				float2 ab = b - a;
				float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 0.0001));
				float dist = length(p - (a + ab * t));
				float halfThickness = thickness * 0.5;
				float aa = max(fwidth(dist), 0.0001);
				float softness = max(_ShadowSoftness, aa);
				return 1.0 - smoothstep(halfThickness, halfThickness + softness, dist);
			}

			float ForceReticleMask(float2 p)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float halfLength = max(_ForceTickLength * 0.5, 0.0001);
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float mask = 0.0;
				mask = max(mask, SegmentMask(p, float2(centerRadius - halfLength, 0.0), float2(centerRadius + halfLength, 0.0), thickness));
				mask = max(mask, SegmentMask(p, float2(-centerRadius - halfLength, 0.0), float2(-centerRadius + halfLength, 0.0), thickness));
				mask = max(mask, SegmentMask(p, float2(0.0, centerRadius - halfLength), float2(0.0, centerRadius + halfLength), thickness));
				mask = max(mask, SegmentMask(p, float2(0.0, -centerRadius - halfLength), float2(0.0, -centerRadius + halfLength), thickness));
				return mask;
			}

			float RadialTickMask(float2 p, float2 axis, float centerRadius, float halfLength, float thickness)
			{
				return SegmentMask(p, axis * (centerRadius - halfLength), axis * (centerRadius + halfLength), thickness);
			}

			float LightingReticleMask(float2 p)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float halfLength = max(_ForceTickLength * 0.5, 0.0001);
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float diagonal = 0.70710678;
				float mask = 0.0;
				mask = max(mask, RadialTickMask(p, float2(1.0, 0.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(-1.0, 0.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(0.0, 1.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(0.0, -1.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(diagonal, diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(-diagonal, diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(diagonal, -diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickMask(p, float2(-diagonal, -diagonal), centerRadius, halfLength, thickness));
				return mask;
			}

			float ForceChevron(float2 p, float2 axis, bool outward)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float length = max(_ForceTickLength, 0.0001);
				float wingLength = length * 0.65;
				float wingSpread = length * 0.45;
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float2 direction = outward ? axis : -axis;
				float2 tangent = float2(-axis.y, axis.x);
				float2 tip = axis * centerRadius + direction * length * 0.45;
				float2 baseCenter = tip - direction * wingLength;
				float mask = 0.0;
				mask = max(mask, SegmentMask(p, tip, baseCenter + tangent * wingSpread, thickness));
				mask = max(mask, SegmentMask(p, tip, baseCenter - tangent * wingSpread, thickness));
				return mask;
			}

			float ForceReticleShadowMask(float2 p)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float halfLength = max(_ForceTickLength * 0.5, 0.0001);
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float mask = 0.0;
				mask = max(mask, SoftSegmentShadowMask(p, float2(centerRadius - halfLength, 0.0), float2(centerRadius + halfLength, 0.0), thickness));
				mask = max(mask, SoftSegmentShadowMask(p, float2(-centerRadius - halfLength, 0.0), float2(-centerRadius + halfLength, 0.0), thickness));
				mask = max(mask, SoftSegmentShadowMask(p, float2(0.0, centerRadius - halfLength), float2(0.0, centerRadius + halfLength), thickness));
				mask = max(mask, SoftSegmentShadowMask(p, float2(0.0, -centerRadius - halfLength), float2(0.0, -centerRadius + halfLength), thickness));
				return mask;
			}

			float RadialTickShadowMask(float2 p, float2 axis, float centerRadius, float halfLength, float thickness)
			{
				return SoftSegmentShadowMask(p, axis * (centerRadius - halfLength), axis * (centerRadius + halfLength), thickness);
			}

			float LightingReticleShadowMask(float2 p)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float halfLength = max(_ForceTickLength * 0.5, 0.0001);
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float diagonal = 0.70710678;
				float mask = 0.0;
				mask = max(mask, RadialTickShadowMask(p, float2(1.0, 0.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(-1.0, 0.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(0.0, 1.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(0.0, -1.0), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(diagonal, diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(-diagonal, diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(diagonal, -diagonal), centerRadius, halfLength, thickness));
				mask = max(mask, RadialTickShadowMask(p, float2(-diagonal, -diagonal), centerRadius, halfLength, thickness));
				return mask;
			}

			float ForceChevronShadow(float2 p, float2 axis, bool outward)
			{
				float centerRadius = _Radius * saturate(_InnerRadiusFraction);
				float length = max(_ForceTickLength, 0.0001);
				float wingLength = length * 0.65;
				float wingSpread = length * 0.45;
				float thickness = max(StrokeThickness() * _ForceTickThicknessMultiplier, 0.0001);
				float2 direction = outward ? axis : -axis;
				float2 tangent = float2(-axis.y, axis.x);
				float2 tip = axis * centerRadius + direction * length * 0.45;
				float2 baseCenter = tip - direction * wingLength;
				float mask = 0.0;
				mask = max(mask, SoftSegmentShadowMask(p, tip, baseCenter + tangent * wingSpread, thickness));
				mask = max(mask, SoftSegmentShadowMask(p, tip, baseCenter - tangent * wingSpread, thickness));
				return mask;
			}

			float ForceChevronShadowMask(float2 p, bool outward)
			{
				float mask = 0.0;
				mask = max(mask, ForceChevronShadow(p, float2(1.0, 0.0), outward));
				mask = max(mask, ForceChevronShadow(p, float2(0.0, 1.0), outward));
				mask = max(mask, ForceChevronShadow(p, float2(-1.0, 0.0), outward));
				mask = max(mask, ForceChevronShadow(p, float2(0.0, -1.0), outward));
				return mask;
			}

			float ForceChevronMask(float2 p, bool outward)
			{
				float mask = 0.0;
				mask = max(mask, ForceChevron(p, float2(1.0, 0.0), outward));
				mask = max(mask, ForceChevron(p, float2(0.0, 1.0), outward));
				mask = max(mask, ForceChevron(p, float2(-1.0, 0.0), outward));
				mask = max(mask, ForceChevron(p, float2(0.0, -1.0), outward));
				return mask;
			}

			float TemperatureReticleMask(float2 p, float dist)
			{
				float angle = atan2(p.y, p.x);
				float wavePhase = sin(_Time.y * _TemperatureWaveSpeed);
				float innerRadius = _Radius * saturate(_InnerRadiusFraction);
				float wave = sin(angle * _TemperatureWaveCount) * innerRadius * _TemperatureWaveAmplitude * _TemperatureWaveAmplitudeMultiplier;
				float radius = innerRadius + wave;
				return RingMask(dist, radius);
			}

			float InnerReticleMask(float2 p, float dist)
			{
				// Force
				if (_CursorFamily == 1)
				{
					if (abs(_Interaction) > 0.01)
					{
						return ForceChevronMask(p, _Interaction < -0.01);
					}

					return ForceReticleMask(p);
				}

				//temperature
				if (_CursorFamily == 2)
				{
					return TemperatureReticleMask(p, dist);
				}

				//light
				if (_CursorFamily == 3)
				{
					return LightingReticleMask(p);
				}

				return RingMask(dist, _Radius * saturate(_InnerRadiusFraction));
			}

			float DashMask(float2 p)
			{
				float angle = atan2(p.y, p.x) / 6.28318530718 + 0.5;
				float dashCoord = angle * max(_OuterDashCount, 1.0);
				float cellDistanceFromCenter = abs(frac(dashCoord) - 0.5);
				float halfFill = saturate(_OuterDashFill) * 0.5;
				float edgeAA = max(fwidth(dashCoord) * 0.5, 0.001);
				return 1.0 - smoothstep(halfFill - edgeAA, halfFill + edgeAA, cellDistanceFromCenter);
			}

			float ShadowDashMask(float2 p)
			{
				float angle = atan2(p.y, p.x) / 6.28318530718 + 0.5;
				float dashCount = max(_OuterDashCount, 1.0);
				float dashCoord = angle * dashCount;
				float cellDistanceFromCenter = abs(frac(dashCoord) - 0.5);
				float halfFill = saturate(_OuterDashFill) * 0.5;
				float radius = max(length(p), 0.001);
				float angularSoftness = _ShadowSoftness * dashCount / (6.28318530718 * radius);
				float edgeSoftness = max(fwidth(dashCoord) * 0.5 + angularSoftness, 0.001);
				return 1.0 - smoothstep(halfFill - edgeSoftness, halfFill + edgeSoftness, cellDistanceFromCenter);
			}

			float ReticleMask(float2 p)
			{
				float dist = length(p);
				float outerRing = RingMask(dist, _Radius) * DashMask(p);
				float2 innerP = Rotate(p, _InnerRotation);
				float innerRing = InnerReticleMask(innerP, dist);
				return saturate(max(outerRing, innerRing));
			}

			float SoftRingShadowMask(float dist, float radius)
			{
				float halfThickness = StrokeThickness() * 0.5;
				float aa = max(fwidth(dist), 0.0001);
				float softness = max(_ShadowSoftness, aa);
				return 1.0 - smoothstep(halfThickness, halfThickness + softness, abs(dist - radius));
			}

			float CircleShadowMask(float2 p)
			{
				float dist = length(p);
				float outerShadow = SoftRingShadowMask(dist, _Radius) * ShadowDashMask(p);
				float2 innerP = Rotate(p, _InnerRotation);
				if (_CursorFamily == 1)
				{
					float forceShadow = abs(_Interaction) > 0.01
						? ForceChevronShadowMask(innerP, _Interaction < -0.01)
						: ForceReticleShadowMask(innerP);
					return saturate(max(outerShadow, forceShadow));
				}

				if (_CursorFamily == 3)
				{
					return saturate(max(outerShadow, LightingReticleShadowMask(innerP)));
				}

				float innerRadius = _Radius * saturate(_InnerRadiusFraction);
				if (_CursorFamily == 2)
				{
					float angle = atan2(innerP.y, innerP.x);
					float wavePhase = sin(_Time.y * _TemperatureWaveSpeed);
					innerRadius += sin(angle * _TemperatureWaveCount) * innerRadius * _TemperatureWaveAmplitude * _TemperatureWaveAmplitudeMultiplier;
				}
				float innerShadow = SoftRingShadowMask(dist, innerRadius);
				return saturate(max(outerShadow, innerShadow));
			}

			float4 frag(v2f i) : SV_Target
			{
				float2 p = (i.uv * 2.0 - 1.0) * _UvScale;
				float mainAlpha = ReticleMask(p) * _Color.a;
				if (_ShadowColor.a <= 0.0 || _ShadowSoftness <= 0.0)
				{
					return float4(_Color.rgb, mainAlpha);
				}

				float shadowAlpha = CircleShadowMask(p) * _ShadowColor.a * (1.0 - mainAlpha);
				float alpha = mainAlpha + shadowAlpha;
				float3 rgb = alpha > 0.0001
					? (_Color.rgb * mainAlpha + _ShadowColor.rgb * shadowAlpha) / alpha
					: 0.0;
				return float4(rgb, alpha);
			}
			ENDHLSL
		}
	}
}
