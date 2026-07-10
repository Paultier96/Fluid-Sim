using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	[CreateAssetMenu(fileName = "ParticleFluidPhaseLookPreset", menuName = "Fluid Sim/Phase Look Preset")]
	public sealed class ParticleFluidPhaseLookPreset : ScriptableObject
	{
		public static event System.Action<ParticleFluidPhaseLookPreset> Changed;

		[Min(0f)] public float gaussianDiffuseScatterStrength = 0f;
		[Min(0f)] public float gaussianDiffuseRadius = 24f;
		public ParticleFluidLighting2D.PhaseMaterialSettings phase0Material = new();
		public ParticleFluidLighting2D.PhaseMaterialSettings phase1Material = new();
		public ParticleFluidLighting2D.PhaseMaterialSettings boundaryMaterial = new();
		public Gradient phase0ColourMap;
		public Gradient phase1ColourMap;

		void OnValidate()
		{
			Changed?.Invoke(this);
		}
	}
}

