using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Triangle geometry gathered from one or more renderers, in the target's scaled space.</summary>
    internal sealed class MeshGeometry
    {
        public MeshGeometry(string label, float3[] vertices, int[] triangles)
        {
            Label = label;
            Vertices = vertices;
            Triangles = triangles;
        }

        public string Label { get; }
        public float3[] Vertices { get; }
        public int[] Triangles { get; }
        public int TriangleCount => Triangles.Length / 3;

        /// <summary>Concatenate several geometries into one (used for <see cref="MeshGranularity.Combined"/>).</summary>
        public static MeshGeometry Combine(string label, IReadOnlyList<MeshGeometry> parts)
        {
            var vertices = new List<float3>();
            var triangles = new List<int>();
            foreach (var part in parts)
            {
                var offset = vertices.Count;
                vertices.AddRange(part.Vertices);
                foreach (var index in part.Triangles) triangles.Add(index + offset);
            }
            return new MeshGeometry(label, vertices.ToArray(), triangles.ToArray());
        }
    }
}
