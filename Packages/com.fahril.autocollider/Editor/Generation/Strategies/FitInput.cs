using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Geometry prepared once for all fitters: deduplicated points and their convex hull.
    /// Enclosing shapes only depend on the hull, so fitters run on <see cref="HullPoints"/> for speed.
    /// </summary>
    internal sealed class FitInput
    {
        private FitInput(MeshGeometry geometry, float3[] points, ConvexHull hull)
        {
            Geometry = geometry;
            Points = points;
            Hull = hull;
        }

        public MeshGeometry Geometry { get; }

        /// <summary>Unique vertices.</summary>
        public float3[] Points { get; }

        /// <summary>Full (uncapped) convex hull; null for flat or degenerate geometry.</summary>
        public ConvexHull Hull { get; }

        /// <summary>Hull vertices, or all points when there is no hull.</summary>
        public float3[] HullPoints => Hull != null ? Hull.Vertices : Points;

        public static FitInput Create(MeshGeometry geometry)
        {
            var points = GeometryUtils.Dedupe(geometry.Vertices);
            QuickHull3D.TryBuild(points, out var hull);
            return new FitInput(geometry, points, hull);
        }
    }
}
