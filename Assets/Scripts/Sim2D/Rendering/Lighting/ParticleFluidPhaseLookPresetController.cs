namespace Seb.Fluid2D.Rendering
{
	internal sealed class ParticleFluidPhaseLookPresetController
	{
		readonly ParticleFluidLighting2D _lighting;

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
			_lighting.gaussianSss?.ApplyPhaseLookPreset(preset);
			_lighting.SyncMaterialSlots();

			ParticleDisplay2D display = _lighting.Display;
			if (display != null)
			{
				display.SetPhaseColourMaps(preset.phase0ColourMap, preset.phase1ColourMap);
			}
		}

		void OnPresetChanged(ParticleFluidPhaseLookPreset changedPreset)
		{
			if (!_lighting.applyPhaseLookPresetOnValidate || changedPreset != _lighting.phaseLookPreset)
			{
				return;
			}

			Apply();
		}

		static void CopyPhaseMaterialSettings(ParticleFluidLighting2D.PhaseMaterialSettings source, ParticleFluidLighting2D.PhaseMaterialSettings destination)
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
	}
}
