using System.Collections.Generic;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Several convex parts for concave shapes (works on moving Rigidbodies). Parts that fit a primitive become one.
    /// Statistics and error for the whole part set are attached to the first part.
    /// </summary>
    internal sealed class ConvexDecompositionStrategy : IColliderStrategy
    {
        private readonly GenerationSettings _settings;

        public ConvexDecompositionStrategy(GenerationSettings settings)
        {
            _settings = settings;
        }

        public List<FittedShape> Fit(FitInput input, FitContext context)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var decomposition = ConvexDecomposer.Decompose(input.Geometry, new ConvexDecomposer.Options
            {
                Resolution = _settings.DecompositionResolution,
                MaxParts = _settings.MaxParts,
                ConcavityTolerance = _settings.ConcavityTolerance,
                PrimitiveFitThreshold = _settings.PrimitiveFitThreshold,
            }, context);

            var parts = decomposition.Parts;
            if (parts.Count == 0) return new List<FittedShape> { BoxStrategy.FitBox(input, context) };

            var union = ShapeMesher.Combine(input.Geometry.Label, parts);
            var measured = MeshOptimizer.Measure(input.Geometry, union);
            var stats = measured.Stats;
            stats.ResultTriangles = 0;
            foreach (var part in parts)
                if (part.Kind == ShapeKind.ConvexMesh) stats.ResultTriangles += part.Hull.TriangleCount;
            stats.ResultParts = parts.Count;
            stats.Milliseconds = timer.Elapsed.TotalMilliseconds;

            parts[0].Stats = stats;
            parts[0].Error = measured.Error;
            if (decomposition.Components > _settings.MaxParts)
                parts[0].Note = string.Format(Strings.NoteComponentsExceedMaxParts, decomposition.Components, _settings.MaxParts);
            return parts;
        }
    }
}
