using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Topology clean-up before optimization: weld, degenerate / duplicate triangle removal, small-island removal.</summary>
    internal static class MeshCleaner
    {
        /// <summary>
        /// Merge vertices closer than <paramref name="epsilon"/> (grid snapping), then drop triangles that collapse or repeat.
        /// Split vertices (UV seams, hard edges) become shared, which collision meshes need.
        /// </summary>
        public static MeshGeometry Weld(MeshGeometry mesh, float epsilon)
        {
            epsilon = math.max(epsilon, 1e-7f);
            var map = new Dictionary<int3, int>(mesh.Vertices.Length);
            var vertices = new List<float3>(mesh.Vertices.Length);
            var remap = new int[mesh.Vertices.Length];

            for (var i = 0; i < mesh.Vertices.Length; i++)
            {
                var key = (int3)math.round(mesh.Vertices[i] / epsilon);
                if (!map.TryGetValue(key, out var index))
                {
                    index = vertices.Count;
                    map[key] = index;
                    vertices.Add(mesh.Vertices[i]);
                }
                remap[i] = index;
            }

            var triangles = new List<int>(mesh.Triangles.Length);
            var seen = new HashSet<int3>();
            for (var t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = remap[mesh.Triangles[t]];
                var b = remap[mesh.Triangles[t + 1]];
                var c = remap[mesh.Triangles[t + 2]];
                if (a == b || b == c || a == c) continue;
                if (!seen.Add(SortedKey(a, b, c))) continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            return Compact(mesh.Label, vertices, triangles);
        }

        /// <summary>
        /// Remove connected pieces whose bounds diagonal is below <paramref name="fraction"/> × the whole mesh diagonal.
        /// Never removes the largest piece.
        /// </summary>
        public static MeshGeometry RemoveSmallIslands(MeshGeometry mesh, float fraction)
        {
            if (fraction <= 0f || mesh.Triangles.Length == 0) return mesh;

            // Union-find over vertices shared by triangles.
            var parent = new int[mesh.Vertices.Length];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            for (var t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = Find(mesh.Triangles[t]);
                var b = Find(mesh.Triangles[t + 1]);
                var c = Find(mesh.Triangles[t + 2]);
                parent[b] = a;
                parent[Find(c)] = a;
            }

            var bounds = new Dictionary<int, (float3 min, float3 max)>();
            foreach (var index in mesh.Triangles)
            {
                var root = Find(index);
                var v = mesh.Vertices[index];
                bounds[root] = bounds.TryGetValue(root, out var b) ? (math.min(b.min, v), math.max(b.max, v)) : (v, v);
            }

            GeometryUtils.MinMax(mesh.Vertices, out var min, out var max);
            var threshold = math.length(max - min) * fraction;
            var largest = -1;
            var largestSize = -1f;
            foreach (var pair in bounds)
            {
                var size = math.length(pair.Value.max - pair.Value.min);
                if (size > largestSize) { largestSize = size; largest = pair.Key; }
            }

            var vertices = new List<float3>(mesh.Vertices);
            var triangles = new List<int>(mesh.Triangles.Length);
            for (var t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var root = Find(mesh.Triangles[t]);
                var b = bounds[root];
                if (root != largest && math.length(b.max - b.min) < threshold) continue;
                triangles.Add(mesh.Triangles[t]);
                triangles.Add(mesh.Triangles[t + 1]);
                triangles.Add(mesh.Triangles[t + 2]);
            }
            return Compact(mesh.Label, vertices, triangles);
        }

        /// <summary>True if any triangle has (near) zero area — used by tests and validation.</summary>
        public static int CountDegenerate(MeshGeometry mesh, float minDoubleArea = 1e-12f)
        {
            var count = 0;
            for (var t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var n = math.cross(mesh.Vertices[mesh.Triangles[t + 1]] - a, mesh.Vertices[mesh.Triangles[t + 2]] - a);
                if (math.length(n) <= minDoubleArea) count++;
            }
            return count;
        }

        private static int3 SortedKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return new int3(a, b, c);
        }

        /// <summary>Drop unreferenced vertices, keeping first-use order.</summary>
        private static MeshGeometry Compact(string label, List<float3> vertices, List<int> triangles)
        {
            var remap = new Dictionary<int, int>();
            var outVertices = new List<float3>();
            var outTriangles = new int[triangles.Count];
            for (var i = 0; i < triangles.Count; i++)
            {
                if (!remap.TryGetValue(triangles[i], out var mapped))
                {
                    mapped = outVertices.Count;
                    remap[triangles[i]] = mapped;
                    outVertices.Add(vertices[triangles[i]]);
                }
                outTriangles[i] = mapped;
            }
            return new MeshGeometry(label, outVertices.ToArray(), outTriangles);
        }
    }
}
