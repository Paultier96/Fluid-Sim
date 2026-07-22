using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[Serializable]
	public sealed class ParticleFluidDirectParticleRenderer2D
	{
		private static readonly int GradientAtlas = Shader.PropertyToID("GradientAtlas");

		[Header("Direct Particles")]
		public Shader shader;

		[NonSerialized] private Material _material;

		internal void Prepare(ParticleDisplay2D display)
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _material, shader);
			if (_material == null)
			{
				return;
			}

			display.BindSimulationBuffers(_material);
			display.ApplyCommonParticleSettings(_material);
			_material.SetTexture(GradientAtlas, display.gradientAtlasTexture != null ? display.gradientAtlasTexture : Texture2D.blackTexture);
		}

		internal void Draw(ParticleDisplay2D display, Camera camera)
		{
			if (_material == null || display.particleMesh == null || display.argsBuffer == null)
			{
				return;
			}

			Graphics.DrawMeshInstancedIndirect(
				display.particleMesh,
				0,
				_material,
				new Bounds(Vector3.zero, Vector3.one * 10000),
				display.argsBuffer,
				0,
				null,
				ShadowCastingMode.Off,
				false,
				display.gameObject.layer,
				camera
			);
		}

		internal void Release()
		{
			ParticleFluidRenderUtils.DestroyMaterial(ref _material);
		}
	}
}
