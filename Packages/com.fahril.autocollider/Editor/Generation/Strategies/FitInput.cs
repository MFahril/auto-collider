using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Geometry prepared once for all fitters: deduplicated points and (lazily) their convex hull.
    /// Enclosing shapes only depend on the hull, so fitters run on <see cref="HullPoints"/> for speed.
    /// Very dense input is thinned before the hull is built (<see cref="IsReduced"/>); primitive strategies then
    /// re-fit their extents over all <see cref="Points"/> so results still enclose the whole mesh.
    /// </summary>
    internal sealed class FitInput
    {
        /// <summary>Points above this count are thinned before building the hull.</summary>
        public const int MaxHullInput = 4096;

        private ConvexHull _hull;
        private bool _hullBuilt;
        private IReadOnlyList<float3> _hullInput;

        private FitInput(MeshGeometry geometry, float3[] points)
        {
            Geometry = geometry;
            Points = points;
        }

        public MeshGeometry Geometry { get; }

        /// <summary>Unique vertices.</summary>
        public float3[] Points { get; }

        /// <summary>True when the hull was built from a thinned point set.</summary>
        public bool IsReduced => Points.Length > MaxHullInput;

        /// <summary>Convex hull (uncapped); null for flat or degenerate geometry. Built on first use.</summary>
        public ConvexHull Hull
        {
            get
            {
                if (_hullBuilt) return _hull;
                _hullBuilt = true;
                _hullInput = QuickHull3D.ReducePoints(Points, MaxHullInput);
                QuickHull3D.TryBuild(_hullInput, out _hull);
                return _hull;
            }
        }

        /// <summary>Hull vertices, or all points when there is no hull.</summary>
        public float3[] HullPoints => Hull != null ? Hull.Vertices : Points;

        public static FitInput Create(MeshGeometry geometry) => new(geometry, GeometryUtils.Dedupe(geometry.Vertices));
    }
}
