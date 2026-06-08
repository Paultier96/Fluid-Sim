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
		#include "Assets/Scripts/Sim2D/Display/Particle2D-MetaballCompositeCommon.hlsl"
		#include "Assets/Scripts/Sim2D/Display/Particle2D-MetaballCompositeLighting.hlsl"
		#include "Assets/Scripts/Sim2D/Display/Particle2D-MetaballCompositeResolve.hlsl"
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
			#pragma fragment fragCausticTemporal
			ENDCG
		}

		Pass {
			Blend One Zero
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragCausticMotionDilate
			ENDCG
		}
	}
}
