using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Turns filled geometry (triangles, polygons or an alpha mask) into outline loops: rasterize onto a grid, then follow
    /// the boundary between solid and empty cells. Loops come out oriented (outer CCW, holes CW), staircase-shaped along the
    /// grid; <see cref="PolygonSimplifier"/> smooths them. Deterministic.
    /// </summary>
    internal static class OutlineTracer
    {
        /// <summary>Solid / empty cells over a rectangle (one padding cell on every side keeps loops closed).</summary>
        internal sealed class Raster
        {
            public int Width, Height;
            public float2 Origin;
            public float CellSize;
            public byte[] Cells;

            public bool Solid(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Cells[y * Width + x] != 0;

            public float2 Center(int x, int y) => Origin + (new float2(x, y) + 0.5f) * CellSize;

            /// <summary>Grid over [min, max] with <paramref name="resolution"/> cells along the longest side, plus padding.</summary>
            public static Raster Over(float2 min, float2 max, int resolution)
            {
                var size = math.max(max - min, 1e-6f);
                var cell = math.cmax(size) / math.max(resolution, 2);
                var w = (int)math.ceil(size.x / cell) + 2;
                var h = (int)math.ceil(size.y / cell) + 2;
                return new Raster
                {
                    Width = w,
                    Height = h,
                    CellSize = cell,
                    Origin = min - cell,
                    Cells = new byte[w * h],
                };
            }
        }

        /// <summary>Union of triangles (any winding), e.g. a flat mesh's projected faces.</summary>
        public static Raster RasterizeTriangles(float2[] vertices, int[] triangles, int resolution)
        {
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var v in vertices)
            {
                min = math.min(min, v);
                max = math.max(max, v);
            }
            var raster = Raster.Over(min, max, resolution);

            using var verts = new NativeArray<float2>(vertices, Allocator.TempJob);
            using var tris = new NativeArray<int>(triangles, Allocator.TempJob);
            using var cells = new NativeArray<byte>(raster.Cells.Length, Allocator.TempJob);
            new TriangleRasterJob
            {
                Vertices = verts,
                Triangles = tris,
                Cells = cells,
                Width = raster.Width,
                Height = raster.Height,
                Origin = raster.Origin,
                CellSize = raster.CellSize,
            }.Run();
            cells.CopyTo(raster.Cells);
            return raster;
        }

        /// <summary>Even-odd fill of polygon loops (scanline at cell centers), e.g. to union several outlines.</summary>
        public static Raster RasterizeLoops(IReadOnlyList<float2[]> loops, int resolution, Raster into = null)
        {
            Polygon2D.Bounds(loops, out var min, out var max);
            var raster = into ?? Raster.Over(min, max, resolution);
            var crossings = new List<float>();
            // Only the rows the loops reach (many small loops go into one big raster, e.g. tiles).
            var yFrom = math.max(0, (int)math.floor((min.y - raster.Origin.y) / raster.CellSize - 0.5f));
            var yTo = math.min(raster.Height - 1, (int)math.ceil((max.y - raster.Origin.y) / raster.CellSize - 0.5f));
            for (var y = yFrom; y <= yTo; y++)
            {
                var cy = raster.Origin.y + (y + 0.5f) * raster.CellSize;
                crossings.Clear();
                foreach (var loop in loops)
                    for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
                    {
                        var a = loop[i];
                        var b = loop[j];
                        if (a.y > cy == b.y > cy) continue;
                        crossings.Add(a.x + (cy - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                crossings.Sort();
                for (var k = 0; k + 1 < crossings.Count; k += 2)
                {
                    var x0 = (int)math.ceil((crossings[k] - raster.Origin.x) / raster.CellSize - 0.5f);
                    var x1 = (int)math.floor((crossings[k + 1] - raster.Origin.x) / raster.CellSize - 0.5f);
                    for (var x = math.max(x0, 0); x <= math.min(x1, raster.Width - 1); x++)
                        raster.Cells[y * raster.Width + x] = 1;
                }
            }
            return raster;
        }

        /// <summary>Raster from a solid mask (row-major, <paramref name="width"/> × <paramref name="height"/>) placed at origin with cell size.</summary>
        public static Raster FromMask(bool[] mask, int width, int height, float2 origin, float cellSize)
        {
            var raster = new Raster
            {
                Width = width + 2,
                Height = height + 2,
                CellSize = cellSize,
                Origin = origin - cellSize,
            };
            raster.Cells = new byte[raster.Width * raster.Height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (mask[y * width + x])
                    raster.Cells[(y + 1) * raster.Width + x + 1] = 1;
            return raster;
        }

        /// <summary>
        /// Boundary loops of the solid cells, solid on the left of every edge: outer loops CCW, holes CW. Diagonally touching
        /// cells stay separate pieces. Straight runs are collapsed to their corners.
        /// </summary>
        public static List<float2[]> Trace(Raster raster)
        {
            var w = raster.Width;
            var stride = w + 1;
            // Directed boundary edges keyed by start vertex (at most two at a saddle vertex).
            var outgoing = new Dictionary<int, List<int>>();
            var starts = new List<int>();

            void AddEdge(int x0, int y0, int x1, int y1)
            {
                var from = y0 * stride + x0;
                var to = y1 * stride + x1;
                if (!outgoing.TryGetValue(from, out var list))
                {
                    list = new List<int>(1);
                    outgoing[from] = list;
                    starts.Add(from);
                }
                list.Add(to);
            }

            for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < w; x++)
            {
                if (!raster.Solid(x, y)) continue;
                if (!raster.Solid(x, y - 1)) AddEdge(x, y, x + 1, y);
                if (!raster.Solid(x + 1, y)) AddEdge(x + 1, y, x + 1, y + 1);
                if (!raster.Solid(x, y + 1)) AddEdge(x + 1, y + 1, x, y + 1);
                if (!raster.Solid(x - 1, y)) AddEdge(x, y + 1, x, y);
            }

            var loops = new List<float2[]>();
            foreach (var start in starts)
            {
                if (!outgoing.TryGetValue(start, out var first) || first.Count == 0) continue;

                var vertices = new List<int> { start };
                var current = start;
                var next = Take(outgoing, current, -1, stride);
                while (next != start && next >= 0)
                {
                    vertices.Add(next);
                    var previous = current;
                    current = next;
                    next = Take(outgoing, current, previous, stride);
                }
                if (vertices.Count < 4) continue;

                var loop = new List<float2>(vertices.Count);
                for (var i = 0; i < vertices.Count; i++)
                {
                    var a = vertices[(i + vertices.Count - 1) % vertices.Count];
                    var b = vertices[i];
                    var c = vertices[(i + 1) % vertices.Count];
                    if (b - a == c - b) continue; // straight run: keep only corners
                    loop.Add(raster.Origin + new float2(b % stride, b / stride) * raster.CellSize);
                }
                if (loop.Count >= 3) loops.Add(loop.ToArray());
            }
            return loops;
        }

        /// <summary>Remove and return the next edge from <paramref name="vertex"/>; at a saddle prefer the left turn — the solid is on the left, so this stays on the same piece and keeps diagonally touching pieces apart.</summary>
        private static int Take(Dictionary<int, List<int>> outgoing, int vertex, int previous, int stride)
        {
            if (!outgoing.TryGetValue(vertex, out var list) || list.Count == 0) return -1;
            var pick = 0;
            if (list.Count > 1 && previous >= 0)
            {
                var din = Direction(previous, vertex, stride);
                var best = int.MaxValue;
                for (var i = 0; i < list.Count; i++)
                {
                    var dout = Direction(vertex, list[i], stride);
                    // cross > 0 → left turn (rank 0), straight (rank 1), right (rank 2).
                    var cross = din.x * dout.y - din.y * dout.x;
                    var rank = cross > 0 ? 0 : cross == 0 ? 1 : 2;
                    if (rank >= best) continue;
                    best = rank;
                    pick = i;
                }
            }
            var to = list[pick];
            list.RemoveAt(pick);
            return to;
        }

        private static int2 Direction(int from, int to, int stride) =>
            new int2(to % stride - from % stride, to / stride - from / stride);

        [BurstCompile(CompileSynchronously = true)]
        private struct TriangleRasterJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Vertices;
            [ReadOnly] public NativeArray<int> Triangles;
            public NativeArray<byte> Cells;
            public int Width, Height;
            public float2 Origin;
            public float CellSize;

            public void Execute()
            {
                for (var t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    var a = Vertices[Triangles[t]];
                    var b = Vertices[Triangles[t + 1]];
                    var c = Vertices[Triangles[t + 2]];
                    var area = Edge(a, b, c);
                    if (math.abs(area) < 1e-12f) continue;

                    var lo = (int2)math.floor((math.min(a, math.min(b, c)) - Origin) / CellSize - 0.5f);
                    var hi = (int2)math.ceil((math.max(a, math.max(b, c)) - Origin) / CellSize - 0.5f);
                    lo = math.max(lo, 0);
                    hi = math.min(hi, new int2(Width - 1, Height - 1));
                    for (var y = lo.y; y <= hi.y; y++)
                    for (var x = lo.x; x <= hi.x; x++)
                    {
                        var p = Origin + (new float2(x, y) + 0.5f) * CellSize;
                        var w0 = Edge(b, c, p);
                        var w1 = Edge(c, a, p);
                        var w2 = Edge(a, b, p);
                        var inside = area > 0 ? w0 >= 0 && w1 >= 0 && w2 >= 0 : w0 <= 0 && w1 <= 0 && w2 <= 0;
                        if (inside) Cells[y * Width + x] = 1;
                    }
                }
            }

            private static float Edge(float2 a, float2 b, float2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }
    }
}
