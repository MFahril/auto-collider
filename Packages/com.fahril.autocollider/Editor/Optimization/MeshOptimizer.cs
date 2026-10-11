using System.Diagnostics;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// The mesh optimization pipeline used by the Optimized Mesh strategy and the Optimize tab:
    /// weld → remove small islands → cluster-simplify (quadric representatives) → inflate/deflate → measure.
    /// </summary>
    internal static class MeshOptimizer
    {
        public sealed class Result
        {
            public MeshGeometry Mesh;
            public ColliderStats Stats;
            public ErrorMetric.Result Error;
        }

        /// <summary>Simplified concave mesh. Geometry is expected in scaled (world-sized) space.</summary>
        public static Result Optimize(MeshGeometry source, GenerationSettings settings, bool measureError = true)
        {
            var timer = Stopwatch.StartNew();
            GeometryUtils.MinMax(source.Vertices, out var min, out var max);
            var diagonal = math.length(max - min);

            var cleaned = MeshCleaner.Weld(source, diagonal * 1e-5f);
            cleaned = MeshCleaner.RemoveSmallIslands(cleaned, settings.MinIslandSize);

            var simplified = ClusterSimplifier.Simplify(cleaned.Vertices, cleaned.Triangles, new ClusterSimplifier.Options
            {
                Quality = settings.Quality,
                Fidelity = settings.ShapeFidelity,
                MaxTriangles = settings.MaxTriangles,
            });

            var vertices = InflateDeflate.Offset(simplified.Vertices, simplified.Triangles, settings.Inflate);
            var mesh = new MeshGeometry(source.Label, vertices, simplified.Triangles);
            return Finish(source, mesh, measureError, timer);
        }

        /// <summary>Convex hull of the source, capped to the PhysX polygon limit and inflated (used to optimize convex colliders).</summary>
        public static Result OptimizeConvex(MeshGeometry source, GenerationSettings settings, bool measureError = true)
        {
            var timer = Stopwatch.StartNew();
            var points = GeometryUtils.Dedupe(source.Vertices);
            if (!QuickHull3D.TryBuildCapped(points, PhysicsLimits.MaxConvexPolygons, out var hull)) return null;

            var vertices = InflateDeflate.Offset(hull.Vertices, hull.Triangles, settings.Inflate);
            var mesh = new MeshGeometry(source.Label, vertices, hull.Triangles);
            return Finish(source, mesh, measureError, timer);
        }

        /// <summary>Error and statistics of an already-built collider mesh against its source.</summary>
        public static Result Measure(MeshGeometry source, MeshGeometry collider)
        {
            return Finish(source, collider, true, Stopwatch.StartNew());
        }

        private static Result Finish(MeshGeometry source, MeshGeometry mesh, bool measureError, Stopwatch timer)
        {
            var result = new Result
            {
                Mesh = mesh,
                Stats = new ColliderStats
                {
                    SourceVertices = source.Vertices.Length,
                    SourceTriangles = source.TriangleCount,
                    ResultVertices = mesh.Vertices.Length,
                    ResultTriangles = mesh.TriangleCount,
                },
            };

            if (measureError)
            {
                result.Error = ErrorMetric.Measure(source.Vertices, mesh.Vertices, mesh.Triangles);
                result.Stats.MaxError = result.Error.Max;
                result.Stats.MeanError = result.Error.Mean;
            }
            result.Stats.Milliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }
    }
}
