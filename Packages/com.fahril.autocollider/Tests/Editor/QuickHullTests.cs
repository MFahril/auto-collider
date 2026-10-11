using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    public class QuickHullTests
    {
        [Test]
        public void Cube_WithInteriorPoints_Gives8Vertices12Triangles()
        {
            var points = TestShapes.BoxCorners(new float3(2)).Concat(TestShapes.RandomInBox(7, 200, new float3(0.9f))).ToArray();

            Assert.That(QuickHull3D.TryBuild(points, out var hull));

            Assert.That(hull.Vertices.Length, Is.EqualTo(8));
            Assert.That(hull.TriangleCount, Is.EqualTo(12));
            Assert.That(hull.Volume, Is.EqualTo(8f).Within(1e-4f));
        }

        [Test]
        public void RandomCloud_AllPointsInside_FacesPointOutward()
        {
            var points = TestShapes.RandomInBox(42, 2000, new float3(3, 1, 2));

            Assert.That(QuickHull3D.TryBuild(points, out var hull));

            foreach (var p in points)
                Assert.That(hull.MaxPlaneDistance(p), Is.LessThanOrEqualTo(1e-4f));

            var centroid = hull.Vertices.Aggregate(float3.zero, (s, v) => s + v) / hull.Vertices.Length;
            for (var i = 0; i < hull.Triangles.Length; i += 3)
            {
                var a = hull.Vertices[hull.Triangles[i]];
                var n = math.cross(hull.Vertices[hull.Triangles[i + 1]] - a, hull.Vertices[hull.Triangles[i + 2]] - a);
                Assert.That(math.dot(n, a - centroid), Is.GreaterThan(0f), "Face winding must point outward");
            }
        }

        [Test]
        public void Hull_IsClosedManifold()
        {
            Assert.That(QuickHull3D.TryBuild(TestShapes.UvSphere(1f), out var hull));

            // Every directed edge appears exactly once and its twin exists.
            var edges = new System.Collections.Generic.HashSet<(int, int)>();
            for (var i = 0; i < hull.Triangles.Length; i += 3)
                for (var e = 0; e < 3; e++)
                    Assert.That(edges.Add((hull.Triangles[i + e], hull.Triangles[i + (e + 1) % 3])), "Duplicate directed edge");
            foreach (var (a, b) in edges)
                Assert.That(edges.Contains((b, a)), "Open edge");

            // Euler characteristic of a sphere.
            Assert.That(hull.Vertices.Length - edges.Count / 2 + hull.TriangleCount, Is.EqualTo(2));
        }

        [Test]
        public void Coplanar_Fails()
        {
            var points = new[] { new float3(0, 0, 0), new float3(1, 0, 0), new float3(0, 0, 1), new float3(1, 0, 1), new float3(0.5f, 0, 0.5f) };

            Assert.That(QuickHull3D.TryBuild(points, out _), Is.False);
        }

        [Test]
        public void TooFewPoints_Fails()
        {
            Assert.That(QuickHull3D.TryBuild(new[] { float3.zero, new float3(1, 0, 0), new float3(0, 1, 0) }, out _), Is.False);
        }

        [Test]
        public void Capped_DenseSphere_RespectsPhysXLimitAndStaysInside()
        {
            var points = TestShapes.UvSphere(1f, 40, 80);

            Assert.That(QuickHull3D.TryBuildCapped(points, PhysicsLimits.MaxConvexPolygons, out var hull));

            Assert.That(hull.TriangleCount, Is.LessThanOrEqualTo(PhysicsLimits.MaxConvexPolygons));
            Assert.That(hull.TriangleCount, Is.GreaterThan(100), "Should keep useful detail");
            foreach (var v in hull.Vertices)
                Assert.That(math.length(v), Is.LessThanOrEqualTo(1f + 1e-5f));
            Assert.That(hull.Volume, Is.GreaterThan(4f / 3f * math.PI * 0.9f), "Reduced hull should keep >90% of the volume");
        }

        [Test]
        public void Build_IsDeterministic()
        {
            var points = TestShapes.RandomInBox(3, 500, new float3(1));

            QuickHull3D.TryBuild(points, out var a);
            QuickHull3D.TryBuild(points, out var b);

            Assert.That(a.Vertices, Is.EqualTo(b.Vertices));
            Assert.That(a.Triangles, Is.EqualTo(b.Triangles));
        }
    }
}
