using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Vertex-clustering mesh simplification (Burst). Vertices are bucketed into grid cells; each cell collapses to one
    /// representative. Unlike plain averaging, the representative minimizes the <b>quadric error</b> of the faces around it
    /// (Lindstrom 2000), so corners and edges survive at the same triangle budget.
    /// Deterministic: same input and options → identical output.
    /// </summary>
    internal static class ClusterSimplifier
    {
        /// <summary>Cell size as a fraction of the bounds diagonal at Quality 0 and Quality 1 (log-interpolated).</summary>
        public const float CoarsestCellFraction = 1f / 6f;
        public const float FinestCellFraction = 1f / 400f;
        private const int MaxBudgetIterations = 40;

        public struct Options
        {
            /// <summary>0 = coarsest, 1 = finest.</summary>
            public float Quality;
            /// <summary>0 = cell mean, 1 = quadric-optimal point (clamped to the cell).</summary>
            public float Fidelity;
            /// <summary>0 = no limit; otherwise cells grow until the result fits.</summary>
            public int MaxTriangles;
        }

        public sealed class Result
        {
            public float3[] Vertices;
            public int[] Triangles;
            public float CellSize;
            public int TriangleCount => Triangles.Length / 3;
        }

        /// <summary>Cell size for a quality value on a mesh with the given bounds diagonal.</summary>
        public static float CellSizeFor(float quality, float diagonal)
        {
            var t = math.saturate(quality);
            var fraction = math.exp(math.lerp(math.log(CoarsestCellFraction), math.log(FinestCellFraction), t));
            return math.max(diagonal * fraction, 1e-6f);
        }

        public static Result Simplify(float3[] vertices, int[] triangles, Options options)
        {
            if (vertices.Length == 0 || triangles.Length < 3)
                return new Result { Vertices = vertices, Triangles = triangles, CellSize = 0f };

            GeometryUtils.MinMax(vertices, out var min, out var max);
            var diagonal = math.length(max - min);
            var cell = CellSizeFor(options.Quality, diagonal);

            var result = Run(vertices, triangles, min, cell, options.Fidelity);
            if (options.MaxTriangles <= 0) return result;

            for (var i = 0; i < MaxBudgetIterations && result.TriangleCount > options.MaxTriangles; i++)
            {
                cell *= 1.2f;
                result = Run(vertices, triangles, min, cell, options.Fidelity);
            }
            return result;
        }

        private static Result Run(float3[] vertices, int[] triangles, float3 origin, float cell, float fidelity)
        {
            var inVertices = new NativeArray<float3>(vertices, Allocator.TempJob);
            var inTriangles = new NativeArray<int>(triangles, Allocator.TempJob);
            var outVertices = new NativeList<float3>(Allocator.TempJob);
            var outTriangles = new NativeList<int>(Allocator.TempJob);
            try
            {
                new ClusterJob
                {
                    Vertices = inVertices,
                    Triangles = inTriangles,
                    Origin = origin,
                    CellSize = cell,
                    Fidelity = math.saturate(fidelity),
                    OutVertices = outVertices,
                    OutTriangles = outTriangles,
                }.Run();

                return new Result
                {
                    Vertices = outVertices.AsArray().ToArray(),
                    Triangles = outTriangles.AsArray().ToArray(),
                    CellSize = cell,
                };
            }
            finally
            {
                inVertices.Dispose();
                inTriangles.Dispose();
                outVertices.Dispose();
                outTriangles.Dispose();
            }
        }

        /// <summary>Symmetric 4x4 error quadric, stored as its 10 unique values.</summary>
        private struct Quadric
        {
            public double A00, A01, A02, A11, A12, A22, B0, B1, B2, C;

            public static Quadric FromPlane(double3 n, double d, double weight) => new()
            {
                A00 = n.x * n.x * weight, A01 = n.x * n.y * weight, A02 = n.x * n.z * weight,
                A11 = n.y * n.y * weight, A12 = n.y * n.z * weight, A22 = n.z * n.z * weight,
                B0 = n.x * d * weight, B1 = n.y * d * weight, B2 = n.z * d * weight,
                C = d * d * weight,
            };

            public void Add(in Quadric q)
            {
                A00 += q.A00; A01 += q.A01; A02 += q.A02; A11 += q.A11; A12 += q.A12; A22 += q.A22;
                B0 += q.B0; B1 += q.B1; B2 += q.B2; C += q.C;
            }

            /// <summary>
            /// Quadric-optimal point closest to <paramref name="reference"/> (Lindstrom's pseudo-inverse solve):
            /// rank 3 (corner) → the corner, rank 2 (edge) → onto the edge line, rank 1 (flat) → onto the plane.
            /// </summary>
            public double3 SolveAround(double3 reference)
            {
                var a = new double3x3(A00, A01, A02, A01, A11, A12, A02, A12, A22);
                var residual = -new double3(B0, B1, B2) - math.mul(a, reference);
                JacobiEigen(a, out var values, out var vectors);

                var largest = math.cmax(math.abs(values));
                if (largest <= 0) return reference;

                var point = reference;
                for (var i = 0; i < 3; i++)
                {
                    if (math.abs(values[i]) <= RelativeRankThreshold * largest) continue;
                    var v = vectors[i];
                    point += v * (math.dot(v, residual) / values[i]);
                }
                return point;
            }

            /// <summary>Eigenvalues are relevant above this fraction of the largest one (sets what counts as an edge/corner).</summary>
            private const double RelativeRankThreshold = 1e-3;

            /// <summary>Cyclic Jacobi for a symmetric 3x3 matrix (Burst-friendly). Eigenvectors are the columns.</summary>
            private static void JacobiEigen(double3x3 matrix, out double3 values, out double3x3 vectors)
            {
                var a = matrix;
                var v = double3x3.identity;
                for (var sweep = 0; sweep < 32; sweep++)
                {
                    if (math.abs(a.c1.x) + math.abs(a.c2.x) + math.abs(a.c2.y) < 1e-24) break;
                    Rotate(ref a, ref v, 0, 1);
                    Rotate(ref a, ref v, 0, 2);
                    Rotate(ref a, ref v, 1, 2);
                }
                values = new double3(a.c0.x, a.c1.y, a.c2.z);
                vectors = v;
            }

            private static void Rotate(ref double3x3 a, ref double3x3 v, int p, int q)
            {
                var apq = a[q][p];
                if (math.abs(apq) < 1e-300) return;
                var theta = (a[q][q] - a[p][p]) / (2 * apq);
                var t = (theta >= 0 ? 1.0 : -1.0) / (math.abs(theta) + math.sqrt(theta * theta + 1));
                var c = 1 / math.sqrt(t * t + 1);
                var s = t * c;

                // Indexing is [column][row]: J has c on (p,p),(q,q), s at row p / column q, −s at row q / column p.
                var j = double3x3.identity;
                j[p][p] = c;
                j[q][q] = c;
                j[q][p] = s;
                j[p][q] = -s;
                a = math.mul(math.transpose(j), math.mul(a, j));
                v = math.mul(v, j);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ClusterJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int> Triangles;
            public float3 Origin;
            public float CellSize;
            public float Fidelity;
            public NativeList<float3> OutVertices;
            public NativeList<int> OutTriangles;

            public void Execute()
            {
                var vertexCount = Vertices.Length;
                var cellOf = new NativeHashMap<int3, int>(vertexCount, Allocator.Temp);
                var clusterOfVertex = new NativeArray<int>(vertexCount, Allocator.Temp);
                var clusterKeys = new NativeList<int3>(Allocator.Temp);
                var sums = new NativeList<double3>(Allocator.Temp);
                var counts = new NativeList<int>(Allocator.Temp);

                // 1. Assign clusters in vertex order (deterministic ids).
                for (var i = 0; i < vertexCount; i++)
                {
                    var key = (int3)math.floor((Vertices[i] - Origin) / CellSize);
                    if (!cellOf.TryGetValue(key, out var cluster))
                    {
                        cluster = clusterKeys.Length;
                        cellOf.Add(key, cluster);
                        clusterKeys.Add(key);
                        sums.Add(double3.zero);
                        counts.Add(0);
                    }
                    clusterOfVertex[i] = cluster;
                    sums[cluster] += Vertices[i];
                    counts[cluster] += 1;
                }

                // 2. Area-weighted face quadrics accumulated per cluster.
                var quadrics = new NativeArray<Quadric>(clusterKeys.Length, Allocator.Temp);
                for (var t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    double3 a = Vertices[Triangles[t]], b = Vertices[Triangles[t + 1]], c = Vertices[Triangles[t + 2]];
                    var n = math.cross(b - a, c - a);
                    var doubleArea = math.length(n);
                    if (doubleArea < 1e-20) continue;
                    n /= doubleArea;
                    var q = Quadric.FromPlane(n, -math.dot(n, a), doubleArea * 0.5);
                    for (var k = 0; k < 3; k++)
                    {
                        var cluster = clusterOfVertex[Triangles[t + k]];
                        var acc = quadrics[cluster];
                        acc.Add(q);
                        quadrics[cluster] = acc;
                    }
                }

                // 3. Representative per cluster: mean blended toward the quadric optimum, kept inside the cell.
                var representatives = new NativeArray<float3>(clusterKeys.Length, Allocator.Temp);
                for (var c = 0; c < clusterKeys.Length; c++)
                {
                    var mean = sums[c] / math.max(1, counts[c]);
                    var point = mean;
                    if (Fidelity > 0f)
                    {
                        double3 cellMin = (double3)Origin + (double3)clusterKeys[c] * CellSize;
                        var optimum = math.clamp(quadrics[c].SolveAround(mean), cellMin, cellMin + CellSize);
                        point = math.lerp(mean, optimum, Fidelity);
                    }
                    representatives[c] = (float3)point;
                }

                // 4. Remap triangles; drop collapsed, sliver and duplicate (either winding) triangles.
                var seen = new NativeHashSet<int3>(Triangles.Length / 3, Allocator.Temp);
                var remap = new NativeArray<int>(clusterKeys.Length, Allocator.Temp);
                for (var c = 0; c < remap.Length; c++) remap[c] = -1;
                var minArea = 1e-6f * CellSize * CellSize;

                for (var t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    var c0 = clusterOfVertex[Triangles[t]];
                    var c1 = clusterOfVertex[Triangles[t + 1]];
                    var c2 = clusterOfVertex[Triangles[t + 2]];
                    if (c0 == c1 || c1 == c2 || c0 == c2) continue;

                    var p0 = representatives[c0];
                    if (math.length(math.cross(representatives[c1] - p0, representatives[c2] - p0)) < minArea) continue;

                    var sorted = Sort(new int3(c0, c1, c2));
                    if (!seen.Add(sorted)) continue;

                    OutTriangles.Add(Compact(c0, ref remap, representatives));
                    OutTriangles.Add(Compact(c1, ref remap, representatives));
                    OutTriangles.Add(Compact(c2, ref remap, representatives));
                }
            }

            private int Compact(int cluster, ref NativeArray<int> remap, NativeArray<float3> representatives)
            {
                if (remap[cluster] >= 0) return remap[cluster];
                remap[cluster] = OutVertices.Length;
                OutVertices.Add(representatives[cluster]);
                return remap[cluster];
            }

            private static int3 Sort(int3 v)
            {
                if (v.x > v.y) v = v.yxz;
                if (v.y > v.z) v = v.xzy;
                if (v.x > v.y) v = v.yxz;
                return v;
            }
        }
    }
}
