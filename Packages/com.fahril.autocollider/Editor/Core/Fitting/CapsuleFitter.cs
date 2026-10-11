using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal struct CapsuleFit
    {
        public float3 Center;
        /// <summary>Identity for axis-aligned capsules (axis = <see cref="Direction"/>); otherwise maps local Y to the capsule axis.</summary>
        public quaternion Rotation;
        public int Direction;
        public float Radius;
        public float Height;
        public float Volume;
    }

    /// <summary>Enclosing capsule fitters.</summary>
    internal static class CapsuleFitter
    {
        private static readonly float3[] Axes = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };

        /// <summary>Best of the three local axes (no rotation).</summary>
        public static CapsuleFit BestAxisAligned(IReadOnlyList<float3> points)
        {
            var best = Along(points, 0);
            for (var axis = 1; axis < 3; axis++)
            {
                var candidate = Along(points, axis);
                if (candidate.Volume < best.Volume) best = candidate;
            }
            return best;
        }

        /// <summary>Best of the local axes and the principal (PCA) axis. Axis-aligned wins unless the oriented fit is clearly tighter.</summary>
        public static CapsuleFit BestOriented(IReadOnlyList<float3> points)
        {
            var best = BestAxisAligned(points);
            var covariance = GeometryUtils.Covariance(points, out _);
            GeometryUtils.EigenSymmetric(covariance, out _, out var vectors);
            var principal = math.normalize((float3)vectors.c0);

            var oriented = AlongDirection(points, principal);
            if (oriented.Volume < best.Volume * 0.99f)
            {
                oriented.Rotation = FromToY(principal);
                oriented.Direction = 1;
                best = oriented;
            }
            return best;
        }

        /// <summary>Capsule along local axis 0/1/2.</summary>
        public static CapsuleFit Along(IReadOnlyList<float3> points, int axis)
        {
            var fit = AlongDirection(points, Axes[axis]);
            fit.Rotation = quaternion.identity;
            fit.Direction = axis;
            return fit;
        }

        /// <summary>
        /// Tight capsule around the points along <paramref name="axis"/>: radius from the minimal enclosing circle of the
        /// projection, then the shortest segment such that every point lies within the rounded caps.
        /// </summary>
        public static CapsuleFit AlongDirection(IReadOnlyList<float3> points, float3 axis)
        {
            axis = math.normalize(axis);
            GeometryUtils.Basis(axis, out var u, out var v);

            var planar = new float3[points.Count];
            for (var i = 0; i < points.Count; i++)
                planar[i] = new float3(math.dot(points[i], u), math.dot(points[i], v), 0);
            var circle = SphereFitter.Welzl(planar);
            var radius = math.max(circle.Radius, PhysicsLimits.MinExtent);
            var c2 = circle.Center.xy;

            var segmentEnd = float.MinValue;   // segment top must be at least t - h for every point
            var segmentStart = float.MaxValue; // segment bottom must be at most t + h for every point
            var rSq = radius * radius;
            for (var i = 0; i < points.Count; i++)
            {
                var t = math.dot(points[i], axis);
                var dSq = math.distancesq(planar[i].xy, c2);
                var h = math.sqrt(math.max(0f, rSq - dSq));
                segmentEnd = math.max(segmentEnd, t - h);
                segmentStart = math.min(segmentStart, t + h);
            }

            if (segmentStart > segmentEnd)
            {
                // Shorter than a sphere: collapse the segment to its midpoint.
                var mid = (segmentStart + segmentEnd) * 0.5f;
                segmentStart = segmentEnd = mid;
            }

            var length = segmentEnd - segmentStart;
            var centerT = (segmentStart + segmentEnd) * 0.5f;
            return new CapsuleFit
            {
                Center = u * c2.x + v * c2.y + axis * centerT,
                Rotation = quaternion.identity,
                Direction = 1,
                Radius = radius,
                Height = length + 2f * radius,
                Volume = math.PI * rSq * length + 4f / 3f * math.PI * rSq * radius,
            };
        }

        private static quaternion FromToY(float3 direction)
        {
            var up = new float3(0, 1, 0);
            var d = math.dot(up, direction);
            if (d > 0.999999f) return quaternion.identity;
            if (d < -0.999999f) return quaternion.AxisAngle(new float3(1, 0, 0), math.PI);
            var axis = math.normalize(math.cross(up, direction));
            return quaternion.AxisAngle(axis, math.acos(math.clamp(d, -1f, 1f)));
        }
    }
}
