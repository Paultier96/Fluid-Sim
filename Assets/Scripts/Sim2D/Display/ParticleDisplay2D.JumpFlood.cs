using System;
using UnityEngine;

namespace Seb.Fluid2D.Rendering
{
	public partial class ParticleDisplay2D
	{
		[Header("Jump Flood")]
		public JumpFloodSettings jumpFlood = new JumpFloodSettings();

		JumpFloodRenderer2D jumpFloodRenderer;

		[Serializable]
		public sealed class JumpFloodSettings
		{
			public ComputeShader computeShader;
			public Shader displayShader;
		}
	}
}
