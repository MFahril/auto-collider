using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor.Tests
{
    /// <summary>Procedural triangle meshes with known geometry for optimization tests.</summary>
    internal static class TestMeshes
    {
        /// <summary>Closed UV sphere (shared vertices, poles included).</summary>
        public static MeshGeometry UvSphere(float radius, int latitude, int longitude, float3 center = default)
        {
            var vertices = new List<float3> { center + new float3(0, radius, 0) };
            for (var i = 1; i < latitude; i++)
            {
                var theta = math.PI * i / latitude;
                for (var j = 0; j < longitude; j++)
                {
                    var phi = 2 * math.PI * j / longitude;
                    vertices.Add(center + radius * new float3(math.sin(theta) * math.cos(phi), math.cos(theta), math.sin(theta) * math.sin(phi)));
                }
            }
            vertices.Add(center - new float3(0, radius, 0));

            var triangles = new List<int>();
            int Ring(int i, int j) => 1 + (i - 1) * longitude + (j % longitude);
            var bottom = vertices.Count - 1;
            for (var j = 0; j < longitude; j++)
            {
                triangles.AddRange(new[] { 0, Ring(1, j + 1), Ring(1, j) });
                triangles.AddRange(new[] { bottom, Ring(latitude - 1, j), Ring(latitude - 1, j + 1) });
            }
            for (var i = 1; i < latitude - 1; i++)
            for (var j = 0; j < longitude; j++)
            {
                triangles.AddRange(new[] { Ring(i, j), Ring(i, j + 1), Ring(i + 1, j) });
                triangles.AddRange(new[] { Ring(i, j + 1), Ring(i + 1, j + 1), Ring(i + 1, j) });
            }
            return new MeshGeometry("Sphere", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>Cube from −size/2 to +size/2, each face an n×n grid with its own (split) vertices — like an imported mesh.</summary>
        public static MeshGeometry SubdividedCube(float size, int n)
        {
            var vertices = new List<float3>();
            var triangles = new List<int>();
            var half = size * 0.5f;
            var normals = new[] { new float3(1, 0, 0), new float3(-1, 0, 0), new float3(0, 1, 0), new float3(0, -1, 0), new float3(0, 0, 1), new float3(0, 0, -1) };
            foreach (var normal in normals)
            {
                GeometryUtils.Basis(normal, out var u, out var v);
                var start = vertices.Count;
                for (var i = 0; i <= n; i++)
                for (var j = 0; j <= n; j++)
                {
                    var a = -half + size * i / n;
                    var b = -half + size * j / n;
                    vertices.Add(math.round((normal * half + u * a + v * b) * 1e5f) / 1e5f);
                }
                for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    var i0 = start + i * (n + 1) + j;
                    var i1 = i0 + 1;
                    var i2 = i0 + n + 1;
                    var i3 = i2 + 1;
                    triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }
            }
            return new MeshGeometry("Cube", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>Flat grid in the XZ plane (open surface).</summary>
        public static MeshGeometry Grid(float size, int n, float y = 0f)
        {
            var vertices = new List<float3>();
            var triangles = new List<int>();
            for (var i = 0; i <= n; i++)
            for (var j = 0; j <= n; j++)
                vertices.Add(new float3(-size / 2 + size * i / n, y, -size / 2 + size * j / n));
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var i0 = i * (n + 1) + j;
                triangles.AddRange(new[] { i0, i0 + 1, i0 + n + 1, i0 + 1, i0 + n + 2, i0 + n + 1 });
            }
            return new MeshGeometry("Grid", vertices.ToArray(), triangles.ToArray());
        }

        public static MeshGeometry Merge(MeshGeometry a, MeshGeometry b) => MeshGeometry.Combine("Merged", new[] { a, b });

        public static Mesh ToMesh(MeshGeometry geometry)
        {
            var mesh = new Mesh { name = geometry.Label, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var vertices = new Vector3[geometry.Vertices.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = geometry.Vertices[i];
            mesh.vertices = vertices;
            mesh.triangles = geometry.Triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
