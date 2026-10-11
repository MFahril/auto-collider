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

        /// <summary>Closed box (8 shared vertices, outward winding) spanning min..max.</summary>
        public static MeshGeometry Box(float3 min, float3 max)
        {
            var vertices = new float3[8];
            for (var i = 0; i < 8; i++)
                vertices[i] = new float3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            var triangles = new[]
            {
                0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
                2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5,
            };
            return new MeshGeometry("Box", vertices, triangles);
        }

        /// <summary>
        /// Closed L-shaped prism (outline (0,0)(3,0)(3,1)(1,1)(1,3)(0,3) in XZ, height 1): volume 5, convex hull volume 7.
        /// </summary>
        public static MeshGeometry LShape()
        {
            var outline = new[] { new float2(0, 0), new float2(3, 0), new float2(3, 1), new float2(1, 1), new float2(1, 3), new float2(0, 3) };
            var n = outline.Length;
            var vertices = new List<float3>();
            foreach (var p in outline) vertices.Add(new float3(p.x, 0, p.y)); // bottom 0..n-1
            foreach (var p in outline) vertices.Add(new float3(p.x, 1, p.y)); // top n..2n-1

            var triangles = new List<int>();
            for (var i = 1; i < n - 1; i++)
            {
                triangles.AddRange(new[] { 0, i, i + 1 });         // bottom, normal −Y (fan from (0,0) sees every vertex)
                triangles.AddRange(new[] { n, n + i + 1, n + i }); // top, normal +Y
            }
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                triangles.AddRange(new[] { i, n + j, j, i, n + i, n + j }); // sides, outward
            }
            return new MeshGeometry("L", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>Closed torus around the Y axis.</summary>
        public static MeshGeometry Torus(float major, float minor, int segmentsU = 32, int segmentsV = 16)
        {
            var vertices = new List<float3>();
            var triangles = new List<int>();
            for (var i = 0; i < segmentsU; i++)
            {
                var u = 2 * math.PI * i / segmentsU;
                for (var j = 0; j < segmentsV; j++)
                {
                    var v = 2 * math.PI * j / segmentsV;
                    var ring = major + minor * math.cos(v);
                    vertices.Add(new float3(ring * math.cos(u), minor * math.sin(v), ring * math.sin(u)));
                }
            }
            for (var i = 0; i < segmentsU; i++)
            for (var j = 0; j < segmentsV; j++)
            {
                int a = i * segmentsV + j, b = (i + 1) % segmentsU * segmentsV + j;
                int c = i * segmentsV + (j + 1) % segmentsV, d = (i + 1) % segmentsU * segmentsV + (j + 1) % segmentsV;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return new MeshGeometry("Torus", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>Closed regular octahedron (convex, but no primitive fits it well).</summary>
        public static MeshGeometry Octahedron(float radius)
        {
            var vertices = new[]
            {
                new float3(radius, 0, 0), new float3(-radius, 0, 0), new float3(0, radius, 0),
                new float3(0, -radius, 0), new float3(0, 0, radius), new float3(0, 0, -radius),
            };
            var triangles = new[] { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5 };
            return new MeshGeometry("Octahedron", vertices, triangles);
        }

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
