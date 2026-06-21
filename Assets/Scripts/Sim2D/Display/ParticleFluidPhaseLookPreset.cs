using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	[CreateAssetMenu(fileName = "ParticleFluidPhaseLookPreset", menuName = "Fluid Sim/Phase Look Preset")]
	public sealed class ParticleFluidPhaseLookPreset : ScriptableObject
	{
		public static event System.Action<ParticleFluidPhaseLookPreset> Changed;

		public ParticleFluidLighting2D.PhaseMaterialSettings phase0Material = new(1.442f);
		public ParticleFluidLighting2D.PhaseMaterialSettings phase1Material = new(1.333f);
		public ParticleFluidLighting2D.PhaseMaterialSettings boundaryMaterial = new(1.516f);
		public Gradient phase0ColourMap;
		public Gradient phase1ColourMap;

		void OnValidate()
		{
			Changed?.Invoke(this);
		}
	}
}
