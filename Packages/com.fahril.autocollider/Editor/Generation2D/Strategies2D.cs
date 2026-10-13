using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Turns one 2D outline into 2D collider shapes. Pure: never touches the scene.</summary>
    internal interface IColliderStrategy2D
    {
        List<FittedShape2D> Fit(Outline2D outline, FitContext context);
    }

    /// <summary>Strategy lookup and the shared fitting helpers.</summary>
    internal static class Strategies2D
    {
        /// <summary>Cheaper primitives win when they are within this much of the tightest area.</summary>
        private const float CheaperWins = 1.02f;

        /// <summary>A rotated box needs its own child object; only take it when clearly tighter.</summary>
        private const float RotationGain = 0.97f;

        public static IColliderStrategy2D Get(ColliderStrategy2D kind, GenerationSettings settings) => kind switch
        {
            ColliderStrategy2D.Box or ColliderStrategy2D.Circle or ColliderStrategy2D.Capsule or ColliderStrategy2D.BestPrimitive
                => new PrimitiveStrategy2D(kind),
            ColliderStrategy2D.ConvexPolygon or ColliderStrategy2D.Polygon or ColliderStrategy2D.Edge
                => new PolygonStrategy2D(kind, settings),
            _ => new AutoStrategy2D(settings),
        };

        /// <summary>Outline simplification tolerance: Quality 1 keeps nearly every corner, 0 simplifies hard.</summary>
        public static float Tolerance(Outline2D outline, GenerationSettings settings) =>
            outline.Diagonal * math.lerp(0.02f, 0.0015f, math.saturate(settings.Quality));

        public static FittedShape2D Box(Outline2D outline, FitContext context)
        {
            var points = outline.OuterPoints;
            var aabb = Box2DFitter.Aabb(points);
            if (!context.AllowRotation) return FittedShape2D.FromBox(aabb);
            var oriented = Box2DFitter.MinArea(outline.Hull, points);
            var useOriented = oriented.Area < aabb.Area * RotationGain && math.abs(oriented.Angle) > 0.5f;
            return FittedShape2D.FromBox(useOriented ? oriented : aabb);
        }

        public static FittedShape2D Circle(Outline2D outline) => FittedShape2D.FromCircle(CircleFitter.Welzl(outline.OuterPoints));

        public static FittedShape2D Capsule(Outline2D outline, FitContext context) =>
            FittedShape2D.FromCapsule(Capsule2DFitter.Best(outline.OuterPoints, context.AllowRotation));

        /// <summary>A capsule whose straight part is this short (× radius) is really a circle — and a circle is cheaper.</summary>
        private const float CircularCapsule = 0.1f;

        private static FittedShape2D CircleIfRound(FittedShape2D capsule) =>
            capsule.Height - 2f * capsule.Radius < CircularCapsule * capsule.Radius
                ? FittedShape2D.FromCircle(new CircleFit { Center = capsule.Center, Radius = capsule.Radius })
                : capsule;

        /// <summary>Tightest of circle / capsule / box (cheaper wins near-ties); <paramref name="fill"/> = outline area ÷ shape area.</summary>
        public static FittedShape2D BestPrimitive(Outline2D outline, FitContext context, out float fill)
        {
            var candidates = new[] { Circle(outline), CircleIfRound(Capsule(outline, context)), Box(outline, context) };
            var best = candidates[0];
            foreach (var c in candidates)
                if (c.Area * CheaperWins < best.Area)
                    best = c;
            fill = best.Area > 0f ? math.saturate(outline.Area / best.Area) : 0f;
            return best;
        }

        public static FittedShape2D Polygon(Outline2D outline, GenerationSettings settings)
        {
            var loops = PolygonSimplifier.Simplify(outline.Loops, Tolerance(outline, settings), settings.MinIslandSize * outline.Diagonal);
            return loops.Count == 0 ? null : FittedShape2D.FromPaths(loops.ToArray(), false);
        }

        public static FittedShape2D ConvexPolygon(Outline2D outline, GenerationSettings settings)
        {
            var hull = outline.HullLoop();
            if (hull.Length < 3) return null;
            var simplified = PolygonSimplifier.Simplify(new[] { hull }, Tolerance(outline, settings));
            return simplified.Count == 0 ? null : FittedShape2D.FromPaths(new[] { simplified[0] }, false);
        }

        public static List<FittedShape2D> Edges(Outline2D outline, GenerationSettings settings)
        {
            var shapes = new List<FittedShape2D>();
            var loops = PolygonSimplifier.Simplify(outline.Loops, Tolerance(outline, settings), settings.MinIslandSize * outline.Diagonal);
            foreach (var loop in loops) shapes.Add(FittedShape2D.FromPaths(new[] { loop }, true));
            return shapes;
        }

        public static string KindName(ShapeKind2D kind) => kind switch
        {
            ShapeKind2D.Box => Strings.KindBox,
            ShapeKind2D.Circle => Strings.KindCircle,
            ShapeKind2D.Capsule => Strings.KindCapsule,
            ShapeKind2D.Edge => Strings.KindEdge,
            _ => Strings.KindPolygon,
        };
    }

    /// <summary>Box / circle / capsule, or the best of the three.</summary>
    internal sealed class PrimitiveStrategy2D : IColliderStrategy2D
    {
        private readonly ColliderStrategy2D _kind;

        public PrimitiveStrategy2D(ColliderStrategy2D kind)
        {
            _kind = kind;
        }

        public List<FittedShape2D> Fit(Outline2D outline, FitContext context)
        {
            var shape = _kind switch
            {
                ColliderStrategy2D.Circle => Strategies2D.Circle(outline),
                ColliderStrategy2D.Capsule => Strategies2D.Capsule(outline, context),
                ColliderStrategy2D.Box => Strategies2D.Box(outline, context),
                _ => Strategies2D.BestPrimitive(outline, context, out _),
            };
            return new List<FittedShape2D> { shape };
        }
    }

    /// <summary>Polygons from the simplified outline: one convex hull polygon, the outline itself (holes too), or edge loops.</summary>
    internal sealed class PolygonStrategy2D : IColliderStrategy2D
    {
        private readonly ColliderStrategy2D _kind;
        private readonly GenerationSettings _settings;

        public PolygonStrategy2D(ColliderStrategy2D kind, GenerationSettings settings)
        {
            _kind = kind;
            _settings = settings;
        }

        public List<FittedShape2D> Fit(Outline2D outline, FitContext context)
        {
            if (_kind == ColliderStrategy2D.Edge)
            {
                var edges = Strategies2D.Edges(outline, _settings);
                return edges.Count > 0 ? edges : new List<FittedShape2D> { Strategies2D.Box(outline, context) };
            }
            var shape = _kind == ColliderStrategy2D.ConvexPolygon
                ? Strategies2D.ConvexPolygon(outline, _settings)
                : Strategies2D.Polygon(outline, _settings);
            return new List<FittedShape2D> { shape ?? Strategies2D.Box(outline, context) };
        }
    }

    /// <summary>
    /// The cheapest 2D collider that fits: a primitive filling ≥ Primitive Fit Threshold of it, else one convex polygon when
    /// the outline is convex enough, else the simplified outline polygon.
    /// </summary>
    internal sealed class AutoStrategy2D : IColliderStrategy2D
    {
        private readonly GenerationSettings _settings;

        public AutoStrategy2D(GenerationSettings settings)
        {
            _settings = settings;
        }

        public List<FittedShape2D> Fit(Outline2D outline, FitContext context)
        {
            if (outline.IsEmpty) return Tag(Strategies2D.Box(outline, context), Strings.KindBox);

            var primitive = Strategies2D.BestPrimitive(outline, context, out var fill);
            if (fill >= _settings.PrimitiveFitThreshold) return Tag(primitive, Strategies2D.KindName(primitive.Kind));

            if (outline.Convexity >= _settings.ConvexityThreshold)
            {
                var convex = Strategies2D.ConvexPolygon(outline, _settings);
                if (convex != null) return Tag(convex, Strings.KindConvexPolygon);
            }

            var polygon = Strategies2D.Polygon(outline, _settings);
            return polygon != null ? Tag(polygon, Strings.KindPolygon) : Tag(primitive, Strategies2D.KindName(primitive.Kind));
        }

        private static List<FittedShape2D> Tag(FittedShape2D shape, string choice)
        {
            shape.Note = string.Format(Strings.NoteAutoChose, choice);
            return new List<FittedShape2D> { shape };
        }
    }
}
