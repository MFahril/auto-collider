using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M8: outline tracing (raster â†’ loops), simplification and loop orientation.</summary>
    public class Outline2DTests
    {
        private static bool[] Mask(int w, int h, System.Func<int, int, bool> solid)
        {
            var mask = new bool[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                mask[y * w + x] = solid(x, y);
            return mask;
        }

        [Test]
        public void Trace_Square_OneCcwLoopWithFourCorners()
        {
            var raster = OutlineTracer.FromMask(Mask(4, 4, (x, y) => true), 4, 4, float2.zero, 1f);

            var loops = OutlineTracer.Trace(raster);

            Assert.That(loops.Count, Is.EqualTo(1));
            Assert.That(loops[0].Length, Is.EqualTo(4));
            Assert.That(Polygon2D.SignedArea(loops[0]), Is.EqualTo(16f).Within(1e-4f));
        }

        [Test]
        public void Trace_Ring_OuterCcwAndHoleCw()
        {
            var raster = OutlineTracer.FromMask(Mask(10, 10, (x, y) => !(x >= 3 && x < 7 && y >= 3 && y < 7)), 10, 10, float2.zero, 1f);

            var loops = OutlineTracer.Trace(raster);

            Assert.That(loops.Count, Is.EqualTo(2));
            var areas = loops.Select(Polygon2D.SignedArea).OrderBy(a => a).ToArray();
            Assert.That(areas[0], Is.EqualTo(-16f).Within(1e-4f), "Hole runs clockwise");
            Assert.That(areas[1], Is.EqualTo(100f).Within(1e-4f));
            Assert.That(Polygon2D.Area(loops), Is.EqualTo(84f).Within(1e-4f));
        }

        [Test]
        public void Trace_DiagonalCells_StaySeparate()
        {
            var raster = OutlineTracer.FromMask(Mask(2, 2, (x, y) => x == y), 2, 2, float2.zero, 1f);

            var loops = OutlineTracer.Trace(raster);

            Assert.That(loops.Count, Is.EqualTo(2));
            Assert.That(loops.All(l => Polygon2D.SignedArea(l) > 0f));
        }

        [Test]
        public void RasterizeTriangles_TracesTheirUnion()
        {
            // Two overlapping squares (opposite windings) â†’ an L-ish union of area 1 + 1 âˆ’ 0.25 = 1.75.
            var vertices = new[]
            {
                new float2(0, 0), new float2(1, 0), new float2(1, 1), new float2(0, 1),
                new float2(0.5f, 0.5f), new float2(1.5f, 0.5f), new float2(1.5f, 1.5f), new float2(0.5f, 1.5f),
            };
            var triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };

            var loops = OutlineTracer.Trace(OutlineTracer.RasterizeTriangles(vertices, triangles, 128));

            Assert.That(loops.Count, Is.EqualTo(1));
            Assert.That(Polygon2D.Area(loops), Is.EqualTo(1.75f).Within(0.05f));
        }

        [Test]
        public void Simplify_StaircaseCircle_FewPoints_SameArea_Valid_Deterministic()
        {
            var raster = OutlineTracer.FromMask(Mask(64, 64, (x, y) => math.distance(new float2(x + 0.5f, y + 0.5f), 32f) <= 30f),
                64, 64, float2.zero, 1f);
            var staircase = OutlineTracer.Trace(raster);

            var a = PolygonSimplifier.Simplify(staircase, 1.2f);
            var b = PolygonSimplifier.Simplify(staircase, 1.2f);

            Assert.That(a.Count, Is.EqualTo(1));
            Assert.That(a[0].Length, Is.LessThan(staircase[0].Length / 2));
            Assert.That(Polygon2D.Area(a), Is.EqualTo(math.PI * 30f * 30f).Within(math.PI * 900f * 0.03f));
            Assert.That(Polygon2D.IsSimple(a[0]));
            Assert.That(b[0], Is.EqualTo(a[0]));
        }

        [Test]
        public void Simplify_KeepsCorners_DropsCollinearPoints_AndIslands()
        {
            var square = new[]
            {
                new float2(0, 0), new float2(1, 0), new float2(2, 0), new float2(2, 1), new float2(2, 2),
                new float2(1, 2), new float2(0, 2), new float2(0, 1),
            };
            var speck = new[] { new float2(5, 5), new float2(5.01f, 5), new float2(5.01f, 5.01f), new float2(5, 5.01f) };

            var result = PolygonSimplifier.Simplify(new[] { square, speck }, 0.01f, minIslandSize: 0.1f);

            Assert.That(result.Count, Is.EqualTo(1), "The speck is dropped as an island");
            Assert.That(result[0].Length, Is.EqualTo(4));
            Assert.That(Polygon2D.SignedArea(result[0]), Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void Simplify_NeverMakesLoopsCross()
        {
            // A thin notch: aggressive simplification would cut across it.
            var notched = new[]
            {
                new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(5.2f, 10), new float2(5.2f, 0.4f),
                new float2(4.8f, 0.4f), new float2(4.8f, 10), new float2(0, 10),
            };

            var result = PolygonSimplifier.Simplify(new[] { notched }, 3f);

            Assert.That(Polygon2D.IsSimple(result[0]));
            Assert.That(Polygon2D.SignedArea(result[0]), Is.GreaterThan(0f));
        }

        [Test]
        public void BoundaryLoops_OfATriangulatedQuad()
        {
            var vertices = new[] { new float2(0, 0), new float2(2, 0), new float2(2, 1), new float2(0, 1) };

            var loops = SpriteSource.BoundaryLoops(vertices, new[] { 0, 1, 2, 0, 2, 3 });

            Assert.That(loops.Count, Is.EqualTo(1));
            Assert.That(loops[0].Length, Is.EqualTo(4));
            Assert.That(math.abs(Polygon2D.SignedArea(loops[0])), Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void OrientByNesting_MakesInnerLoopsHoles()
        {
            var outer = new[] { new float2(0, 0), new float2(4, 0), new float2(4, 4), new float2(0, 4) };
            var inner = new[] { new float2(1, 1), new float2(3, 1), new float2(3, 3), new float2(1, 3) }; // also CCW
            var outerCw = outer.Reverse().ToArray();

            var loops = Polygon2D.OrientByNesting(new[] { outerCw, inner });

            Assert.That(Polygon2D.SignedArea(loops[0]), Is.GreaterThan(0f));
            Assert.That(Polygon2D.SignedArea(loops[1]), Is.LessThan(0f));
            Assert.That(Polygon2D.Area(loops), Is.EqualTo(12f).Within(1e-5f));
            Assert.That(Polygon2D.Contains(loops, new float2(0.5f, 0.5f)), Is.True);
            Assert.That(Polygon2D.Contains(loops, new float2(2, 2)), Is.False);
        }
    }
}
