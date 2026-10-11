using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Solid voxelization of a triangle mesh (Burst).</summary>
    internal sealed class VoxelGrid
    {
        public const byte Empty = 0;
        public const byte Surface = 1;
        public const byte Interior = 2;

        public int3 Dimensions;
        public float3 Origin;
        public float VoxelSize;
        public byte[] Cells;

        /// <summary>Points on the mesh surface (spacing ≤ half a voxel) and the voxel each one falls in.</summary>
        public float3[] Samples;
        public int[] SampleVoxel;

        public int Count => Cells.Length;
        public float VoxelVolume => VoxelSize * VoxelSize * VoxelSize;

        public int Index(int3 v) => v.x + Dimensions.x * (v.y + Dimensions.y * v.z);

        public int3 Coordinate(int index)
        {
            var x = index % Dimensions.x;
            var yz = index / Dimensions.x;
            return new int3(x, yz % Dimensions.y, yz / Dimensions.y);
        }

        public float3 Corner(int3 v) => Origin + (float3)v * VoxelSize;

        public bool InRange(int3 v) => math.all(v >= 0) && math.all(v < Dimensions);

        /// <summary>Estimated solid volume: interior voxels count fully, surface voxels half.</summary>
        public float SolidVolume
        {
            get
            {
                long interior = 0, surface = 0;
                foreach (var c in Cells)
                {
                    if (c == Interior) interior++;
                    else if (c == Surface) surface++;
                }
                return (interior + surface * 0.5f) * VoxelVolume;
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="VoxelGrid"/>: surface voxels by exact triangle/box overlap, interior by flood-filling the outside.
    /// Closed meshes become solid; open meshes become a shell.
    /// </summary>
    internal static class Voxelizer
    {
        public static VoxelGrid Voxelize(float3[] vertices, int[] triangles, int resolution)
        {
            GeometryUtils.MinMax(vertices, out var min, out var max);
            var extent = math.max(max - min, 1e-6f);
            var voxel = math.cmax(extent) / math.max(1, resolution);
            var grid = new VoxelGrid
            {
                VoxelSize = voxel,
                // Two empty layers of padding on every side, so the outside flood fill can always surround the mesh
                // (triangles on the bounds touch the first layer through the slightly enlarged overlap test).
                Origin = min - 2f * voxel,
                Dimensions = (int3)math.ceil(extent / voxel) + 4,
            };

            var cellCount = grid.Dimensions.x * grid.Dimensions.y * grid.Dimensions.z;
            var cells = new NativeArray<byte>(cellCount, Allocator.TempJob);
            var inVertices = new NativeArray<float3>(vertices, Allocator.TempJob);
            var inTriangles = new NativeArray<int>(triangles, Allocator.TempJob);
            var samples = new NativeList<float3>(Allocator.TempJob);
            var sampleVoxel = new NativeList<int>(Allocator.TempJob);
            try
            {
                new VoxelizeJob
                {
                    Vertices = inVertices,
                    Triangles = inTriangles,
                    Origin = grid.Origin,
                    VoxelSize = voxel,
                    Dimensions = grid.Dimensions,
                    Cells = cells,
                    Samples = samples,
                    SampleVoxel = sampleVoxel,
                }.Run();

                grid.Cells = cells.ToArray();
                grid.Samples = samples.AsArray().ToArray();
                grid.SampleVoxel = sampleVoxel.AsArray().ToArray();
                return grid;
            }
            finally
            {
                cells.Dispose();
                inVertices.Dispose();
                inTriangles.Dispose();
                samples.Dispose();
                sampleVoxel.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct VoxelizeJob : IJob
        {
            private const byte Outside = 3;

            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int> Triangles;
            public float3 Origin;
            public float VoxelSize;
            public int3 Dimensions;
            public NativeArray<byte> Cells;
            public NativeList<float3> Samples;
            public NativeList<int> SampleVoxel;

            public void Execute()
            {
                var half = new float3(VoxelSize * 0.5f * 1.0001f);
                var spacing = VoxelSize * 0.5f;

                for (var t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    var a = Vertices[Triangles[t]];
                    var b = Vertices[Triangles[t + 1]];
                    var c = Vertices[Triangles[t + 2]];

                    // Surface voxels: every voxel the triangle touches.
                    var lo = math.clamp((int3)math.floor((math.min(math.min(a, b), c) - Origin) / VoxelSize), 0, Dimensions - 1);
                    var hi = math.clamp((int3)math.floor((math.max(math.max(a, b), c) - Origin) / VoxelSize), 0, Dimensions - 1);
                    for (var z = lo.z; z <= hi.z; z++)
                    for (var y = lo.y; y <= hi.y; y++)
                    for (var x = lo.x; x <= hi.x; x++)
                    {
                        var center = Origin + (new float3(x, y, z) + 0.5f) * VoxelSize;
                        if (TriangleBoxOverlap(center, half, a, b, c))
                            Cells[x + Dimensions.x * (y + Dimensions.y * z)] = VoxelGrid.Surface;
                    }

                    // Surface samples (barycentric grid) used as hull points.
                    var longest = math.max(math.max(math.distance(a, b), math.distance(b, c)), math.distance(c, a));
                    var n = math.max(1, (int)math.ceil(longest / spacing));
                    for (var i = 0; i <= n; i++)
                    for (var j = 0; j <= n - i; j++)
                    {
                        var p = a + (b - a) * ((float)i / n) + (c - a) * ((float)j / n);
                        var v = math.clamp((int3)math.floor((p - Origin) / VoxelSize), 0, Dimensions - 1);
                        Samples.Add(p);
                        SampleVoxel.Add(v.x + Dimensions.x * (v.y + Dimensions.y * v.z));
                    }
                }

                FloodOutside();

                for (var i = 0; i < Cells.Length; i++)
                {
                    if (Cells[i] == VoxelGrid.Empty) Cells[i] = VoxelGrid.Interior;
                    else if (Cells[i] == Outside) Cells[i] = VoxelGrid.Empty;
                }
            }

            private void FloodOutside()
            {
                var queue = new NativeList<int>(Allocator.Temp);
                Cells[0] = Outside; // corner voxel is padding, always empty
                queue.Add(0);
                for (var head = 0; head < queue.Length; head++)
                {
                    var index = queue[head];
                    var x = index % Dimensions.x;
                    var yz = index / Dimensions.x;
                    var v = new int3(x, yz % Dimensions.y, yz / Dimensions.y);
                    Visit(v + new int3(1, 0, 0), ref queue);
                    Visit(v - new int3(1, 0, 0), ref queue);
                    Visit(v + new int3(0, 1, 0), ref queue);
                    Visit(v - new int3(0, 1, 0), ref queue);
                    Visit(v + new int3(0, 0, 1), ref queue);
                    Visit(v - new int3(0, 0, 1), ref queue);
                }
            }

            private void Visit(int3 v, ref NativeList<int> queue)
            {
                if (math.any(v < 0) || math.any(v >= Dimensions)) return;
                var index = v.x + Dimensions.x * (v.y + Dimensions.y * v.z);
                if (Cells[index] != VoxelGrid.Empty) return;
                Cells[index] = Outside;
                queue.Add(index);
            }
        }

        /// <summary>Triangle / axis-aligned box overlap (separating axis theorem, Akenine-Möller).</summary>
        public static bool TriangleBoxOverlap(float3 center, float3 half, float3 a, float3 b, float3 c)
        {
            var v0 = a - center;
            var v1 = b - center;
            var v2 = c - center;

            // Box face normals.
            var min = math.min(math.min(v0, v1), v2);
            var max = math.max(math.max(v0, v1), v2);
            if (math.any(min > half) || math.any(max < -half)) return false;

            // Triangle normal.
            var e0 = v1 - v0;
            var e1 = v2 - v1;
            var e2 = v0 - v2;
            var normal = math.cross(e0, e1);
            if (!PlaneBoxOverlap(normal, v0, half)) return false;

            // Edge × axis cross products.
            return AxisTest(e0, v0, v1, v2, half) && AxisTest(e1, v0, v1, v2, half) && AxisTest(e2, v0, v1, v2, half);
        }

        private static bool AxisTest(float3 edge, float3 v0, float3 v1, float3 v2, float3 half)
        {
            for (var k = 0; k < 3; k++)
            {
                var unit = float3.zero;
                unit[k] = 1f;
                var axis = math.cross(unit, edge);
                var p0 = math.dot(axis, v0);
                var p1 = math.dot(axis, v1);
                var p2 = math.dot(axis, v2);
                var radius = math.dot(math.abs(axis), half);
                if (math.min(math.min(p0, p1), p2) > radius || math.max(math.max(p0, p1), p2) < -radius) return false;
            }
            return true;
        }

        private static bool PlaneBoxOverlap(float3 normal, float3 vertex, float3 half)
        {
            var vMin = math.select(half - vertex, -half - vertex, normal > 0f);
            var vMax = math.select(-half - vertex, half - vertex, normal > 0f);
            if (math.dot(normal, vMin) > 0f) return false;
            return math.dot(normal, vMax) >= 0f;
        }
    }
}
