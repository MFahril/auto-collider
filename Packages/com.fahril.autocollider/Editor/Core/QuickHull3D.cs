using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// 3D QuickHull. Computes in double precision, deterministic for a given input order.
    /// <see cref="TryBuildCapped"/> enforces a triangle budget (the PhysX "256 guard").
    /// </summary>
    internal static class QuickHull3D
    {
        private sealed class Face
        {
            public int A, B, C;
            public double3 Normal;
            public double Offset;
            public List<int> Outside;
            public bool Alive = true;

            public double Distance(double3 p) => math.dot(Normal, p) - Offset;
        }

        /// <summary>Full hull of the points. Fails for fewer than 4 non-coplanar points.</summary>
        public static bool TryBuild(IReadOnlyList<float3> points, out ConvexHull hull)
        {
            hull = null;
            if (points == null || points.Count < PhysicsLimits.MinHullVertices) return false;

            var p = new double3[points.Count];
            double3 min = new double3(double.MaxValue), max = new double3(double.MinValue);
            for (var i = 0; i < p.Length; i++)
            {
                p[i] = points[i];
                min = math.min(min, p[i]);
                max = math.max(max, p[i]);
            }
            var diagonal = math.length(max - min);
            if (diagonal <= 0) return false;
            // Input is float data: treat anything within ~float precision of a plane as on it, to avoid sliver faces.
            var eps = diagonal * 1e-6;

            if (!TryInitialSimplex(p, eps, out var simplex)) return false;

            var faces = new List<Face>();
            var edges = new Dictionary<long, int>();
            var centroid = (p[simplex.x] + p[simplex.y] + p[simplex.z] + p[simplex.w]) / 4;

            void AddFace(int a, int b, int c, List<Face> list)
            {
                var n = math.cross(p[b] - p[a], p[c] - p[a]);
                var len = math.length(n);
                n = len > 1e-300 ? n / len : double3.zero;
                var face = new Face { A = a, B = b, C = c, Normal = n, Offset = math.dot(n, p[a]) };
                var index = faces.Count;
                faces.Add(face);
                edges[Key(a, b)] = index;
                edges[Key(b, c)] = index;
                edges[Key(c, a)] = index;
                list?.Add(face);
            }

            // Initial tetrahedron, oriented outward.
            int s0 = simplex.x, s1 = simplex.y, s2 = simplex.z, s3 = simplex.w;
            var tetra = new[] { new int3(s0, s1, s2), new int3(s0, s3, s1), new int3(s1, s3, s2), new int3(s2, s3, s0) };
            foreach (var t in tetra)
            {
                var n = math.cross(p[t.y] - p[t.x], p[t.z] - p[t.x]);
                if (math.dot(n, centroid - p[t.x]) > 0) AddFace(t.x, t.z, t.y, null);
                else AddFace(t.x, t.y, t.z, null);
            }

            // Assign every other point to the face it is furthest outside of.
            for (var i = 0; i < p.Length; i++)
            {
                if (i == s0 || i == s1 || i == s2 || i == s3) continue;
                AssignToBest(i, faces, 0, faces.Count, p, eps);
            }

            var pending = new Queue<int>();
            for (var i = 0; i < faces.Count; i++)
                if (faces[i].Outside != null) pending.Enqueue(i);

            var visible = new List<int>();
            var horizon = new List<int2>();
            var orphans = new List<int>();
            var stack = new Stack<int>();
            var visited = new HashSet<int>();
            var newFaces = new List<Face>();

            while (pending.Count > 0)
            {
                var fi = pending.Dequeue();
                var face = faces[fi];
                if (!face.Alive || face.Outside == null || face.Outside.Count == 0) continue;

                // Eye point: furthest outside point (lowest index on ties for determinism).
                var eye = -1;
                var eyeDist = double.MinValue;
                foreach (var idx in face.Outside)
                {
                    var d = face.Distance(p[idx]);
                    if (d > eyeDist) { eyeDist = d; eye = idx; }
                }

                // Flood visible faces and collect horizon edges.
                visible.Clear();
                horizon.Clear();
                visited.Clear();
                stack.Clear();
                stack.Push(fi);
                visited.Add(fi);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    visible.Add(current);
                    var f = faces[current];
                    for (var k = 0; k < 3; k++)
                    {
                        var e = Edge(f, k);
                        if (!edges.TryGetValue(Key(e.y, e.x), out var neighbor)) continue;
                        if (visited.Contains(neighbor)) continue;
                        if (faces[neighbor].Distance(p[eye]) > eps)
                        {
                            visited.Add(neighbor);
                            stack.Push(neighbor);
                        }
                    }
                }

                // Every visited face is visible, so `visited` doubles as the visible-set lookup.
                foreach (var vi in visible)
                {
                    var f = faces[vi];
                    for (var k = 0; k < 3; k++)
                    {
                        var e = Edge(f, k);
                        if (edges.TryGetValue(Key(e.y, e.x), out var neighbor) && !visited.Contains(neighbor))
                            horizon.Add(e);
                    }
                }

                // Remove visible faces, keep their outside points for reassignment.
                orphans.Clear();
                foreach (var vi in visible)
                {
                    var f = faces[vi];
                    f.Alive = false;
                    RemoveEdge(edges, f.A, f.B, vi);
                    RemoveEdge(edges, f.B, f.C, vi);
                    RemoveEdge(edges, f.C, f.A, vi);
                    if (f.Outside != null)
                        foreach (var idx in f.Outside)
                            if (idx != eye) orphans.Add(idx);
                    f.Outside = null;
                }

                // Cone from the horizon to the eye.
                newFaces.Clear();
                var firstNew = faces.Count;
                foreach (var e in horizon)
                    AddFace(e.x, e.y, eye, newFaces);

                foreach (var idx in orphans)
                    AssignToBest(idx, faces, firstNew, faces.Count, p, eps);

                for (var i = firstNew; i < faces.Count; i++)
                    if (faces[i].Outside != null) pending.Enqueue(i);
            }

            // Collect alive faces and compact vertices.
            var remap = new Dictionary<int, int>();
            var vertices = new List<float3>();
            var triangles = new List<int>();
            foreach (var f in faces)
            {
                if (!f.Alive) continue;
                triangles.Add(Remap(f.A));
                triangles.Add(Remap(f.B));
                triangles.Add(Remap(f.C));
            }

            int Remap(int index)
            {
                if (remap.TryGetValue(index, out var mapped)) return mapped;
                mapped = vertices.Count;
                remap[index] = mapped;
                vertices.Add(points[index]);
                return mapped;
            }

            if (triangles.Count < 12) return false;
            hull = new ConvexHull(vertices.ToArray(), triangles.ToArray());
            return true;
        }

        /// <summary>
        /// Hull with at most <paramref name="maxTriangles"/> triangles. When the full hull is too detailed, hull vertices are
        /// clustered on a growing grid (keeping the vertex furthest from the centroid per cell) and the hull is rebuilt.
        /// The result is slightly inside the full hull.
        /// </summary>
        public static bool TryBuildCapped(IReadOnlyList<float3> points, int maxTriangles, out ConvexHull hull)
        {
            // The capped result is approximate anyway, so very dense input is thinned first (hull cost grows with hull points).
            if (!TryBuild(ReducePoints(points, MaxCappedInput), out hull)) return false;
            if (hull.TriangleCount <= maxTriangles) return true;

            var source = hull.Vertices;
            GeometryUtils.MinMax(source, out var min, out var max);
            var diagonal = math.length(max - min);
            var centroid = Centroid(source);

            var cell = diagonal / 24f;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                var reduced = ClusterFurthest(source, min, cell, centroid);
                if (reduced.Length >= PhysicsLimits.MinHullVertices && TryBuild(reduced, out var candidate))
                {
                    hull = candidate;
                    if (hull.TriangleCount <= maxTriangles) return true;
                }
                cell *= 1.2f;
            }
            return hull.TriangleCount <= maxTriangles;
        }

        /// <summary>Input size above which <see cref="TryBuildCapped"/> thins points before building.</summary>
        public const int MaxCappedInput = 4096;

        /// <summary>
        /// At most <paramref name="maxPoints"/> points that keep the outline: grid clustering, keeping per cell the point
        /// furthest from the centroid. Returns the input unchanged when it is already small enough.
        /// </summary>
        public static IReadOnlyList<float3> ReducePoints(IReadOnlyList<float3> points, int maxPoints)
        {
            if (points.Count <= maxPoints) return points;

            var source = new float3[points.Count];
            for (var i = 0; i < source.Length; i++) source[i] = points[i];
            GeometryUtils.MinMax(source, out var min, out var max);
            var diagonal = math.length(max - min);
            if (diagonal <= 0f) return points;
            var centroid = Centroid(source);

            var cell = diagonal / 64f;
            float3[] reduced;
            do
            {
                reduced = ClusterFurthest(source, min, cell, centroid);
                cell *= 1.25f;
            } while (reduced.Length > maxPoints);
            return reduced;
        }

        private static float3 Centroid(float3[] points)
        {
            var sum = float3.zero;
            foreach (var v in points) sum += v;
            return sum / math.max(1, points.Length);
        }

        private static float3[] ClusterFurthest(float3[] source, float3 origin, float cell, float3 centroid)
        {
            var best = new Dictionary<int3, int>();
            var order = new List<int3>();
            for (var i = 0; i < source.Length; i++)
            {
                var key = (int3)math.floor((source[i] - origin) / cell);
                if (!best.TryGetValue(key, out var current))
                {
                    best[key] = i;
                    order.Add(key);
                }
                else if (math.lengthsq(source[i] - centroid) > math.lengthsq(source[current] - centroid))
                {
                    best[key] = i;
                }
            }
            var result = new float3[order.Count];
            for (var i = 0; i < order.Count; i++)
                result[i] = source[best[order[i]]];
            return result;
        }

        private static void AssignToBest(int index, List<Face> faces, int from, int to, double3[] p, double eps)
        {
            Face best = null;
            var bestDist = eps;
            for (var i = from; i < to; i++)
            {
                var f = faces[i];
                if (!f.Alive) continue;
                var d = f.Distance(p[index]);
                if (d > bestDist)
                {
                    bestDist = d;
                    best = f;
                }
            }
            if (best == null) return;
            best.Outside ??= new List<int>();
            best.Outside.Add(index);
        }

        private static bool TryInitialSimplex(double3[] p, double eps, out int4 simplex)
        {
            simplex = default;

            // Extreme points per axis.
            var extremes = new int[6];
            for (var i = 0; i < p.Length; i++)
            for (var axis = 0; axis < 3; axis++)
            {
                if (p[i][axis] < p[extremes[axis * 2]][axis]) extremes[axis * 2] = i;
                if (p[i][axis] > p[extremes[axis * 2 + 1]][axis]) extremes[axis * 2 + 1] = i;
            }

            int i0 = 0, i1 = 0;
            var bestSq = -1.0;
            for (var a = 0; a < 6; a++)
            for (var b = a + 1; b < 6; b++)
            {
                var d = math.lengthsq(p[extremes[a]] - p[extremes[b]]);
                if (d > bestSq) { bestSq = d; i0 = extremes[a]; i1 = extremes[b]; }
            }
            if (bestSq <= eps * eps) return false;

            // Furthest from the line i0-i1.
            var dir = math.normalize(p[i1] - p[i0]);
            var i2 = -1;
            var best = eps;
            for (var i = 0; i < p.Length; i++)
            {
                var d = math.length(math.cross(p[i] - p[i0], dir));
                if (d > best) { best = d; i2 = i; }
            }
            if (i2 < 0) return false;

            // Furthest from the plane i0-i1-i2.
            var n = math.normalize(math.cross(p[i1] - p[i0], p[i2] - p[i0]));
            var i3 = -1;
            best = eps;
            for (var i = 0; i < p.Length; i++)
            {
                var d = math.abs(math.dot(p[i] - p[i0], n));
                if (d > best) { best = d; i3 = i; }
            }
            if (i3 < 0) return false;

            simplex = new int4(i0, i1, i2, i3);
            return true;
        }

        private static long Key(int from, int to) => ((long)from << 32) | (uint)to;

        private static int2 Edge(Face f, int k) => k switch
        {
            0 => new int2(f.A, f.B),
            1 => new int2(f.B, f.C),
            _ => new int2(f.C, f.A),
        };

        private static void RemoveEdge(Dictionary<long, int> edges, int from, int to, int face)
        {
            var key = Key(from, to);
            if (edges.TryGetValue(key, out var owner) && owner == face) edges.Remove(key);
        }
    }
}
