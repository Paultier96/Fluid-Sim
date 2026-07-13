using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seb.Helpers
{
	public static class QuadGenerator
	{
		public static Mesh GenerateQuadMesh()
		{
			Mesh mesh = new Mesh();
			mesh.SetVertices(new Vector3[] {
				new (-0.5f, 0.5f),
				new (0.5f, 0.5f),
				new (-0.5f, -0.5f),
				new (0.5f, -0.5f)
			});
			mesh.SetTriangles(new[] { 0, 1, 2, 2, 1, 3 }, 0, true);
			mesh.SetUVs(0, new Vector2[] {
				new (0, 1),
				new (1, 1),
				new (0, 0),
				new (1, 0)
			});
			return mesh;
		}
	}
}