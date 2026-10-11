namespace AutoCollider.Editor
{
    /// <summary>
    /// Swaps a convex part for a box / sphere / capsule when the primitive is nearly as tight
    /// (fill ratio = part volume ÷ primitive volume). Primitives are much cheaper for PhysX than convex meshes.
    /// </summary>
    internal static class PrimitiveReplacer
    {
        /// <summary>Within this ratio of the best fill, cheaper shapes (sphere → capsule → box) win.</summary>
        private const float CheaperShapeTolerance = 0.98f;

        /// <summary>The best-filling primitive for the input, and how well it fills (0..1, based on hull volume).</summary>
        public static FittedShape BestFit(FitInput input, FitContext context, out float fill)
        {
            fill = 0f;
            var hull = input.Hull;
            if (hull == null || hull.Volume <= 0f) return null;

            // Ordered cheapest physics cost first.
            var candidates = new[]
            {
                SphereStrategy.FitSphere(input),
                CapsuleStrategy.FitCapsule(input, context),
                BoxStrategy.FitBox(input, context),
            };

            var bestFill = 0f;
            foreach (var c in candidates)
                if (c.Volume > 0f) bestFill = System.Math.Max(bestFill, hull.Volume / c.Volume);

            foreach (var c in candidates)
            {
                if (c.Volume <= 0f) continue;
                var f = hull.Volume / c.Volume;
                if (f >= bestFill * CheaperShapeTolerance)
                {
                    fill = f;
                    return c;
                }
            }
            return null;
        }

        /// <summary>Primitive replacing the hull, or null when none fills at least <paramref name="threshold"/>.</summary>
        public static FittedShape TryReplace(ConvexHull hull, FitContext context, float threshold)
        {
            if (threshold >= 1f || hull == null) return null;
            var input = FitInput.Create(new MeshGeometry("part", hull.Vertices, hull.Triangles));
            var primitive = BestFit(input, context, out var fill);
            return primitive != null && fill >= threshold ? primitive : null;
        }
    }
}
