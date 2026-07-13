using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	[DisallowMultipleComponent]
	public abstract class ParticleFluidLight2D : MonoBehaviour
	{
		[Tooltip("6500 -> neutral daylight; 2700 -> incandescent light")]
		[Range(1000f, 20000f)] public float temperatureKelvin = 6500f;
		[ColorUsage(false, true)] public Color color = Color.white;
		[Min(0f)] public float intensity = 0.45f;
		[Min(0f)] public float sampleBias = 1f;

		public Color EffectiveColor => Mathf.CorrelatedColorTemperatureToRGB(temperatureKelvin) * color;

		public bool SupportsCausticRaymarch()
		{
			return isActiveAndEnabled && intensity > 0f && sampleBias > 0f;
		}

		public float GetCausticExposure()
		{
			return Mathf.Max(0f, Luminance(EffectiveColor) * intensity);

			static float Luminance(Color color)
			{
				return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
			}
		}

		public float GetCausticSampleWeight()
		{
			if (!SupportsCausticRaymarch())
			{
				return 0f;
			}

			return Mathf.Max(sampleBias, 0f);
		}

		public Vector4 GetCausticMultiplier()
		{
			Color effectiveColor = EffectiveColor;
			float clampedIntensity = Mathf.Max(intensity, 0f);
			return new Vector4(effectiveColor.r * clampedIntensity, effectiveColor.g * clampedIntensity, effectiveColor.b * clampedIntensity, 0f);
		}

		protected virtual void OnDrawGizmos()
		{
			Gizmos.color = EffectiveColor;
			DrawLightGizmos();
		}

		protected abstract void DrawLightGizmos();
	}
}
