using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Picks the cheapest collider that fits each mesh:
    /// a primitive when one fills ≥ <see cref="GenerationSettings.PrimitiveFitThreshold"/> of the hull;
    /// else one convex hull when the mesh is convex enough (≥ <see cref="GenerationSettings.ConvexityThreshold"/>);
    /// else convex decomposition for moving Rigidbodies, or an optimized concave mesh for static objects.
    /// </summary>
    internal sealed class AutoStrategy : IColliderStrategy
    {
        private const int ConvexityResolution = 24;

        private readonly GenerationSettings _settings;

        public AutoStrategy(GenerationSettings settings)
        {
            _settings = settings;
        }

        public List<FittedShape> Fit(FitInput input, FitContext context)
        {
            var hull = input.Hull;
            if (hull == null || hull.Volume <= 0f)
                return Tag(new List<FittedShape> { BoxStrategy.FitBox(input, context) }, Strings.KindBox);

            var primitive = PrimitiveReplacer.BestFit(input, context, out var fill);
            if (primitive != null && fill >= _settings.PrimitiveFitThreshold)
                return Tag(new List<FittedShape> { primitive }, KindName(primitive.Kind));

            if (Convexity(input) >= _settings.ConvexityThreshold)
                return Tag(new ConvexHullStrategy().Fit(input, context), Strings.StrategyConvexHull);

            return context.IsDynamic
                ? Tag(new ConvexDecompositionStrategy(_settings).Fit(input, context), Strings.StrategyConvexDecomposition)
                : Tag(new OptimizedMeshStrategy(_settings).Fit(input, context), Strings.StrategyOptimizedMesh);
        }

        /// <summary>
        /// Solid volume ÷ hull volume (1 = convex). Uses the exact mesh volume for closed meshes and a voxel estimate
        /// for open ones.
        /// </summary>
        public static float Convexity(FitInput input)
        {
            var hullVolume = input.Hull?.Volume ?? 0f;
            if (hullVolume <= 0f) return 1f;

            var geometry = input.Geometry;
            GeometryUtils.MinMax(geometry.Vertices, out var min, out var max);
            var welded = MeshCleaner.Weld(geometry, math.length(max - min) * 1e-5f);
            var solid = IsClosed(welded)
                ? GeometryUtils.MeshVolume(welded.Vertices, welded.Triangles)
                : Voxelizer.Voxelize(welded.Vertices, welded.Triangles, ConvexityResolution).SolidVolume;
            return math.saturate(solid / hullVolume);
        }

        /// <summary>Every edge shared by exactly two triangles (watertight).</summary>
        public static bool IsClosed(MeshGeometry mesh)
        {
            if (mesh.TriangleCount < 4) return false;
            var edges = new Dictionary<long, int>();
            var t = mesh.Triangles;
            for (var i = 0; i + 2 < t.Length; i += 3)
            for (var e = 0; e < 3; e++)
            {
                var a = t[i + e];
                var b = t[i + (e + 1) % 3];
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                edges[key] = edges.TryGetValue(key, out var count) ? count + 1 : 1;
            }
            foreach (var count in edges.Values)
                if (count != 2) return false;
            return true;
        }

        private static List<FittedShape> Tag(List<FittedShape> shapes, string choice)
        {
            if (shapes.Count > 0)
            {
                var note = string.Format(Strings.NoteAutoChose, choice);
                shapes[0].Note = string.IsNullOrEmpty(shapes[0].Note) ? note : note + " " + shapes[0].Note;
            }
            return shapes;
        }

        private static string KindName(ShapeKind kind) => kind switch
        {
            ShapeKind.Box => Strings.KindBox,
            ShapeKind.Sphere => Strings.KindSphere,
            ShapeKind.Capsule => Strings.KindCapsule,
            _ => Strings.KindConvex,
        };
    }
}
