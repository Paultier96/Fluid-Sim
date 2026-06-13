using System;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleDisplay2D
	{
		[Header("Jump Flood")]
		public JumpFloodSettings jumpFlood = new JumpFloodSettings();

		JumpFloodRenderer2D jumpFloodRenderer;
		internal JumpFloodRenderer2D JumpFloodRenderer => jumpFloodRenderer ??= new JumpFloodRenderer2D();

		[Serializable]
		public sealed class JumpFloodSettings
		{
			public ComputeShader computeShader;
			public Shader displayShader;
			[Tooltip("Shader that converts the Jump Flood result into the albedo/normal material maps consumed by ParticleFluidLighting2D.")]
			public Shader materialShader;
		}
	}
}
