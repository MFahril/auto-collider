using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    public class FitterTests
    {
        [Test]
        public void Aabb_Box_IsExact()
        {
            var points = TestShapes.BoxCorners(new float3(2, 4, 6), center: new float3(1, 2, 3));

            var box = BoxFitter.Aabb(points);

            Assert.That(box.Size, Is.EqualTo(new float3(2, 4, 6)));
            Assert.That(box.Center, Is.EqualTo(new float3(1, 2, 3)));
        }

        [Test]
        public void MinVolume_RotatedBox_RecoversVolumeAndRotation()
        {
            var rotation = math.mul(quaternion.RotateY(math.radians(40)), quaternion.RotateX(math.radians(25)));
            var points = TestShapes.BoxCorners(new float3(1, 2, 3), rotation, new float3(5, 0, -2));
            QuickHull3D.TryBuild(points, out var hull);

            var box = BoxFitter.MinVolume(hull.Vertices, hull);

            Assert.That(box.Volume, Is.EqualTo(6f).Within(6f * 0.01f));
            Assert.That(BoxFitter.Aabb(points).Volume, Is.GreaterThan(box.Volume * 1.2f));
            TestShapes.AssertBoxContains(box, points);
            Assert.That(math.length(box.Center - new float3(5, 0, -2)), Is.LessThan(1e-3f));
        }

        [Test]
        public void MinVolume_AxisAlignedBox_HasIdentityRotation()
        {
            var points = TestShapes.BoxCorners(new float3(3, 1, 2));
            QuickHull3D.TryBuild(points, out var hull);

            var box = BoxFitter.MinVolume(hull.Vertices, hull);

            Assert.That(GeometryUtils.IsNearIdentity(box.Rotation));
            Assert.That(math.all(math.abs(box.Size - new float3(3, 1, 2)) < 1e-4f), $"size {box.Size}");
        }

        [Test]
        public void Pca_FlatRotatedQuad_FitsThinBox()
        {
            var rotation = quaternion.RotateZ(math.radians(30));
            var quad = TestShapes.BoxCorners(new float3(4, 0, 2), rotation).Distinct().ToArray();

            var box = BoxFitter.Pca(quad);

            Assert.That(box.Volume, Is.LessThan(4 * 2 * 0.01f));
            TestShapes.AssertBoxContains(box, quad);
        }

        [Test]
        public void Welzl_UvSphere_RadiusWithinOnePercent()
        {
            var points = TestShapes.UvSphere(2.5f, center: new float3(1, -1, 3));

            var sphere = SphereFitter.Welzl(points);

            Assert.That(sphere.Radius, Is.EqualTo(2.5f).Within(2.5f * 0.01f));
            Assert.That(math.distance(sphere.Center, new float3(1, -1, 3)), Is.LessThan(0.025f));
            TestShapes.AssertSphereContains(sphere, points);
        }

        [Test]
        public void Welzl_CubeCorners_IsHalfDiagonal()
        {
            var sphere = SphereFitter.Welzl(TestShapes.BoxCorners(new float3(1)));

            Assert.That(sphere.Radius, Is.EqualTo(math.sqrt(3f) / 2f).Within(1e-5f));
        }

        [Test]
        public void Welzl_RandomCloud_EnclosesAndBeatsRitter()
        {
            var points = TestShapes.RandomInBox(11, 3000, new float3(2, 1, 0.5f));

            var welzl = SphereFitter.Welzl(points);
            var ritter = SphereFitter.Ritter(points);

            TestShapes.AssertSphereContains(welzl, points);
            TestShapes.AssertSphereContains(ritter, points);
            Assert.That(welzl.Radius, Is.LessThanOrEqualTo(ritter.Radius + 1e-5f));
        }

        [Test]
        public void Welzl_IsDeterministic()
        {
            var points = TestShapes.RandomInBox(5, 1000, new float3(1));

            Assert.That(SphereFitter.Welzl(points).Radius, Is.EqualTo(SphereFitter.Welzl(points).Radius));
        }

        [Test]
        public void Capsule_CylinderAlongX_PicksXAxis()
        {
            var points = TestShapes.Cylinder(0.5f, 4f, new float3(1, 0, 0));

            var capsule = CapsuleFitter.BestAxisAligned(points);

            Assert.That(capsule.Direction, Is.EqualTo(0));
            Assert.That(capsule.Radius, Is.EqualTo(0.5f).Within(0.005f));
            Assert.That(capsule.Height, Is.EqualTo(5f).Within(0.02f));
            TestShapes.AssertCapsuleContains(capsule, points);
        }

        [Test]
        public void Capsule_DiagonalCylinder_OrientedAlongAxis()
        {
            var axis = math.normalize(new float3(1, 1, 0));
            var points = TestShapes.Cylinder(0.3f, 3f, axis);

            var capsule = CapsuleFitter.BestOriented(points);

            var fittedAxis = math.mul(capsule.Rotation, new float3(0, 1, 0));
            Assert.That(math.abs(math.dot(fittedAxis, axis)), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(capsule.Radius, Is.EqualTo(0.3f).Within(0.01f));
            TestShapes.AssertCapsuleContains(capsule, points);
        }

        [Test]
        public void Capsule_ShortShape_CollapsesToSphereLikeAndEncloses()
        {
            var points = TestShapes.UvSphere(1f);

            var capsule = CapsuleFitter.BestAxisAligned(points);

            Assert.That(capsule.Height, Is.EqualTo(2f).Within(0.03f));
            TestShapes.AssertCapsuleContains(capsule, points);
        }

        [TestCase(ColliderStrategy.BestPrimitive)]
        [TestCase(ColliderStrategy.Box)]
        [TestCase(ColliderStrategy.Sphere)]
        [TestCase(ColliderStrategy.Capsule)]
        [TestCase(ColliderStrategy.ConvexHull)]
        public void Strategy_ProducesOneShape(ColliderStrategy strategy)
        {
            var geometry = new MeshGeometry("test", TestShapes.RandomInBox(9, 300, new float3(1, 2, 0.5f)), new int[0]);

            var shapes = StrategyRegistry.Get(strategy).Fit(FitInput.Create(geometry), new FitContext(true));

            Assert.That(shapes.Count, Is.EqualTo(1));
            Assert.That(shapes[0].Volume, Is.GreaterThan(0f));
        }

        [Test]
        public void BestPrimitive_PicksExpectedShapes()
        {
            ShapeKind Best(float3[] points)
            {
                var input = FitInput.Create(new MeshGeometry("t", points, new int[0]));
                return new BestPrimitiveStrategy().Fit(input, new FitContext(true))[0].Kind;
            }

            Assert.That(Best(TestShapes.UvSphere(1f)), Is.EqualTo(ShapeKind.Sphere));
            Assert.That(Best(TestShapes.Cylinder(0.25f, 3f, new float3(0, 0, 1), 48)), Is.EqualTo(ShapeKind.Capsule));
            Assert.That(Best(TestShapes.BoxCorners(new float3(2, 1, 1.5f))), Is.EqualTo(ShapeKind.Box));
        }

        [Test]
        public void DenseInput_IsThinned_ButPrimitivesStillEncloseEveryPoint()
        {
            var points = TestShapes.UvSphere(1f, 120, 240).Concat(TestShapes.RandomInBox(4, 6000, new float3(1.2f, 0.4f, 0.3f))).ToArray();
            var input = FitInput.Create(new MeshGeometry("dense", points, new int[0]));
            Assert.That(input.IsReduced, "Input should exceed the hull budget");
            var context = new FitContext(true);

            var box = BoxStrategy.FitBox(input, context);
            var sphere = SphereStrategy.FitSphere(input);
            var capsule = CapsuleStrategy.FitCapsule(input, context);

            TestShapes.AssertBoxContains(new BoxFit { Center = box.Center, Rotation = box.Rotation, Size = box.Size }, points);
            TestShapes.AssertSphereContains(new SphereFit { Center = sphere.Center, Radius = sphere.Radius }, points);
            TestShapes.AssertCapsuleContains(new CapsuleFit
            {
                Center = capsule.Center, Rotation = capsule.Rotation, Direction = capsule.Direction,
                Radius = capsule.Radius, Height = capsule.Height,
            }, points);
        }

        [Test]
        public void ConvexHullStrategy_FlatGeometry_FallsBackToBox()
        {
            var quad = new[] { new float3(0, 0, 0), new float3(1, 0, 0), new float3(0, 0, 1), new float3(1, 0, 1) };

            var shapes = new ConvexHullStrategy().Fit(FitInput.Create(new MeshGeometry("q", quad, new int[0])), new FitContext(true));

            Assert.That(shapes[0].Kind, Is.EqualTo(ShapeKind.Box));
        }
    }
}
