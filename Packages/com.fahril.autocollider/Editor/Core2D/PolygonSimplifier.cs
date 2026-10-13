using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Reduces outline loops to few points: Douglas–Peucker on each closed loop, then a validity check — a loop that would
    /// cross itself, cross another loop or flip its winding is retried with half the tolerance (and kept unsimplified as a
    /// last resort). Loops smaller than the island size are dropped. Deterministic.
    /// </summary>
    internal static class PolygonSimplifier
    {
        private const int MaxRetries = 5;

        /// <summary>Above this many edges in total, loops are only checked against themselves (cross-loop check is O(n²)).</summary>
        private const int CrossCheckLimit = 4000;

        public static List<float2[]> Simplify(IReadOnlyList<float2[]> loops, float tolerance, float minIslandSize = 0f)
        {
            var result = new List<float2[]>(loops.Count);
            var totalEdges = 0;
            foreach (var loop in loops) totalEdges += loop.Length;
            var crossCheck = totalEdges <= CrossCheckLimit;

            foreach (var loop in loops)
            {
                if (loop.Length < 3) continue;
                if (minIslandSize > 0f && Diagonal(loop) < minIslandSize) continue;

                var sign = math.sign(Polygon2D.SignedArea(loop));
                float2[] simplified = null;
                var tol = tolerance;
                for (var attempt = 0; attempt <= MaxRetries && tol > 0f; attempt++, tol *= 0.5f)
                {
                    var candidate = Polygon2D.RemoveCollinear(Closed(loop, tol), tol * 0.01f);
                    if (candidate.Length >= 3
                        && math.sign(Polygon2D.SignedArea(candidate)) == sign
                        && Polygon2D.IsSimple(candidate)
                        && (!crossCheck || DisjointFromAll(candidate, result)))
                    {
                        simplified = candidate;
                        break;
                    }
                }
                result.Add(simplified ?? Polygon2D.RemoveCollinear(loop, 0f));
            }
            return result;
        }

        private static bool DisjointFromAll(float2[] loop, List<float2[]> others)
        {
            foreach (var other in others)
                if (!Polygon2D.Disjoint(loop, other))
                    return false;
            return true;
        }

        /// <summary>Douglas–Peucker on a closed loop, anchored at the lowest point and the point furthest from it.</summary>
        public static float2[] Closed(float2[] loop, float tolerance)
        {
            var n = loop.Length;
            if (n <= 3 || tolerance <= 0f) return (float2[])loop.Clone();

            var a = 0;
            for (var i = 1; i < n; i++)
                if (loop[i].y < loop[a].y || (loop[i].y == loop[a].y && loop[i].x < loop[a].x))
                    a = i;
            var b = a;
            var far = -1f;
            for (var i = 0; i < n; i++)
            {
                var d = math.distancesq(loop[i], loop[a]);
                if (d > far)
                {
                    far = d;
                    b = i;
                }
            }

            var keep = new bool[n];
            keep[a] = keep[b] = true;
            Mark(loop, a, b, tolerance, keep);
            Mark(loop, b, a, tolerance, keep);

            var result = new List<float2>();
            for (var k = 0; k < n; k++)
            {
                var i = (a + k) % n;
                if (keep[i]) result.Add(loop[i]);
            }
            return result.ToArray();
        }

        /// <summary>Iterative Douglas–Peucker over the open run from <paramref name="from"/> to <paramref name="to"/> (wrapping).</summary>
        private static void Mark(float2[] loop, int from, int to, float tolerance, bool[] keep)
        {
            var n = loop.Length;
            var stack = new Stack<int2>();
            stack.Push(new int2(from, to));
            while (stack.Count > 0)
            {
                var span = stack.Pop();
                var count = (span.y - span.x + n) % n;
                if (count < 2) continue;

                var worst = -1;
                var worstDistance = tolerance;
                for (var k = 1; k < count; k++)
                {
                    var i = (span.x + k) % n;
                    var d = Polygon2D.DistanceToSegment(loop[i], loop[span.x], loop[span.y]);
                    if (d <= worstDistance) continue;
                    worstDistance = d;
                    worst = i;
                }
                if (worst < 0) continue;
                keep[worst] = true;
                stack.Push(new int2(span.x, worst));
                stack.Push(new int2(worst, span.y));
            }
        }

        private static float Diagonal(float2[] loop)
        {
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var p in loop)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }
            return math.distance(min, max);
        }
    }
}
