using System.Collections.Generic;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Builds the collider(s) of a <see cref="PaintedRegion"/>. Used by both the paint tool and generation, so painting a
    /// region and regenerating it later give identical colliders.
    /// </summary>
    internal static class RegionFitter
    {
        /// <summary>The selected triangles of <paramref name="source"/> as their own compact mesh. Out-of-range indices are counted.</summary>
        public static MeshGeometry SubMesh(MeshGeometry source, IReadOnlyList<int> triangles, out int invalid)
        {
            invalid = 0;
            var indices = new List<int>(triangles.Count * 3);
            var triangleCount = source.TriangleCount;
            foreach (var t in triangles)
            {
                if (t < 0 || t >= triangleCount)
                {
                    invalid++;
                    continue;
                }
                indices.Add(source.Triangles[t * 3]);
                indices.Add(source.Triangles[t * 3 + 1]);
                indices.Add(source.Triangles[t * 3 + 2]);
            }
            MeshSource.CompactReferenced(source.Vertices, indices, out var vertices, out var compact);
            return new MeshGeometry(source.Label, vertices, compact);
        }

        /// <summary>
        /// Shapes for the region (in the target's scaled space), labelled, inflated and tagged with the region id —
        /// exactly as generation produces them.
        /// </summary>
        public static List<FittedShape> Fit(PaintedRegion region, MeshGeometry source, GenerationSettings settings, FitContext context,
            out int invalidTriangles)
        {
            var sub = SubMesh(source, region.Triangles, out invalidTriangles);
            var shapes = new List<FittedShape>();
            if (sub.Vertices.Length < PhysicsLimits.MinHullVertices) return shapes;

            var input = FitInput.Create(new MeshGeometry(region.Label, sub.Vertices, sub.Triangles));
            switch (region.Shape)
            {
                case PartShape.Box:
                    shapes.Add(BoxStrategy.FitBox(input, context));
                    break;
                case PartShape.Sphere:
                    shapes.Add(SphereStrategy.FitSphere(input));
                    break;
                case PartShape.Capsule:
                    shapes.Add(CapsuleStrategy.FitCapsule(input, context));
                    break;
                case PartShape.ConvexHull:
                    shapes.AddRange(new ConvexHullStrategy().Fit(input, context));
                    break;
                case PartShape.ConvexParts:
                    shapes.AddRange(new ConvexDecompositionStrategy(settings).Fit(input, context));
                    break;
                default:
                    shapes.AddRange(new AutoStrategy(settings).Fit(input, context));
                    break;
            }

            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                shape.Label = shapes.Count == 1 ? region.Label : $"{region.Label} {i + 1}";
                shape.RegionId = region.Id;
                if (shape.Kind != ShapeKind.ConcaveMesh) InflateDeflate.Apply(shape, settings.Inflate);
            }
            return shapes;
        }

        /// <summary>Default name for the next region of a group.</summary>
        public static string NextLabel(GenerationRecipe recipe) => string.Format(Strings.RegionLabel, recipe.Regions.Count + 1);

        /// <summary>Distinct triangle numbers in ascending order (stable regions regardless of paint order).</summary>
        internal static int[] Sorted(IEnumerable<int> triangles)
        {
            var list = new List<int>(new HashSet<int>(triangles));
            list.Sort();
            return list.ToArray();
        }
    }
}
