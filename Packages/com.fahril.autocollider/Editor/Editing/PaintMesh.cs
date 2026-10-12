using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// A renderer's mesh in world space for painting: ray hits, brush selection, grow / connected selection.
    /// Triangle numbers match <see cref="MeshSource.Read"/> (and so <see cref="PaintedRegion.Triangles"/>).
    /// </summary>
    internal sealed class PaintMesh
    {
        private int[][] _vertexTriangles;
        private int[] _weldedVertex;

        public PaintMesh(Renderer renderer, ColliderGroup group, Vector3[] vertices, int[] triangles)
        {
            Renderer = renderer;
            Group = group;
            Vertices = vertices;
            Triangles = triangles;
            BuiltWith = renderer != null ? renderer.localToWorldMatrix : Matrix4x4.identity;

            var count = triangles.Length / 3;
            Centroids = new Vector3[count];
            Normals = new Vector3[count];
            Bounds = vertices.Length > 0 ? new Bounds(vertices[0], Vector3.zero) : default;
            foreach (var v in vertices) Bounds.Encapsulate(v);
            for (var t = 0; t < count; t++)
            {
                Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
                Centroids[t] = (a + b + c) / 3f;
                Normals[t] = Vector3.Cross(b - a, c - a).normalized;
            }
        }

        public Renderer Renderer { get; }
        public ColliderGroup Group { get; }
        public Vector3[] Vertices { get; }
        public int[] Triangles { get; }
        public Vector3[] Centroids { get; }
        public Vector3[] Normals { get; }
        public Bounds Bounds;
        public Matrix4x4 BuiltWith { get; }
        public int TriangleCount => Centroids.Length;

        /// <summary>Read a renderer (skinned meshes in their current pose) into world space.</summary>
        public static PaintMesh Build(Renderer renderer, ColliderGroup group)
        {
            var space = ColliderShapes.SpaceOf(group);
            var geometry = MeshSource.Read(renderer, space);
            var world = new Vector3[geometry.Vertices.Length];
            for (var i = 0; i < world.Length; i++) world[i] = ColliderShapes.ToWorld(space, geometry.Vertices[i]);
            return new PaintMesh(renderer, group, world, geometry.Triangles);
        }

        /// <summary>Nearest triangle hit by the ray (Möller–Trumbore, both faces).</summary>
        public bool Raycast(Ray ray, out int triangle, out float distance)
        {
            triangle = -1;
            distance = float.MaxValue;
            if (!Bounds.IntersectRay(ray)) return false;

            for (var t = 0; t < TriangleCount; t++)
            {
                if (!RayTriangle(ray, Vertices[Triangles[t * 3]], Vertices[Triangles[t * 3 + 1]], Vertices[Triangles[t * 3 + 2]], out var d)) continue;
                if (d >= distance) continue;
                distance = d;
                triangle = t;
            }
            return triangle >= 0;
        }

        /// <summary>
        /// Brush: triangles facing the camera whose centroid is within <paramref name="radiusPixels"/> of the mouse on screen
        /// and not hidden behind the surface under the mouse (depth within <paramref name="depthTolerance"/>). The triangle
        /// directly under the mouse is always included.
        /// </summary>
        public static List<(PaintMesh mesh, int triangle)> Brush(IReadOnlyList<PaintMesh> meshes, Ray ray, Vector3 cameraPosition,
            Vector2 mouse, float radiusPixels, float depthTolerance, Func<Vector3, Vector2> worldToGui)
        {
            var result = new List<(PaintMesh, int)>();
            PaintMesh hitMesh = null;
            var hitTriangle = -1;
            var hitDistance = float.MaxValue;
            foreach (var mesh in meshes)
            {
                if (!mesh.Raycast(ray, out var t, out var d) || d >= hitDistance) continue;
                hitMesh = mesh;
                hitTriangle = t;
                hitDistance = d;
            }
            if (hitMesh == null) return result;

            var maxDepth = Vector3.Distance(cameraPosition, ray.GetPoint(hitDistance)) + depthTolerance;
            var radiusSq = radiusPixels * radiusPixels;
            foreach (var mesh in meshes)
            {
                for (var t = 0; t < mesh.TriangleCount; t++)
                {
                    var c = mesh.Centroids[t];
                    if (Vector3.Dot(mesh.Normals[t], cameraPosition - c) <= 0f) continue;
                    if (Vector3.Distance(cameraPosition, c) > maxDepth) continue;
                    if ((worldToGui(c) - mouse).sqrMagnitude > radiusSq) continue;
                    result.Add((mesh, t));
                }
            }
            if (!result.Contains((hitMesh, hitTriangle))) result.Add((hitMesh, hitTriangle));
            return result;
        }

        /// <summary>The selection plus every triangle sharing a vertex with it (split vertices count as shared).</summary>
        public HashSet<int> Grow(IEnumerable<int> selection)
        {
            var grown = new HashSet<int>(selection);
            foreach (var t in new List<int>(grown))
                foreach (var n in Neighbors(t))
                    grown.Add(n);
            return grown;
        }

        /// <summary>Whole connected pieces touched by the selection.</summary>
        public HashSet<int> Connected(IEnumerable<int> selection)
        {
            var result = new HashSet<int>(selection);
            var stack = new Stack<int>(result);
            while (stack.Count > 0)
                foreach (var n in Neighbors(stack.Pop()))
                    if (result.Add(n)) stack.Push(n);
            return result;
        }

        private IEnumerable<int> Neighbors(int triangle)
        {
            BuildAdjacency();
            for (var k = 0; k < 3; k++)
                foreach (var n in _vertexTriangles[_weldedVertex[Triangles[triangle * 3 + k]]])
                    if (n != triangle) yield return n;
        }

        private void BuildAdjacency()
        {
            if (_vertexTriangles != null) return;

            // Weld by position so UV seams / hard edges don't split connectivity.
            var epsilon = math.max(Bounds.size.magnitude * 1e-5f, 1e-7f);
            var ids = new Dictionary<int3, int>();
            _weldedVertex = new int[Vertices.Length];
            for (var i = 0; i < Vertices.Length; i++)
            {
                var key = (int3)math.round((float3)Vertices[i] / epsilon);
                if (!ids.TryGetValue(key, out var id)) ids[key] = id = ids.Count;
                _weldedVertex[i] = id;
            }

            var lists = new List<int>[ids.Count];
            for (var t = 0; t < TriangleCount; t++)
                for (var k = 0; k < 3; k++)
                    (lists[_weldedVertex[Triangles[t * 3 + k]]] ??= new List<int>()).Add(t);

            _vertexTriangles = new int[lists.Length][];
            for (var i = 0; i < lists.Length; i++) _vertexTriangles[i] = lists[i]?.ToArray() ?? Array.Empty<int>();
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(ray.direction, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            var inv = 1f / det;
            var s = ray.origin - a;
            var u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            var v = Vector3.Dot(ray.direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }
    }
}
