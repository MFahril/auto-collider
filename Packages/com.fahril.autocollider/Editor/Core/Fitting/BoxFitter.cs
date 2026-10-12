using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal struct BoxFit
    {
        public float3 Center;
        public quaternion Rotation;
        public float3 Size;
        public float Volume;
    }

    /// <summary>Enclosing box fitters: axis-aligned, PCA-oriented and minimum-volume oriented.</summary>
    internal static class BoxFitter
    {
        /// <summary>Axis-aligned bounding box.</summary>
        public static BoxFit Aabb(IReadOnlyList<float3> points)
        {
            GeometryUtils.MinMax(points, out var min, out var max);
            var size = math.max(max - min, PhysicsLimits.MinExtent);
            return new BoxFit
            {
                Center = (min + max) * 0.5f,
                Rotation = quaternion.identity,
                Size = size,
                Volume = size.x * size.y * size.z,
            };
        }

        /// <summary>Box aligned to the principal axes of the points. Works for flat (coplanar) input too.</summary>
        public static BoxFit Pca(IReadOnlyList<float3> points)
        {
            var covariance = GeometryUtils.Covariance(points, out _);
            GeometryUtils.EigenSymmetric(covariance, out _, out var vectors);
            var axes = new float3x3((float3)vectors.c0, (float3)vectors.c1, (float3)vectors.c2);
            return InFrame(points, axes);
        }

        /// <summary>
        /// Approximate minimum-volume box: for each hull face normal, the minimum-area rectangle of the projection
        /// (one box face flush with a hull face), compared against PCA and AABB candidates.
        /// </summary>
        public static BoxFit MinVolume(IReadOnlyList<float3> hullPoints, ConvexHull hull)
        {
            var best = Aabb(hullPoints);
            var pca = Pca(hullPoints);
            if (pca.Volume < best.Volume) best = pca;
            if (hull == null) return best;

            var projected = new List<double2>(hullPoints.Count);
            foreach (var n in CandidateDirections(hull))
            {
                GeometryUtils.Basis(n, out var u, out var v);
                projected.Clear();
                for (var i = 0; i < hullPoints.Count; i++)
                    projected.Add(new double2(math.dot(hullPoints[i], u), math.dot(hullPoints[i], v)));

                var hull2D = GeometryUtils.ConvexHull2D(projected);
                var dir = GeometryUtils.MinAreaRectDirection(hull2D);
                var axis0 = math.normalize(u * (float)dir.x + v * (float)dir.y);
                var axis1 = math.cross(n, axis0);
                var candidate = InFrame(hullPoints, new float3x3(axis0, axis1, n));
                if (candidate.Volume < best.Volume) best = candidate;
            }
            return best;
        }

        /// <summary>Face directions tried by <see cref="MinVolume"/>; caps the cost on round hulls (hundreds of tiny faces).</summary>
        private const int MaxDirections = 64;

        /// <summary>
        /// Distinct hull face directions (n and −n merged, near-duplicates quantized together), ranked by the hull area facing
        /// them — coplanar triangles add up, so real flat faces always come first. Ties keep face order (deterministic).
        /// </summary>
        private static List<float3> CandidateDirections(ConvexHull hull)
        {
            var area = new Dictionary<int3, float>();
            var direction = new Dictionary<int3, float3>();
            var order = new List<int3>();
            var vertices = hull.Vertices;
            var triangles = hull.Triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var cross = math.cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a);
                var doubleArea = math.length(cross);
                if (doubleArea < 1e-12f) continue;
                var n = cross / doubleArea;
                if (n.x < 0 || (n.x == 0 && (n.y < 0 || (n.y == 0 && n.z < 0)))) n = -n;

                var key = (int3)math.round(n * 2000f);
                if (area.TryGetValue(key, out var sum))
                {
                    area[key] = sum + doubleArea;
                    continue;
                }
                area[key] = doubleArea;
                direction[key] = n;
                order.Add(key);
            }

            var ranked = new List<int>(order.Count);
            for (var i = 0; i < order.Count; i++) ranked.Add(i);
            ranked.Sort((x, y) =>
            {
                var byArea = area[order[y]].CompareTo(area[order[x]]);
                return byArea != 0 ? byArea : x.CompareTo(y);
            });

            var result = new List<float3>(math.min(MaxDirections, ranked.Count));
            for (var i = 0; i < ranked.Count && i < MaxDirections; i++) result.Add(direction[order[ranked[i]]]);
            return result;
        }

        /// <summary>Tightest box with the given orthonormal axes (columns).</summary>
        public static BoxFit InFrame(IReadOnlyList<float3> points, float3x3 axes)
        {
            if (math.determinant(axes) < 0) axes.c2 = -axes.c2;

            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            for (var i = 0; i < points.Count; i++)
            {
                var local = new float3(math.dot(points[i], axes.c0), math.dot(points[i], axes.c1), math.dot(points[i], axes.c2));
                min = math.min(min, local);
                max = math.max(max, local);
            }

            var size = math.max(max - min, PhysicsLimits.MinExtent);
            var mid = (min + max) * 0.5f;
            var center = axes.c0 * mid.x + axes.c1 * mid.y + axes.c2 * mid.z;
            GeometryUtils.CanonicalizeAxes(ref axes, ref size);

            return new BoxFit
            {
                Center = center,
                Rotation = math.normalize(new quaternion(axes)),
                Size = size,
                Volume = size.x * size.y * size.z,
            };
        }
    }
}
