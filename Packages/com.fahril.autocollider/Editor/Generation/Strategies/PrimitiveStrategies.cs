using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Single box: oriented (minimum volume) when allowed and clearly tighter than the axis-aligned box.</summary>
    internal sealed class BoxStrategy : IColliderStrategy
    {
        /// <summary>An oriented box must be at least this much smaller to be worth a rotated child object.</summary>
        public const float RotationGain = 0.97f;

        public List<FittedShape> Fit(FitInput input, FitContext context) => new() { FitBox(input, context) };

        public static FittedShape FitBox(FitInput input, FitContext context)
        {
            var aabb = BoxFitter.Aabb(input.Points);
            if (!context.AllowRotation) return FittedShape.FromBox(aabb);

            var oriented = input.Hull != null ? BoxFitter.MinVolume(input.HullPoints, input.Hull) : BoxFitter.Pca(input.Points);
            // A thinned hull finds the orientation; the extents must still cover every point.
            if (input.IsReduced) oriented = BoxFitter.InFrame(input.Points, new float3x3(oriented.Rotation));
            var useOriented = oriented.Volume < aabb.Volume * RotationGain && !GeometryUtils.IsNearIdentity(oriented.Rotation);
            return FittedShape.FromBox(useOriented ? oriented : aabb);
        }
    }

    /// <summary>Minimal enclosing sphere.</summary>
    internal sealed class SphereStrategy : IColliderStrategy
    {
        public List<FittedShape> Fit(FitInput input, FitContext context) => new() { FitSphere(input) };

        public static FittedShape FitSphere(FitInput input)
        {
            var sphere = SphereFitter.Welzl(input.HullPoints);
            if (input.IsReduced)
                foreach (var p in input.Points)
                    sphere.Radius = math.max(sphere.Radius, math.distance(p, sphere.Center));
            return FittedShape.FromSphere(sphere);
        }
    }

    /// <summary>Capsule along the best local axis, or the principal axis when rotation is allowed.</summary>
    internal sealed class CapsuleStrategy : IColliderStrategy
    {
        public List<FittedShape> Fit(FitInput input, FitContext context) => new() { FitCapsule(input, context) };

        public static FittedShape FitCapsule(FitInput input, FitContext context)
        {
            var fit = context.AllowRotation
                ? CapsuleFitter.BestOriented(input.HullPoints)
                : CapsuleFitter.BestAxisAligned(input.HullPoints);

            if (input.IsReduced)
            {
                // Keep the axis found on the thinned hull, re-fit radius and length over every point.
                var axisLocal = float3.zero;
                axisLocal[fit.Direction] = 1f;
                var refit = CapsuleFitter.AlongDirection(input.Points, math.mul(fit.Rotation, axisLocal));
                refit.Rotation = fit.Rotation;
                refit.Direction = fit.Direction;
                fit = refit;
            }
            return FittedShape.FromCapsule(fit);
        }
    }

    /// <summary>The tightest of sphere, capsule and box. Cheaper shapes win ties (within 2%).</summary>
    internal sealed class BestPrimitiveStrategy : IColliderStrategy
    {
        private const float CheaperShapeTolerance = 1.02f;

        public List<FittedShape> Fit(FitInput input, FitContext context)
        {
            // Ordered cheapest physics cost first.
            var candidates = new[]
            {
                SphereStrategy.FitSphere(input),
                CapsuleStrategy.FitCapsule(input, context),
                BoxStrategy.FitBox(input, context),
            };

            var minVolume = float.MaxValue;
            foreach (var c in candidates) minVolume = System.Math.Min(minVolume, c.Volume);

            foreach (var c in candidates)
                if (c.Volume <= minVolume * CheaperShapeTolerance)
                    return new List<FittedShape> { c };

            return new List<FittedShape> { candidates[2] };
        }
    }

    /// <summary>One convex MeshCollider within the PhysX polygon limit. Falls back to a box for flat geometry.</summary>
    internal sealed class ConvexHullStrategy : IColliderStrategy
    {
        public List<FittedShape> Fit(FitInput input, FitContext context)
        {
            if (input.Hull != null
                && QuickHull3D.TryBuildCapped(input.Hull.Vertices, PhysicsLimits.MaxConvexPolygons, out var capped))
                return new List<FittedShape> { FittedShape.FromHull(capped) };

            return new List<FittedShape> { BoxStrategy.FitBox(input, context) };
        }
    }
}
