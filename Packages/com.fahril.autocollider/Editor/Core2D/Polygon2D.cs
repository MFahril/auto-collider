using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Closed-loop polygon helpers. Convention everywhere in 2D: outer loops counter-clockwise (positive area),
    /// holes clockwise; loops are filled with the even-odd rule.
    /// </summary>
    internal static class Polygon2D
    {
        /// <summary>Shoelace area: positive for counter-clockwise loops.</summary>
        public static float SignedArea(IReadOnlyList<float2> loop)
        {
            double sum = 0;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                sum += (double)loop[j].x * loop[i].y - (double)loop[i].x * loop[j].y;
            return (float)(sum * 0.5);
        }

        /// <summary>Filled area of oriented loops (holes subtract).</summary>
        public static float Area(IReadOnlyList<float2[]> loops)
        {
            var area = 0f;
            foreach (var loop in loops) area += SignedArea(loop);
            return area;
        }

        /// <summary>Even-odd point test against all loops.</summary>
        public static bool Contains(IReadOnlyList<float2[]> loops, float2 p)
        {
            var inside = false;
            foreach (var loop in loops)
                if (InLoop(loop, p)) inside = !inside;
            return inside;
        }

        public static bool InLoop(IReadOnlyList<float2> loop, float2 p)
        {
            var inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var a = loop[i];
                var b = loop[j];
                if (a.y > p.y == b.y > p.y) continue;
                var x = a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y);
                if (p.x < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Re-orient loops by nesting depth: a loop inside an even number of others is an outer loop (CCW), inside an odd
        /// number a hole (CW). Sources such as sprite physics shapes come in arbitrary winding.
        /// </summary>
        public static List<float2[]> OrientByNesting(IReadOnlyList<float2[]> loops)
        {
            var result = new List<float2[]>(loops.Count);
            for (var i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                if (loop.Length < 3) continue;
                var depth = 0;
                for (var j = 0; j < loops.Count; j++)
                    if (j != i && loops[j].Length >= 3 && InLoop(loops[j], loop[0]))
                        depth++;
                var wantCcw = depth % 2 == 0;
                var copy = (float2[])loop.Clone();
                if (SignedArea(copy) > 0f != wantCcw) System.Array.Reverse(copy);
                result.Add(copy);
            }
            return result;
        }

        public static void Bounds(IReadOnlyList<float2[]> loops, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue);
            max = new float2(float.MinValue);
            foreach (var loop in loops)
            foreach (var p in loop)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }
            if (min.x > max.x) min = max = float2.zero;
        }

        /// <summary>True when segments ab and cd cross (touching at shared endpoints doesn't count).</summary>
        public static bool SegmentsCross(float2 a, float2 b, float2 c, float2 d)
        {
            var d1 = Cross(c, d, a);
            var d2 = Cross(c, d, b);
            var d3 = Cross(a, b, c);
            var d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(float2 o, float2 a, float2 b) =>
            ((double)a.x - o.x) * ((double)b.y - o.y) - ((double)a.y - o.y) * ((double)b.x - o.x);

        /// <summary>Loops up to this many edges are checked pair by pair; longer ones through a uniform grid.</summary>
        private const int BruteForceEdges = 96;

        /// <summary>No two non-adjacent edges of the loop cross.</summary>
        public static bool IsSimple(IReadOnlyList<float2> loop)
        {
            var n = loop.Count;
            if (n < 3) return false;
            return n <= BruteForceEdges ? IsSimpleBrute(loop) : IsSimpleGrid(loop);
        }

        private static bool IsSimpleBrute(IReadOnlyList<float2> loop)
        {
            var n = loop.Count;
            for (var i = 0; i < n; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % n];
                for (var j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegmentsCross(a, b, loop[j], loop[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Same answer as the pairwise check in about O(n) for outlines: every edge is binned into the grid cells its bounding
        /// box covers (≈ n cells over the loop's bounds), and only edges sharing a cell are tested.
        /// </summary>
        private static bool IsSimpleGrid(IReadOnlyList<float2> loop)
        {
            var n = loop.Count;
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            for (var i = 0; i < n; i++)
            {
                min = math.min(min, loop[i]);
                max = math.max(max, loop[i]);
            }
            var side = (int)math.ceil(math.sqrt(n));
            var size = math.max(max - min, new float2(1e-6f));
            var scale = side / size;

            var cells = new Dictionary<int, List<int>>(n);
            for (var i = 0; i < n; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % n];
                var lo = math.clamp((int2)math.floor((math.min(a, b) - min) * scale), 0, side - 1);
                var hi = math.clamp((int2)math.floor((math.max(a, b) - min) * scale), 0, side - 1);
                for (var y = lo.y; y <= hi.y; y++)
                for (var x = lo.x; x <= hi.x; x++)
                {
                    var key = y * side + x;
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>(4);
                    list.Add(i);
                }
            }

            foreach (var list in cells.Values)
            {
                for (var p = 0; p < list.Count; p++)
                for (var q = p + 1; q < list.Count; q++)
                {
                    int i = list[p], j = list[q];
                    var gap = math.abs(i - j);
                    if (gap <= 1 || gap == n - 1) continue; // adjacent edges share an endpoint
                    if (SegmentsCross(loop[i], loop[(i + 1) % n], loop[j], loop[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        /// <summary>No edge of <paramref name="a"/> crosses an edge of <paramref name="b"/>.</summary>
        public static bool Disjoint(IReadOnlyList<float2> a, IReadOnlyList<float2> b)
        {
            for (var i = 0; i < a.Count; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Count];
                for (var j = 0; j < b.Count; j++)
                    if (SegmentsCross(p, q, b[j], b[(j + 1) % b.Count]))
                        return false;
            }
            return true;
        }

        /// <summary>
        /// Move every vertex outwards (+) or inwards (−) by <paramref name="distance"/> along its corner bisector
        /// (miter, limited to 2× so sharp corners don't spike). Orientation decides "outwards": holes shrink when the outline grows.
        /// </summary>
        public static float2[] Offset(float2[] loop, float distance)
        {
            var n = loop.Length;
            var result = new float2[n];
            for (var i = 0; i < n; i++)
            {
                var prev = loop[(i + n - 1) % n];
                var cur = loop[i];
                var next = loop[(i + 1) % n];
                // Outward normal of a CCW edge (dx, dy) is (dy, −dx).
                var e0 = math.normalizesafe(cur - prev);
                var e1 = math.normalizesafe(next - cur);
                var n0 = new float2(e0.y, -e0.x);
                var n1 = new float2(e1.y, -e1.x);
                var bisector = math.normalizesafe(n0 + n1, n1);
                var cos = math.max(math.dot(bisector, n1), 0.5f);
                result[i] = cur + bisector * (distance / cos);
            }
            return result;
        }

        /// <summary>Drop vertices that lie on the line through their neighbors (within <paramref name="epsilon"/>).</summary>
        public static float2[] RemoveCollinear(float2[] loop, float epsilon)
        {
            var points = new List<float2>(loop);
            var changed = true;
            while (changed && points.Count > 3)
            {
                changed = false;
                for (var i = 0; i < points.Count && points.Count > 3; i++)
                {
                    var prev = points[(i + points.Count - 1) % points.Count];
                    var next = points[(i + 1) % points.Count];
                    if (DistanceToSegment(points[i], prev, next) > epsilon) continue;
                    points.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }
            return points.ToArray();
        }

        public static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var lengthSq = math.lengthsq(ab);
            var t = lengthSq > 0f ? math.saturate(math.dot(p - a, ab) / lengthSq) : 0f;
            return math.distance(p, a + ab * t);
        }
    }
}
