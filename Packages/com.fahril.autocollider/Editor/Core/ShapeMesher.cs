using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Triangle meshes for fitted shapes (in scaled space), e.g. to measure the error of mixed primitive / hull results.</summary>
    internal static class ShapeMesher
    {
        private const int Rings = 8;
        private const int Segments = 16;

        public static MeshGeometry ToMesh(FittedShape shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.ConvexMesh:
                    return new MeshGeometry(shape.Label, shape.Hull.Vertices, shape.Hull.Triangles);
                case ShapeKind.ConcaveMesh:
                    return shape.Mesh;
                case ShapeKind.Box:
                    return Transform(Box(shape.Size), shape);
                case ShapeKind.Sphere:
                    return Transform(Capsule(1, shape.Radius, 2f * shape.Radius), shape);
                default:
                    return Transform(Capsule(shape.Direction, shape.Radius, shape.Height), shape);
            }
        }

        /// <summary>All shapes merged into one triangle soup.</summary>
        public static MeshGeometry Combine(string label, IEnumerable<FittedShape> shapes)
        {
            var parts = new List<MeshGeometry>();
            foreach (var shape in shapes) parts.Add(ToMesh(shape));
            return MeshGeometry.Combine(label, parts);
        }

        private static MeshGeometry Transform(MeshGeometry local, FittedShape shape)
        {
            var vertices = new float3[local.Vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = shape.Center + math.mul(shape.Rotation, local.Vertices[i]);
            return new MeshGeometry(shape.Label, vertices, local.Triangles);
        }

        private static MeshGeometry Box(float3 size)
        {
            var h = size * 0.5f;
            var vertices = new float3[8];
            for (var i = 0; i < 8; i++)
                vertices[i] = new float3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            var triangles = new[]
            {
                0, 2, 1, 1, 2, 3, // -z
                4, 5, 6, 5, 7, 6, // +z
                0, 1, 4, 1, 5, 4, // -y
                2, 6, 3, 3, 6, 7, // +y
                0, 4, 2, 2, 4, 6, // -x
                1, 3, 5, 3, 7, 5, // +x
            };
            return new MeshGeometry("Box", vertices, triangles);
        }

        /// <summary>Capsule along local axis <paramref name="direction"/> (a sphere when height = 2·radius).</summary>
        private static MeshGeometry Capsule(int direction, float radius, float height)
        {
            var half = math.max(0f, height * 0.5f - radius);
            var vertices = new List<float3>();
            var triangles = new List<int>();

            // Rings from the top pole to the bottom pole; the equator is duplicated to form the cylinder.
            var ringCount = 0;
            for (var i = 0; i <= Rings; i++)
            {
                var theta = math.PI * i / Rings;
                var offset = i <= Rings / 2 ? half : -half;
                AddRing(theta, offset);
                if (i == Rings / 2) AddRing(theta, -half);
            }

            void AddRing(float theta, float offset)
            {
                for (var j = 0; j < Segments; j++)
                {
                    var phi = 2f * math.PI * j / Segments;
                    var p = new float3(math.sin(theta) * math.cos(phi) * radius, math.cos(theta) * radius + offset, math.sin(theta) * math.sin(phi) * radius);
                    vertices.Add(Reorient(p, direction));
                }
                ringCount++;
            }

            for (var r = 0; r < ringCount - 1; r++)
            for (var j = 0; j < Segments; j++)
            {
                var a = r * Segments + j;
                var b = r * Segments + (j + 1) % Segments;
                var c = a + Segments;
                var d = b + Segments;
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }
            return new MeshGeometry("Capsule", vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>Map a Y-up point so that Y becomes the capsule axis.</summary>
        private static float3 Reorient(float3 p, int direction) => direction switch
        {
            0 => new float3(p.y, p.z, p.x),
            2 => new float3(p.z, p.x, p.y),
            _ => p,
        };
    }
}
