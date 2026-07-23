using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public sealed class ParticleFluidPhaseLookPresetController
	{
		private readonly ParticleFluidLighting2D _lighting;

		public ParticleFluidPhaseLookPresetController(ParticleFluidLighting2D lighting)
		{
			_lighting = lighting;
		}

		public void OnEnable()
		{
			ParticleFluidPhaseLookPreset.Changed += OnPresetChanged;
			if (_lighting.applyPhaseLookPresetOnEnable)
			{
				Apply();
			}
		}

		public void OnDisable()
		{
			ParticleFluidPhaseLookPreset.Changed -= OnPresetChanged;
		}

		public void OnValidate()
		{
			if (_lighting.applyPhaseLookPresetOnValidate)
			{
				Apply();
			}
		}

		public void Apply()
		{
			ParticleFluidPhaseLookPreset preset = _lighting.phaseLookPreset;
			if (preset == null)
			{
				return;
			}

			CopyPhaseMaterialSettings(preset.phase0Material, _lighting.phase0Material);
			CopyPhaseMaterialSettings(preset.phase1Material, _lighting.phase1Material);
			CopyPhaseMaterialSettings(preset.boundaryMaterial, _lighting.boundaryMaterial);
			if (_lighting.gaussianSss != null)
			{
				_lighting.gaussianSss.gaussianDiffuseScatterStrength = preset.gaussianDiffuseScatterStrength;
				_lighting.gaussianSss.gaussianDiffuseRadius = preset.gaussianDiffuseRadius;
			}

			_lighting.SyncMaterialSlots();

			ParticleDisplay2D display = _lighting.display;
			if (display != null)
			{
				display.SetPhaseColourMaps(CloneGradient(preset.phase0ColourMap), CloneGradient(preset.phase1ColourMap));
			}
		}

		private void OnPresetChanged(ParticleFluidPhaseLookPreset changedPreset)
		{
			if (!_lighting.applyPhaseLookPresetOnValidate || changedPreset != _lighting.phaseLookPreset)
			{
				return;
			}

			Apply();
		}

		private static void CopyPhaseMaterialSettings(ParticleFluidLighting2D.PhaseMaterialSettings source, ParticleFluidLighting2D.PhaseMaterialSettings destination)
		{
			if (source == null || destination == null)
			{
				return;
			}

			destination.indexOfRefraction = source.indexOfRefraction;
			destination.absorption = source.absorption;
			destination.absorptionDiffuseTintBlend = source.absorptionDiffuseTintBlend;
			destination.reflectance = source.reflectance;
			destination.roughness = source.roughness;
			destination.metallic = source.metallic;
			destination.screenSpaceReflectionStrength = source.screenSpaceReflectionStrength;
			destination.diffuseLightTint = source.diffuseLightTint;
			destination.causticAdditiveBlend = source.causticAdditiveBlend;
			destination.diffuseAdditiveBlend = source.diffuseAdditiveBlend;
			destination.diffuseNormalInfluence = source.diffuseNormalInfluence;
		}

		private static Gradient CloneGradient(Gradient source)
		{
			if (source == null)
			{
				return null;
			}

			Gradient clone = new Gradient();
			clone.SetKeys(source.colorKeys, source.alphaKeys);
			clone.mode = source.mode;
			return clone;
		}
	}
}
