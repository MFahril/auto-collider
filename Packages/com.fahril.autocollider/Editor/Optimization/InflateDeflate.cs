using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Offsets collider surfaces along their normals (+ grows, − shrinks).</summary>
    internal static class InflateDeflate
    {
        /// <summary>Move every vertex along its angle-weighted normal by <paramref name="distance"/>.</summary>
        public static float3[] Offset(float3[] vertices, int[] triangles, float distance)
        {
            var result = (float3[])vertices.Clone();
            if (distance == 0f) return result;

            var normals = AngleWeightedNormals(vertices, triangles);
            for (var i = 0; i < result.Length; i++)
                result[i] += normals[i] * distance;
            return result;
        }

        /// <summary>Grow or shrink a fitted shape by <paramref name="distance"/> (meters, scaled space).</summary>
        public static void Apply(FittedShape shape, float distance)
        {
            if (distance == 0f) return;
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                    shape.Size = math.max(shape.Size + 2f * distance, PhysicsLimits.MinExtent);
                    break;
                case ShapeKind.Sphere:
                    shape.Radius = math.max(shape.Radius + distance, PhysicsLimits.MinExtent);
                    break;
                case ShapeKind.Capsule:
                    shape.Radius = math.max(shape.Radius + distance, PhysicsLimits.MinExtent);
                    shape.Height = math.max(shape.Height + 2f * distance, 2f * shape.Radius);
                    break;
                case ShapeKind.ConvexMesh:
                    shape.Hull = new ConvexHull(Offset(shape.Hull.Vertices, shape.Hull.Triangles, distance), shape.Hull.Triangles);
                    break;
                case ShapeKind.ConcaveMesh:
                    var mesh = shape.Mesh;
                    shape.Mesh = new MeshGeometry(mesh.Label, Offset(mesh.Vertices, mesh.Triangles, distance), mesh.Triangles);
                    break;
            }
        }

        /// <summary>Per-vertex normals weighted by each face's corner angle (stable on uneven tessellation).</summary>
        public static float3[] AngleWeightedNormals(float3[] vertices, int[] triangles)
        {
            var normals = new float3[vertices.Length];
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var i0 = triangles[t];
                var i1 = triangles[t + 1];
                var i2 = triangles[t + 2];
                var p0 = vertices[i0];
                var p1 = vertices[i1];
                var p2 = vertices[i2];
                var n = math.cross(p1 - p0, p2 - p0);
                var len = math.length(n);
                if (len < 1e-12f) continue;
                n /= len;

                normals[i0] += n * Angle(p1 - p0, p2 - p0);
                normals[i1] += n * Angle(p2 - p1, p0 - p1);
                normals[i2] += n * Angle(p0 - p2, p1 - p2);
            }

            for (var i = 0; i < normals.Length; i++)
            {
                var len = math.length(normals[i]);
                normals[i] = len > 1e-12f ? normals[i] / len : float3.zero;
            }
            return normals;
        }

        private static float Angle(float3 a, float3 b)
        {
            var denominator = math.length(a) * math.length(b);
            return denominator < 1e-12f ? 0f : math.acos(math.clamp(math.dot(a, b) / denominator, -1f, 1f));
        }
    }
}
