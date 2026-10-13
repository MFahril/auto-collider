using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// A 2D capsule (stadium): runs along its own vertical axis when <see cref="Vertical"/>, else horizontal, after rotating
    /// by <see cref="Angle"/> degrees. <see cref="Height"/> includes both caps.
    /// </summary>
    internal struct Capsule2DFit
    {
        public float2 Center;
        public float Angle;
        public float Radius;
        public float Height;
        public bool Vertical;

        public float Area => 2f * Radius * (Height - 2f * Radius) + math.PI * Radius * Radius;
    }

    /// <summary>Enclosing capsules for 2D points: minimal radius across the axis, shortest segment that keeps every point inside the caps.</summary>
    internal static class Capsule2DFitter
    {
        /// <summary>
        /// Points within this fraction of the radius past a cap still count as covered (about one pixel on a 24-pixel radius).
        /// Traced pixel outlines put stair corners
        /// exactly at the widest point, where a cap has no room; without slack every such corner stretches the capsule by a
        /// pixel row (a 0.375 × 2.5 pill came out 2.75 long).
        /// </summary>
        private const float CapTolerance = 0.04f;

        /// <summary>Best of horizontal / vertical, and the principal axis when <paramref name="allowRotation"/>.</summary>
        public static Capsule2DFit Best(IReadOnlyList<float2> points, bool allowRotation)
        {
            var best = Along(points, new float2(0, 1));
            var horizontal = Along(points, new float2(1, 0));
            if (horizontal.Area < best.Area) best = horizontal;
            if (!allowRotation) return best;

            var principal = Along(points, PrincipalAxis(points));
            // A rotated capsule needs its own child object; only take it when clearly tighter.
            if (principal.Area < best.Area * 0.99f && math.abs(principal.Angle) > 0.5f) best = principal;
            return best;
        }

        /// <summary>Capsule whose axis runs along unit <paramref name="axis"/>.</summary>
        public static Capsule2DFit Along(IReadOnlyList<float2> points, float2 axis)
        {
            axis = math.normalize(axis);
            var normal = new float2(-axis.y, axis.x);
            float sMin = float.MaxValue, sMax = float.MinValue;
            foreach (var p in points)
            {
                var s = math.dot(p, normal);
                sMin = math.min(sMin, s);
                sMax = math.max(sMax, s);
            }
            var radius = math.max((sMax - sMin) * 0.5f, PhysicsLimits.MinExtent);
            var s0 = (sMin + sMax) * 0.5f;
            var capRadius = radius * (1f + CapTolerance);

            // Each point is covered when its axis coordinate lies within [a − h, b + h], h = √(r² − Δs²).
            float a = float.MaxValue, b = float.MinValue;
            foreach (var p in points)
            {
                var t = math.dot(p, axis);
                var ds = math.dot(p, normal) - s0;
                var h = math.sqrt(math.max(capRadius * capRadius - ds * ds, 0f));
                a = math.min(a, t + h);
                b = math.max(b, t - h);
            }
            if (a > b) a = b = (a + b) * 0.5f;

            var center = axis * ((a + b) * 0.5f) + normal * s0;
            var height = (b - a) + 2f * radius;
            return ToFit(center, axis, radius, height);
        }

        private static Capsule2DFit ToFit(float2 center, float2 axis, float radius, float height)
        {
            // Express the axis as an unrotated vertical / horizontal capsule when it is (nearly) one.
            if (math.abs(axis.x) < 1e-4f) return new Capsule2DFit { Center = center, Radius = radius, Height = height, Vertical = true };
            if (math.abs(axis.y) < 1e-4f) return new Capsule2DFit { Center = center, Radius = radius, Height = height, Vertical = false };
            var angle = math.degrees(math.atan2(axis.y, axis.x)) - 90f; // rotate the vertical axis onto `axis`
            angle -= 180f * math.round(angle / 180f);
            return new Capsule2DFit { Center = center, Angle = angle, Radius = radius, Height = height, Vertical = true };
        }

        /// <summary>Direction of largest spread (2×2 covariance eigenvector).</summary>
        public static float2 PrincipalAxis(IReadOnlyList<float2> points)
        {
            var mean = float2.zero;
            foreach (var p in points) mean += p;
            mean /= math.max(points.Count, 1);
            double xx = 0, yy = 0, xy = 0;
            foreach (var p in points)
            {
                var d = p - mean;
                xx += d.x * d.x;
                yy += d.y * d.y;
                xy += d.x * d.y;
            }
            var angle = 0.5 * math.atan2(2 * xy, xx - yy);
            return new float2((float)math.cos(angle), (float)math.sin(angle));
        }
    }
}
