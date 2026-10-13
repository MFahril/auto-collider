using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal struct CircleFit
    {
        public float2 Center;
        public float Radius;
        public float Area => math.PI * Radius * Radius;
    }

    /// <summary>Smallest enclosing circle (Welzl, iterative; fixed-seed shuffle so results are deterministic).</summary>
    internal static class CircleFitter
    {
        private const int Seed = 1337;
        private const double Epsilon = 1e-9;

        public static CircleFit Welzl(IReadOnlyList<float2> input)
        {
            if (input.Count == 0) return default;
            var points = new double2[input.Count];
            for (var i = 0; i < points.Length; i++) points[i] = input[i];
            var random = new System.Random(Seed);
            for (var i = points.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (points[i], points[j]) = (points[j], points[i]);
            }

            var center = points[0];
            var radius = 0.0;
            for (var i = 1; i < points.Length; i++)
            {
                if (Inside(points[i], center, radius)) continue;
                center = points[i];
                radius = 0;
                for (var j = 0; j < i; j++)
                {
                    if (Inside(points[j], center, radius)) continue;
                    center = (points[i] + points[j]) * 0.5;
                    radius = math.distance(points[i], points[j]) * 0.5;
                    for (var k = 0; k < j; k++)
                    {
                        if (Inside(points[k], center, radius)) continue;
                        Circumcircle(points[i], points[j], points[k], out center, out radius);
                    }
                }
            }
            return new CircleFit { Center = (float2)center, Radius = math.max((float)radius, PhysicsLimits.MinExtent) };
        }

        private static bool Inside(double2 p, double2 c, double r) => math.distance(p, c) <= r + Epsilon * math.max(1.0, r);

        private static void Circumcircle(double2 a, double2 b, double2 c, out double2 center, out double radius)
        {
            var d = 2 * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
            if (math.abs(d) < 1e-18)
            {
                // Collinear: the two furthest points span the circle.
                var ab = math.distance(a, b);
                var ac = math.distance(a, c);
                var bc = math.distance(b, c);
                if (ab >= ac && ab >= bc) { center = (a + b) * 0.5; radius = ab * 0.5; }
                else if (ac >= bc) { center = (a + c) * 0.5; radius = ac * 0.5; }
                else { center = (b + c) * 0.5; radius = bc * 0.5; }
                return;
            }
            var a2 = math.lengthsq(a);
            var b2 = math.lengthsq(b);
            var c2 = math.lengthsq(c);
            center = new double2(
                (a2 * (b.y - c.y) + b2 * (c.y - a.y) + c2 * (a.y - b.y)) / d,
                (a2 * (c.x - b.x) + b2 * (a.x - c.x) + c2 * (b.x - a.x)) / d);
            radius = math.distance(center, a);
        }
    }
}
