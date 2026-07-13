Shader "Hidden/ParticleFluidInteractionCursor"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_Radius ("Radius", Float) = 0.85
		_Thickness ("Thickness", Float) = 0.08
		_CursorWorldRadius ("Cursor World Radius", Float) = 1
		_InnerRadiusFraction ("Inner Radius Fraction", Float) = 0.5
		_OuterDashCount ("Outer Dash Count", Float) = 48
		_OuterDashFill ("Outer Dash Fill", Float) = 0.55
		_ArrowRadiusFraction ("Arrow Radius Fraction", Float) = 0.68
		_ArrowLength ("Arrow Length", Float) = 0.2
		_ArrowWidth ("Arrow Width", Float) = 0.1
		_Mode ("Mode", Int) = 0
		_CursorFamily ("Cursor Family", Int) = 0
		_ForceTickLength ("Force Tick Length", Float) = 0.15
		_ForceTickThicknessMultiplier ("Force Tick Thickness Multiplier", Float) = 0.9
		_TemperatureWaveCount ("Temperature Wave Count", Float) = 8
		_TemperatureWaveAmplitude ("Temperature Wave Amplitude", Float) = 0.75
		_TemperatureWaveSpeed ("Temperature Wave Speed", Float) = 2
		_TemperatureWaveAmplitudeMultiplier ("Temperature Wave Amplitude Multiplier", Float) = 1
	}
	SubShader
	{
		Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
		Pass
		{
			Blend SrcAlpha OneMinusSrcAlpha
			Cull Off
			ZWrite Off
			ZTest Always

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float4 _Color;
			float _Radius;
			float _Thickness;
			float _CursorWorldRadius;
			float _InnerRadiusFraction;
			float _OuterDashCount;
			float _OuterDashFill;
			float _ArrowRadiusFraction;
			float _ArrowLength;
			float _ArrowWidth;
			int _Mode;
			int _CursorFamily;
			float _ForceTickLength;
			float _ForceTickThicknessMultiplier;
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
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				return o;
			}

			float Sign(float2 p1, float2 p2, float2 p3)
			{
				return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
			}

			float TriangleMask(float2 p, float2 a, float2 b, float2 c)
			{
				float d1 = Sign(p, a, b);
				float d2 = Sign(p, b, c);
				float d3 = Sign(p, c, a);
				bool hasNeg = (d1 < 0.0) || (d2 < 0.0) || (d3 < 0.0);
				bool hasPos = (d1 > 0.0) || (d2 > 0.0) || (d3 > 0.0);
				return !(hasNeg && hasPos) ? 1.0 : 0.0;
			}

			float ArrowMask(float2 p, float2 axis, bool outward)
			{
				float2 dir = outward ? axis : -axis;
				float2 tangent = float2(-axis.y, axis.x);
				float2 center = axis * _Radius * _ArrowRadiusFraction;
				float2 tip = center + dir * _ArrowLength * 0.5;
				float2 baseCenter = center - dir * _ArrowLength * 0.5;
				float2 baseA = baseCenter + tangent * _ArrowWidth * 0.5;
				float2 baseB = baseCenter - tangent * _ArrowWidth * 0.5;
				return TriangleMask(p, tip, baseA, baseB);
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

			float SegmentMask(float2 p, float2 a, float2 b, float thickness)
			{
				float2 ab = b - a;
				float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 0.0001));
				float dist = length(p - (a + ab * t));
				float aa = max(fwidth(dist), 0.0001);
				return 1.0 - smoothstep(thickness * 0.5 - aa, thickness * 0.5 + aa, dist);
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
				float wavePhase = _Time.y * _TemperatureWaveSpeed;
				float wave = sin(angle * _TemperatureWaveCount + wavePhase) * StrokeThickness() * _TemperatureWaveAmplitude * _TemperatureWaveAmplitudeMultiplier;
				float radius = _Radius * saturate(_InnerRadiusFraction) + wave;
				return RingMask(dist, radius);
			}

			float InnerReticleMask(float2 p, float dist)
			{
				if (_CursorFamily == 1)
				{
					if (_Mode == 1 || _Mode == 2)
					{
						return ForceChevronMask(p, _Mode == 2);
					}

					return ForceReticleMask(p);
				}

				if (_CursorFamily == 2)
				{
					return TemperatureReticleMask(p, dist);
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

			float4 frag(v2f i) : SV_Target
			{
				float2 p = i.uv * 2.0 - 1.0;
				float dist = length(p);
				float outerRing = RingMask(dist, _Radius) * DashMask(p);
				float innerRing = InnerReticleMask(p, dist);
				float ring = max(outerRing, innerRing);

				float arrows = 0.0;
				if (_CursorFamily != 1 && (_Mode == 1 || _Mode == 2))
				{
					bool outward = _Mode == 2;
					arrows = max(arrows, ArrowMask(p, float2(1.0, 0.0), outward));
					arrows = max(arrows, ArrowMask(p, float2(0.0, 1.0), outward));
					arrows = max(arrows, ArrowMask(p, float2(-1.0, 0.0), outward));
					arrows = max(arrows, ArrowMask(p, float2(0.0, -1.0), outward));
				}

				float alpha = saturate(max(ring, arrows)) * _Color.a;
				return float4(_Color.rgb, alpha);
			}
			ENDCG
		}
	}
}
