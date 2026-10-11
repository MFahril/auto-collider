using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal struct SphereFit
    {
        public float3 Center;
        public float Radius;
        public float Volume => 4f / 3f * math.PI * Radius * Radius * Radius;
    }

    /// <summary>Enclosing sphere fitters.</summary>
    internal static class SphereFitter
    {
        private const double Tolerance = 1e-7;

        /// <summary>Ritter's approximate bounding sphere (fast, up to ~5–20% larger than minimal).</summary>
        public static SphereFit Ritter(IReadOnlyList<float3> points)
        {
            var x = points[0];
            var y = Furthest(points, x);
            var z = Furthest(points, y);
            var center = (y + z) * 0.5f;
            var radius = math.distance(y, z) * 0.5f;

            for (var i = 0; i < points.Count; i++)
            {
                var d = math.distance(points[i], center);
                if (d <= radius) continue;
                var newRadius = (radius + d) * 0.5f;
                center += (points[i] - center) * ((newRadius - radius) / d);
                radius = newRadius;
            }
            return new SphereFit { Center = center, Radius = math.max(radius, PhysicsLimits.MinExtent) };
        }

        /// <summary>
        /// Minimal enclosing sphere (Welzl, iterative). Points are shuffled with a fixed seed so results are deterministic.
        /// Pass hull vertices for speed — the minimal sphere of a hull equals that of the whole mesh.
        /// </summary>
        public static SphereFit Welzl(IReadOnlyList<float3> points)
        {
            var p = new double3[points.Count];
            for (var i = 0; i < p.Length; i++) p[i] = points[i];

            var random = new Random(0x9E3779B9u);
            for (var i = p.Length - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }

            var s = new Ball(p[0], 0);
            for (var i = 1; i < p.Length; i++)
            {
                if (s.Contains(p[i])) continue;
                s = new Ball(p[i], 0);
                for (var j = 0; j < i; j++)
                {
                    if (s.Contains(p[j])) continue;
                    s = Ball.From2(p[i], p[j]);
                    for (var k = 0; k < j; k++)
                    {
                        if (s.Contains(p[k])) continue;
                        s = Ball.From3(p[i], p[j], p[k]);
                        for (var l = 0; l < k; l++)
                        {
                            if (s.Contains(p[l])) continue;
                            s = Ball.From4(p[i], p[j], p[k], p[l]);
                        }
                    }
                }
            }

            // Guard against round-off: make sure every point is inside.
            var radius = s.Radius;
            foreach (var q in p) radius = math.max(radius, math.distance(q, s.Center));

            return new SphereFit { Center = (float3)s.Center, Radius = math.max((float)radius, PhysicsLimits.MinExtent) };
        }

        private static float3 Furthest(IReadOnlyList<float3> points, float3 from)
        {
            var best = from;
            var bestSq = -1f;
            for (var i = 0; i < points.Count; i++)
            {
                var d = math.distancesq(points[i], from);
                if (d > bestSq) { bestSq = d; best = points[i]; }
            }
            return best;
        }

        private readonly struct Ball
        {
            public readonly double3 Center;
            public readonly double Radius;

            public Ball(double3 center, double radius)
            {
                Center = center;
                Radius = radius;
            }

            public bool Contains(double3 p) => math.distance(p, Center) <= Radius * (1 + Tolerance) + Tolerance;

            public static Ball From2(double3 a, double3 b) => new((a + b) * 0.5, math.distance(a, b) * 0.5);

            public static Ball From3(double3 a, double3 b, double3 c)
            {
                var ab = b - a;
                var ac = c - a;
                var n = math.cross(ab, ac);
                var denominator = 2 * math.lengthsq(n);
                if (denominator < 1e-24) return Largest(From2(a, b), From2(a, c), From2(b, c));

                var offset = (math.lengthsq(ac) * math.cross(n, ab) + math.lengthsq(ab) * math.cross(ac, n)) / denominator;
                return new Ball(a + offset, math.length(offset));
            }

            public static Ball From4(double3 a, double3 b, double3 c, double3 d)
            {
                var ab = b - a;
                var ac = c - a;
                var ad = d - a;
                var det = math.dot(ab, math.cross(ac, ad));
                if (math.abs(det) < 1e-24)
                {
                    // Coplanar: the smallest circumcircle containing all four.
                    var best = new Ball(a, double.MaxValue);
                    foreach (var candidate in new[] { From3(a, b, c), From3(a, b, d), From3(a, c, d), From3(b, c, d) })
                        if (candidate.Radius < best.Radius && candidate.Contains(a) && candidate.Contains(b) && candidate.Contains(c) && candidate.Contains(d))
                            best = candidate;
                    return best.Radius < double.MaxValue ? best : Largest(From3(a, b, c), From3(a, b, d), From3(a, c, d));
                }

                var offset = (math.lengthsq(ab) * math.cross(ac, ad)
                              + math.lengthsq(ac) * math.cross(ad, ab)
                              + math.lengthsq(ad) * math.cross(ab, ac)) / (2 * det);
                return new Ball(a + offset, math.length(offset));
            }

            private static Ball Largest(params Ball[] balls)
            {
                var best = balls[0];
                foreach (var b in balls)
                    if (b.Radius > best.Radius) best = b;
                return best;
            }
        }
    }
}
