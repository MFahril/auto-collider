using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Triangulated convex hull. Triangles wind so that <c>cross(b - a, c - a)</c> points outward.</summary>
    internal sealed class ConvexHull
    {
        public ConvexHull(float3[] vertices, int[] triangles)
        {
            Vertices = vertices;
            Triangles = triangles;
            Volume = GeometryUtils.MeshVolume(vertices, triangles);
        }

        public float3[] Vertices { get; }
        public int[] Triangles { get; }
        public int TriangleCount => Triangles.Length / 3;
        public float Volume { get; }

        /// <summary>Outward unit normals, one per triangle.</summary>
        public IEnumerable<float3> FaceNormals()
        {
            for (var i = 0; i < Triangles.Length; i += 3)
            {
                var a = Vertices[Triangles[i]];
                var n = math.cross(Vertices[Triangles[i + 1]] - a, Vertices[Triangles[i + 2]] - a);
                var len = math.length(n);
                if (len > 1e-12f) yield return n / len;
            }
        }

        /// <summary>Signed distance of <paramref name="p"/> to the hull boundary (negative inside). Approximate for points outside.</summary>
        public float MaxPlaneDistance(float3 p)
        {
            var max = float.MinValue;
            for (var i = 0; i < Triangles.Length; i += 3)
            {
                var a = Vertices[Triangles[i]];
                var n = math.cross(Vertices[Triangles[i + 1]] - a, Vertices[Triangles[i + 2]] - a);
                var len = math.length(n);
                if (len < 1e-12f) continue;
                max = math.max(max, math.dot(n / len, p - a));
            }
            return max;
        }
    }
}
