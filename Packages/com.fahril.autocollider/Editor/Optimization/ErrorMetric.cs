using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// How far a collider surface deviates from the source mesh: distance from sampled source vertices to the closest point
    /// on the collider triangles (Burst, parallel). Drives statistics and the Scene-view heatmap.
    /// </summary>
    internal static class ErrorMetric
    {
        public const int DefaultMaxSamples = 4096;

        public sealed class Result
        {
            public float3[] Samples;
            public float3[] Closest;
            public float[] Distances;
            public float Max;
            public float Mean;
        }

        public static Result Measure(float3[] sourceVertices, float3[] colliderVertices, int[] colliderTriangles,
            int maxSamples = DefaultMaxSamples)
        {
            var stride = math.max(1, (sourceVertices.Length + maxSamples - 1) / math.max(1, maxSamples));
            var sampleCount = (sourceVertices.Length + stride - 1) / stride;
            if (sampleCount == 0 || colliderTriangles.Length < 3)
                return new Result { Samples = new float3[0], Closest = new float3[0], Distances = new float[0] };

            var samples = new NativeArray<float3>(sampleCount, Allocator.TempJob);
            for (var i = 0; i < sampleCount; i++) samples[i] = sourceVertices[i * stride];
            var vertices = new NativeArray<float3>(colliderVertices, Allocator.TempJob);
            var triangles = new NativeArray<int>(colliderTriangles, Allocator.TempJob);
            var closest = new NativeArray<float3>(sampleCount, Allocator.TempJob);
            var distances = new NativeArray<float>(sampleCount, Allocator.TempJob);
            try
            {
                new ClosestPointJob
                {
                    Samples = samples,
                    Vertices = vertices,
                    Triangles = triangles,
                    Closest = closest,
                    Distances = distances,
                }.Schedule(sampleCount, 32).Complete();

                var result = new Result
                {
                    Samples = samples.ToArray(),
                    Closest = closest.ToArray(),
                    Distances = distances.ToArray(),
                };
                double sum = 0;
                foreach (var d in result.Distances)
                {
                    result.Max = math.max(result.Max, d);
                    sum += d;
                }
                result.Mean = (float)(sum / sampleCount);
                return result;
            }
            finally
            {
                samples.Dispose();
                vertices.Dispose();
                triangles.Dispose();
                closest.Dispose();
                distances.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ClosestPointJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Samples;
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int> Triangles;
            public NativeArray<float3> Closest;
            public NativeArray<float> Distances;

            public void Execute(int index)
            {
                var p = Samples[index];
                var best = float.MaxValue;
                var bestPoint = p;
                for (var t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    var q = ClosestPointOnTriangle(p, Vertices[Triangles[t]], Vertices[Triangles[t + 1]], Vertices[Triangles[t + 2]]);
                    var d = math.distancesq(p, q);
                    if (d < best)
                    {
                        best = d;
                        bestPoint = q;
                    }
                }
                Closest[index] = bestPoint;
                Distances[index] = math.sqrt(best);
            }
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        public static float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            var d1 = math.dot(ab, ap);
            var d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            var bp = p - b;
            var d3 = math.dot(ab, bp);
            var d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

            var cp = p - c;
            var d5 = math.dot(ab, cp);
            var d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            var denominator = 1f / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }
    }
}
