using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Approximate convex decomposition (our own, no VHACD):
    /// voxelize → split into block regions (connected pieces per block) → greedily merge the adjacent pair whose merged hull
    /// adds the least empty space, until merges would add more than the concavity tolerance and the part count fits
    /// → cap each hull to the PhysX limit → swap parts for primitives where they fit.
    /// Deterministic: same mesh and options → same parts.
    /// </summary>
    internal static class ConvexDecomposer
    {
        /// <summary>Initial blocks per axis before merging.</summary>
        private const int InitialSplit = 6;

        /// <summary>
        /// Hull points kept per region while merging (merged hulls only depend on hull vertices; thinning keeps the outline).
        /// Keeps every merge evaluation small — final parts are capped to 255 triangles anyway.
        /// </summary>
        private const int MaxRegionPoints = 128;

        public struct Options
        {
            public int Resolution;
            public int MaxParts;
            public float ConcavityTolerance;
            public float PrimitiveFitThreshold;
        }

        public sealed class Result
        {
            public List<FittedShape> Parts = new();
            public int InitialRegions;
            public int Components;
            public float VoxelSize;
        }

        private sealed class Region
        {
            public int Id;
            public bool Alive = true;
            public int Version;
            public float3[] Points;      // hull vertices (or raw points when no hull exists)
            public float HullVolume;
            public float Solid;          // estimated solid volume
            public readonly HashSet<int> Neighbors = new();

            public float Waste => math.max(0f, HullVolume - Solid);
        }

        private struct Candidate
        {
            public float Cost;
            public int A, B, VersionA, VersionB;
        }

        public static Result Decompose(MeshGeometry mesh, Options options, FitContext context)
        {
            var result = new Result();
            if (mesh.TriangleCount == 0) return result;

            var grid = Voxelizer.Voxelize(mesh.Vertices, mesh.Triangles, options.Resolution);
            result.VoxelSize = grid.VoxelSize;
            var regions = BuildRegions(grid, out var regionOfVoxel);
            result.InitialRegions = regions.Count;
            ConnectNeighbors(grid, regionOfVoxel, regions);
            result.Components = CountComponents(regions);

            var totalSolid = 0f;
            foreach (var r in regions) totalSolid += r.Solid;
            if (totalSolid <= 0f) totalSolid = 1f;

            Merge(regions, options, totalSolid);

            foreach (var region in regions)
            {
                if (!region.Alive) continue;
                var part = MakePart(region, grid, options, context);
                if (part == null) continue;
                part.Label = $"Part {result.Parts.Count + 1}";
                result.Parts.Add(part);
            }
            return result;
        }

        private static List<Region> BuildRegions(VoxelGrid grid, out int[] regionOfVoxel)
        {
            var dims = grid.Dimensions;
            var block = math.max(2, (int)math.ceil(math.cmax(dims) / (float)InitialSplit));
            regionOfVoxel = new int[grid.Count];
            for (var i = 0; i < regionOfVoxel.Length; i++) regionOfVoxel[i] = -1;

            var regions = new List<Region>();
            var queue = new List<int>();
            for (var start = 0; start < grid.Count; start++)
            {
                if (grid.Cells[start] == VoxelGrid.Empty || regionOfVoxel[start] >= 0) continue;

                // Flood the connected occupied voxels of this block.
                var id = regions.Count;
                var blockOf = grid.Coordinate(start) / block;
                queue.Clear();
                queue.Add(start);
                regionOfVoxel[start] = id;
                for (var head = 0; head < queue.Count; head++)
                {
                    var v = grid.Coordinate(queue[head]);
                    foreach (var step in Steps)
                    {
                        var n = v + step;
                        if (!grid.InRange(n) || math.any(n / block != blockOf)) continue;
                        var index = grid.Index(n);
                        if (grid.Cells[index] == VoxelGrid.Empty || regionOfVoxel[index] >= 0) continue;
                        regionOfVoxel[index] = id;
                        queue.Add(index);
                    }
                }
                regions.Add(CreateRegion(id, queue, grid));
            }

            // Surface samples go to the region of their voxel.
            var samplePoints = new List<float3>[regions.Count];
            for (var s = 0; s < grid.Samples.Length; s++)
            {
                var region = regionOfVoxel[grid.SampleVoxel[s]];
                if (region < 0) continue;
                (samplePoints[region] ??= new List<float3>()).Add(grid.Samples[s]);
            }
            for (var r = 0; r < regions.Count; r++)
                if (samplePoints[r] != null) AddPoints(regions[r], samplePoints[r]);
            foreach (var region in regions) FinishHull(region);
            return regions;
        }

        private static readonly int3[] Steps =
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        };

        private static Region CreateRegion(int id, List<int> voxels, VoxelGrid grid)
        {
            var region = new Region { Id = id };
            var corners = new HashSet<int3>();
            var points = new List<float3>();
            foreach (var index in voxels)
            {
                if (grid.Cells[index] == VoxelGrid.Interior)
                {
                    region.Solid += grid.VoxelVolume;
                    // Interior voxels never touch the surface, so their corners are inside the mesh.
                    var v = grid.Coordinate(index);
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = v + new int3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                        if (corners.Add(corner)) points.Add(grid.Corner(corner));
                    }
                }
                else
                {
                    region.Solid += grid.VoxelVolume * 0.5f;
                }
            }
            region.Points = points.ToArray();
            return region;
        }

        private static void AddPoints(Region region, List<float3> extra)
        {
            var all = new List<float3>(region.Points);
            all.AddRange(extra);
            region.Points = all.ToArray();
        }

        private static void FinishHull(Region region)
        {
            var reduced = QuickHull3D.ReducePoints(region.Points, MaxRegionPoints);
            if (QuickHull3D.TryBuild(reduced, out var hull))
            {
                region.Points = hull.Vertices;
                region.HullVolume = hull.Volume;
            }
            else
            {
                region.Points = ToArray(reduced);
                region.HullVolume = 0f;
            }
        }

        private static void ConnectNeighbors(VoxelGrid grid, int[] regionOfVoxel, List<Region> regions)
        {
            for (var index = 0; index < grid.Count; index++)
            {
                var a = regionOfVoxel[index];
                if (a < 0) continue;
                var v = grid.Coordinate(index);
                for (var axis = 0; axis < 3; axis++)
                {
                    var n = v;
                    n[axis] += 1;
                    if (!grid.InRange(n)) continue;
                    var b = regionOfVoxel[grid.Index(n)];
                    if (b < 0 || b == a) continue;
                    regions[a].Neighbors.Add(b);
                    regions[b].Neighbors.Add(a);
                }
            }
        }

        private static void Merge(List<Region> regions, Options options, float totalSolid)
        {
            var heap = new List<Candidate>();
            var alive = regions.Count;

            foreach (var region in regions)
                foreach (var other in region.Neighbors)
                    if (other > region.Id) Push(heap, Evaluate(region, regions[other], totalSolid));

            while (heap.Count > 0)
            {
                var best = Pop(heap);
                var a = regions[best.A];
                var b = regions[best.B];
                if (!a.Alive || !b.Alive || a.Version != best.VersionA || b.Version != best.VersionB) continue;

                // Merge while it is cheap enough, and beyond that only to get under the part limit.
                if (best.Cost > options.ConcavityTolerance && alive <= options.MaxParts) break;

                var points = new List<float3>(a.Points);
                points.AddRange(b.Points);
                a.Points = points.ToArray();
                a.Solid += b.Solid;
                FinishHull(a);
                a.Version++;
                b.Alive = false;
                alive--;

                foreach (var n in b.Neighbors)
                {
                    if (n == a.Id) continue;
                    regions[n].Neighbors.Remove(b.Id);
                    regions[n].Neighbors.Add(a.Id);
                    a.Neighbors.Add(n);
                }
                a.Neighbors.Remove(b.Id);

                foreach (var n in a.Neighbors)
                    if (regions[n].Alive) Push(heap, Evaluate(a, regions[n], totalSolid));
            }
        }

        /// <summary>Merge cost = extra empty space the merged hull adds, as a fraction of the whole object's volume.</summary>
        private static Candidate Evaluate(Region a, Region b, float totalSolid)
        {
            var points = new float3[a.Points.Length + b.Points.Length];
            a.Points.CopyTo(points, 0);
            b.Points.CopyTo(points, a.Points.Length);
            var hullVolume = QuickHull3D.TryBuild(points, out var hull) ? hull.Volume : 0f;
            var mergedWaste = math.max(0f, hullVolume - (a.Solid + b.Solid));
            var cost = math.max(0f, mergedWaste - a.Waste - b.Waste) / totalSolid;

            var (lo, hi) = a.Id < b.Id ? (a, b) : (b, a);
            return new Candidate { Cost = cost, A = lo.Id, B = hi.Id, VersionA = lo.Version, VersionB = hi.Version };
        }

        private static FittedShape MakePart(Region region, VoxelGrid grid, Options options, FitContext context)
        {
            if (!QuickHull3D.TryBuildCapped(region.Points, PhysicsLimits.MaxConvexPolygons, out var hull))
            {
                // Flat or tiny piece: a thin box, unless it is negligible.
                if (region.Solid < grid.VoxelVolume * 2f || region.Points.Length == 0) return null;
                return FittedShape.FromBox(BoxFitter.Pca(region.Points));
            }

            var primitive = PrimitiveReplacer.TryReplace(hull, context, options.PrimitiveFitThreshold);
            return primitive ?? FittedShape.FromHull(hull);
        }

        private static int CountComponents(List<Region> regions)
        {
            var seen = new bool[regions.Count];
            var components = 0;
            var stack = new Stack<int>();
            for (var i = 0; i < regions.Count; i++)
            {
                if (seen[i]) continue;
                components++;
                seen[i] = true;
                stack.Push(i);
                while (stack.Count > 0)
                    foreach (var n in regions[stack.Pop()].Neighbors)
                        if (!seen[n])
                        {
                            seen[n] = true;
                            stack.Push(n);
                        }
            }
            return components;
        }

        private static float3[] ToArray(IReadOnlyList<float3> points)
        {
            if (points is float3[] array) return array;
            var result = new float3[points.Count];
            for (var i = 0; i < result.Length; i++) result[i] = points[i];
            return result;
        }

        // Binary min-heap on (cost, a, b) — the id tie-break keeps the merge order deterministic.
        private static void Push(List<Candidate> heap, Candidate item)
        {
            heap.Add(item);
            var i = heap.Count - 1;
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                if (!Less(heap[i], heap[parent])) break;
                (heap[i], heap[parent]) = (heap[parent], heap[i]);
                i = parent;
            }
        }

        private static Candidate Pop(List<Candidate> heap)
        {
            var top = heap[0];
            var last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            var i = 0;
            while (true)
            {
                var l = 2 * i + 1;
                var r = l + 1;
                var smallest = i;
                if (l < heap.Count && Less(heap[l], heap[smallest])) smallest = l;
                if (r < heap.Count && Less(heap[r], heap[smallest])) smallest = r;
                if (smallest == i) break;
                (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
                i = smallest;
            }
            return top;
        }

        private static bool Less(Candidate x, Candidate y)
        {
            if (x.Cost != y.Cost) return x.Cost < y.Cost;
            if (x.A != y.A) return x.A < y.A;
            return x.B < y.B;
        }
    }
}
