using System;
using Seb.Fluid2D.Simulation;
using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seb.Fluid2D.Rendering
{
	[Serializable]
	public sealed class ParticleFluidVectorFieldRenderer2D
	{
		private static readonly int VectorScale = Shader.PropertyToID("vectorScale");
		private static readonly int VectorMaxMagnitude = Shader.PropertyToID("vectorMaxMagnitude");
		private static readonly int VectorUseLogScale = Shader.PropertyToID("vectorUseLogScale");
		private static readonly int VectorLogScaleStrength = Shader.PropertyToID("vectorLogScaleStrength");
		private static readonly int VectorWidth = Shader.PropertyToID("vectorWidth");
		private static readonly int VectorUseSignedColor = Shader.PropertyToID("vectorUseSignedColor");
		private static readonly int DebugVectorData = Shader.PropertyToID("DebugVectorData");
		private static readonly int DebugVectorSign = Shader.PropertyToID("DebugVectorSign");

		public enum VectorFieldSource
		{
			None = 0,
			SurfaceTensionForce = 1,
			NonCoalescenceForces = 2,
			Velocity = 3,
			CurvatureNormal = 4,
			Convection = 5,
		}

		[Header("Vector Field Debug")]
		public Shader shader;
		public VectorFieldSource source = VectorFieldSource.SurfaceTensionForce;
		[Min(0f)] public float scale = 0.25f;
		[Min(0.0001f)] public float maxMagnitude = 1.0f;
		public bool useLogScale = true;
		[Min(1f)] public float logScaleStrength = 10.0f;
		[Min(0f)] public float width = 0.035f;

		[NonSerialized] private Material _material;
		[NonSerialized] private Mesh _arrowMesh;
		[NonSerialized] private ComputeBuffer _argsBuffer;

		public int ComputeMode
		{
			get
			{
				return source switch
				{
					VectorFieldSource.SurfaceTensionForce => 1,
					VectorFieldSource.NonCoalescenceForces => 2,
					VectorFieldSource.CurvatureNormal => 3,
					VectorFieldSource.Convection => 4,
					_ => 0,
				};
			}
		}

		internal void Prepare(ParticleDisplay2D display)
		{
			EnsureResources(display);
			if (_material == null)
			{
				return;
			}

			display.BindSimulationBuffers(_material);
			ApplySettings(display);
		}

		internal void Draw(ParticleDisplay2D display, Camera camera)
		{
			if (!ShouldDraw(display))
			{
				return;
			}

			Graphics.DrawMeshInstancedIndirect(
				_arrowMesh,
				0,
				_material,
				new Bounds(Vector3.zero, Vector3.one * 10000),
				_argsBuffer,
				0,
				null,
				ShadowCastingMode.Off,
				false,
				display.gameObject.layer,
				camera
			);
		}

		internal void AppendDraw(ParticleDisplay2D display, CommandBuffer commandBuffer)
		{
			if (!ShouldDraw(display))
			{
				return;
			}

			commandBuffer.DrawMeshInstancedIndirect(_arrowMesh, 0, _material, 0, _argsBuffer);
		}

		internal void Release()
		{
			ComputeHelper.Release(_argsBuffer);
			_argsBuffer = null;

			if (_material != null)
			{
				UnityEngine.Object.DestroyImmediate(_material);
				_material = null;
			}

			if (_arrowMesh != null)
			{
				UnityEngine.Object.DestroyImmediate(_arrowMesh);
				_arrowMesh = null;
			}
		}

		private void EnsureResources(ParticleDisplay2D display)
		{
			ParticleFluidRenderUtils.EnsureMaterial(ref _material, shader);
			EnsureArrowMesh();
			if (_arrowMesh != null && display.sim.resources.positionBuffer != null)
			{
				ComputeHelper.CreateArgsBuffer(ref _argsBuffer, _arrowMesh, display.sim.resources.positionBuffer.count);
			}
		}

		private void ApplySettings(ParticleDisplay2D display)
		{
			FluidSim2D sim = display.sim;
			_material.SetFloat(VectorScale, scale);
			_material.SetFloat(VectorMaxMagnitude, EffectiveMaxMagnitude(sim));
			_material.SetInt(VectorUseLogScale, useLogScale ? 1 : 0);
			_material.SetFloat(VectorLogScaleStrength, logScaleStrength);
			_material.SetFloat(VectorWidth, width);
			_material.SetInt(VectorUseSignedColor, source == VectorFieldSource.CurvatureNormal ? 1 : 0);
			_material.SetBuffer(DebugVectorData, GetVectorFieldBuffer(sim));
			_material.SetBuffer(DebugVectorSign, sim.resources.debugVectorSignBuffer);
		}

		private bool ShouldDraw(ParticleDisplay2D display)
		{
			return source != VectorFieldSource.None
			       && GetVectorFieldBuffer(display.sim) != null
			       && _material != null
			       && _arrowMesh != null
			       && _argsBuffer != null;
		}

		private float EffectiveMaxMagnitude(FluidSim2D sim)
		{
			return source switch
			{
				VectorFieldSource.CurvatureNormal => sim.maxSurfaceTensionCurvature,
				VectorFieldSource.SurfaceTensionForce => Mathf.Max(Mathf.Max(Mathf.Abs(sim.surfaceTension), Mathf.Abs(sim.blobBlobSurfaceTension), Mathf.Abs(sim.blobSelfSurfaceTension)) * sim.maxSurfaceTensionCurvature, 0.0001f),
				VectorFieldSource.Convection => Mathf.Max(Mathf.Abs(sim.gravity) * sim.buoyancyInversionStrength * Mathf.Max(0f, sim.buoyancyInversionClamp), 0.0001f),
				VectorFieldSource.NonCoalescenceForces => sim.carrierWedgeMaxAcceleration > 0
					? Mathf.Max(sim.carrierWedgeMaxAcceleration, maxMagnitude)
					: Mathf.Max(0.0001f, Mathf.Abs(sim.carrierWedgeStrength), maxMagnitude),
				_ => Mathf.Max(0.0001f, maxMagnitude),
			};
		}

		private ComputeBuffer GetVectorFieldBuffer(FluidSim2D sim)
		{
			return source == VectorFieldSource.Velocity ? sim.resources.velocityBuffer : sim.resources.debugVectorDataBuffer;
		}

		private void EnsureArrowMesh()
		{
			if (_arrowMesh != null)
			{
				return;
			}

			_arrowMesh = new Mesh
			{
				name = "Sim2D Vector Arrow",
				vertices = new[]
				{
					new Vector3(0f, -0.5f, 0f),
					new Vector3(0.62f, -0.5f, 0f),
					new Vector3(0.62f, -1f, 0f),
					new Vector3(1f, 0f, 0f),
					new Vector3(0.62f, 1f, 0f),
					new Vector3(0.62f, 0.5f, 0f),
					new Vector3(0f, 0.5f, 0f),
				},
				triangles = new[]
				{
					0, 1, 6,
					1, 5, 6,
					1, 2, 3,
					1, 3, 5,
					3, 4, 5,
				}
			};
			_arrowMesh.RecalculateBounds();
		}
	}
}
