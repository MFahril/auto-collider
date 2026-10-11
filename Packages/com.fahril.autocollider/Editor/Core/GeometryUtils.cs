using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>Small, pure geometry helpers shared by the fitters.</summary>
    internal static class GeometryUtils
    {
        /// <summary>Remove exact duplicate points, keeping first-occurrence order (deterministic).</summary>
        public static float3[] Dedupe(IReadOnlyList<float3> points)
        {
            var seen = new HashSet<float3>();
            var result = new List<float3>(points.Count);
            for (var i = 0; i < points.Count; i++)
                if (seen.Add(points[i]))
                    result.Add(points[i]);
            return result.ToArray();
        }

        public static void MinMax(IReadOnlyList<float3> points, out float3 min, out float3 max)
        {
            min = new float3(float.MaxValue);
            max = new float3(float.MinValue);
            for (var i = 0; i < points.Count; i++)
            {
                min = math.min(min, points[i]);
                max = math.max(max, points[i]);
            }
        }

        /// <summary>Covariance matrix of the points (population) and their mean.</summary>
        public static double3x3 Covariance(IReadOnlyList<float3> points, out double3 mean)
        {
            mean = double3.zero;
            for (var i = 0; i < points.Count; i++)
                mean += (double3)points[i];
            mean /= math.max(1, points.Count);

            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var d = (double3)points[i] - mean;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            var n = math.max(1, points.Count);
            return new double3x3(xx / n, xy / n, xz / n,
                                 xy / n, yy / n, yz / n,
                                 xz / n, yz / n, zz / n);
        }

        /// <summary>
        /// Eigen decomposition of a symmetric 3x3 matrix (cyclic Jacobi).
        /// Eigenvectors are the columns of <paramref name="vectors"/>, sorted by descending eigenvalue, right-handed.
        /// </summary>
        public static void EigenSymmetric(double3x3 matrix, out double3 values, out double3x3 vectors)
        {
            var a = new double[3, 3];
            var v = new double[3, 3];
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
            {
                a[r, c] = matrix[c][r];
                v[r, c] = r == c ? 1 : 0;
            }

            for (var sweep = 0; sweep < 50; sweep++)
            {
                var off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-15) break;

                for (var p = 0; p < 2; p++)
                for (var q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;

                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }

            var order = new[] { 0, 1, 2 };
            Array.Sort(order, (i, j) => a[j, j].CompareTo(a[i, i]));

            values = new double3(a[order[0], order[0]], a[order[1], order[1]], a[order[2], order[2]]);
            var c0 = math.normalize(new double3(v[0, order[0]], v[1, order[0]], v[2, order[0]]));
            var c1 = math.normalize(new double3(v[0, order[1]], v[1, order[1]], v[2, order[1]]));
            var c2 = math.cross(c0, c1);
            vectors = new double3x3(c0, c1, c2);
        }

        /// <summary>Any unit vector perpendicular to <paramref name="n"/>, plus the third axis completing a right-handed basis.</summary>
        public static void Basis(float3 n, out float3 u, out float3 v)
        {
            var helper = math.abs(n.x) < 0.9f ? new float3(1, 0, 0) : new float3(0, 1, 0);
            u = math.normalize(math.cross(helper, n));
            v = math.cross(n, u);
        }

        /// <summary>2D convex hull (Andrew's monotone chain), counter-clockwise, no collinear points.</summary>
        public static List<double2> ConvexHull2D(List<double2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<double2>(points.Count + 1);
            if (points.Count < 3)
            {
                hull.AddRange(points);
                return hull;
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var start = hull.Count;
                for (var idx = 0; idx < points.Count; idx++)
                {
                    var p = points[pass == 0 ? idx : points.Count - 1 - idx];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static double Cross(double2 o, double2 a, double2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// Minimum-area enclosing rectangle of a convex polygon (rotating edges).
        /// Returns the unit direction of the rectangle's first axis.
        /// </summary>
        public static double2 MinAreaRectDirection(List<double2> hull)
        {
            if (hull.Count < 2) return new double2(1, 0);

            var bestArea = double.MaxValue;
            var bestDir = new double2(1, 0);
            for (var i = 0; i < hull.Count; i++)
            {
                var edge = hull[(i + 1) % hull.Count] - hull[i];
                var len = math.length(edge);
                if (len < 1e-12) continue;
                var u = edge / len;
                var v = new double2(-u.y, u.x);

                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (var p in hull)
                {
                    var pu = math.dot(p, u);
                    var pv = math.dot(p, v);
                    minU = math.min(minU, pu); maxU = math.max(maxU, pu);
                    minV = math.min(minV, pv); maxV = math.max(maxV, pv);
                }

                var area = (maxU - minU) * (maxV - minV);
                if (area < bestArea - 1e-12)
                {
                    bestArea = area;
                    bestDir = u;
                }
            }
            return bestDir;
        }

        /// <summary>Volume of a closed triangle mesh (absolute value of the signed tetrahedra sum).</summary>
        public static float MeshVolume(IReadOnlyList<float3> vertices, IReadOnlyList<int> triangles)
        {
            double sum = 0;
            for (var i = 0; i + 2 < triangles.Count; i += 3)
            {
                var a = (double3)vertices[triangles[i]];
                var b = (double3)vertices[triangles[i + 1]];
                var c = (double3)vertices[triangles[i + 2]];
                sum += math.dot(a, math.cross(b, c));
            }
            return (float)math.abs(sum / 6.0);
        }

        /// <summary>
        /// Of the 24 axis permutations/sign flips describing the same box, pick the one closest to identity.
        /// Keeps oriented boxes stable and lets near-axis-aligned results stay unrotated.
        /// </summary>
        public static void CanonicalizeAxes(ref float3x3 axes, ref float3 size)
        {
            var perms = new[] { new int3(0, 1, 2), new int3(0, 2, 1), new int3(1, 0, 2), new int3(1, 2, 0), new int3(2, 0, 1), new int3(2, 1, 0) };
            var bestScore = float.MinValue;
            var bestAxes = axes;
            var bestSize = size;

            foreach (var p in perms)
            for (var s = 0; s < 4; s++)
            {
                var c0 = axes[p.x] * ((s & 1) == 0 ? 1 : -1);
                var c1 = axes[p.y] * ((s & 2) == 0 ? 1 : -1);
                var c2 = math.cross(c0, c1);
                var score = c0.x + c1.y + c2.z;
                if (score > bestScore + 1e-6f)
                {
                    bestScore = score;
                    bestAxes = new float3x3(c0, c1, c2);
                    bestSize = new float3(size[p.x], size[p.y], size[p.z]);
                }
            }

            axes = bestAxes;
            size = bestSize;
        }

        /// <summary>True when the rotation is within <paramref name="degrees"/> of identity.</summary>
        public static bool IsNearIdentity(quaternion q, float degrees = 0.01f)
        {
            var w = math.abs(math.normalize(q).value.w);
            var angle = 2f * math.degrees(math.acos(math.clamp(w, 0f, 1f)));
            return angle <= degrees;
        }
    }
}
