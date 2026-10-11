using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    public class GeometryTests
    {
        [Test]
        public void EigenSymmetric_Diagonal_SortedDescendingRightHanded()
        {
            var m = new double3x3(1, 0, 0, 0, 5, 0, 0, 0, 3);

            GeometryUtils.EigenSymmetric(m, out var values, out var vectors);

            Assert.That(values.x, Is.EqualTo(5).Within(1e-9));
            Assert.That(values.y, Is.EqualTo(3).Within(1e-9));
            Assert.That(values.z, Is.EqualTo(1).Within(1e-9));
            Assert.That(math.abs(vectors.c0.y), Is.EqualTo(1).Within(1e-9));
            Assert.That(math.determinant(vectors), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void EigenSymmetric_RotatedMatrix_RecoversPrincipalAxis()
        {
            var rf = new float3x3(quaternion.AxisAngle(math.normalize(new float3(1, 2, 3)), 0.7f));
            var r = new double3x3(rf.c0, rf.c1, rf.c2);
            var d = new double3x3(9, 0, 0, 0, 4, 0, 0, 0, 1);
            var m = math.mul(math.mul(r, d), math.transpose(r));

            GeometryUtils.EigenSymmetric(m, out var values, out var vectors);

            Assert.That(values.x, Is.EqualTo(9).Within(1e-6));
            Assert.That(math.abs(math.dot(vectors.c0, r.c0)), Is.EqualTo(1).Within(1e-6));
        }

        [Test]
        public void ConvexHull2D_SquareWithInteriorAndCollinear_KeepsCorners()
        {
            var points = new List<double2>
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0.5, 0.5), new(0.5, 0), new(0.2, 0.7),
            };

            var hull = GeometryUtils.ConvexHull2D(points);

            Assert.That(hull.Count, Is.EqualTo(4));
        }

        [Test]
        public void MinAreaRectDirection_RotatedRectangle_AlignsWithEdge()
        {
            var angle = math.radians(30.0);
            var u = new double2(math.cos(angle), math.sin(angle));
            var v = new double2(-u.y, u.x);
            var points = new List<double2> { u * 2 + v, -u * 2 + v, -u * 2 - v, u * 2 - v };

            var dir = GeometryUtils.MinAreaRectDirection(GeometryUtils.ConvexHull2D(points));

            var alignedU = math.abs(math.dot(dir, u));
            var alignedV = math.abs(math.dot(dir, v));
            Assert.That(math.max(alignedU, alignedV), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void CanonicalizeAxes_PermutedIdentity_BecomesIdentity()
        {
            var axes = new float3x3(new float3(0, 0, 1), new float3(1, 0, 0), new float3(0, 1, 0));
            var size = new float3(3, 1, 2);

            GeometryUtils.CanonicalizeAxes(ref axes, ref size);

            Assert.That(axes.c0, Is.EqualTo(new float3(1, 0, 0)));
            Assert.That(axes.c1, Is.EqualTo(new float3(0, 1, 0)));
            Assert.That(axes.c2, Is.EqualTo(new float3(0, 0, 1)));
            Assert.That(size, Is.EqualTo(new float3(1, 2, 3)));
        }

        [Test]
        public void MeshVolume_UnitCubeHull_IsOne()
        {
            Assert.That(QuickHull3D.TryBuild(TestShapes.BoxCorners(new float3(1)), out var hull));
            Assert.That(GeometryUtils.MeshVolume(hull.Vertices, hull.Triangles), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Dedupe_RemovesExactDuplicatesKeepingOrder()
        {
            var points = new[] { new float3(1), new float3(2), new float3(1), new float3(3) };

            var unique = GeometryUtils.Dedupe(points);

            Assert.That(unique, Is.EqualTo(new[] { new float3(1), new float3(2), new float3(3) }));
        }
    }
}
