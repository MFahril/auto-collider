using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    public class DecompositionTests
    {
        private static ConvexDecomposer.Options Options(int maxParts = 12, float tolerance = 0.03f, float primitiveFit = 0.85f) => new()
        {
            Resolution = 40,
            MaxParts = maxParts,
            ConcavityTolerance = tolerance,
            PrimitiveFitThreshold = primitiveFit,
        };

        private static readonly FitContext Static = new(allowRotation: true);
        private static readonly FitContext Dynamic = new(allowRotation: true, isDynamic: true);

        /// <summary>Point inside (or within <paramref name="tolerance"/> of) a part.</summary>
        private static bool Contains(FittedShape part, float3 p, float tolerance)
        {
            switch (part.Kind)
            {
                case ShapeKind.ConvexMesh:
                    return part.Hull.MaxPlaneDistance(p) <= tolerance;
                case ShapeKind.Box:
                    return math.all(math.abs(math.mul(math.inverse(part.Rotation), p - part.Center)) <= part.Size * 0.5f + tolerance);
                case ShapeKind.Sphere:
                    return math.distance(p, part.Center) <= part.Radius + tolerance;
                case ShapeKind.Capsule:
                {
                    var axis = float3.zero;
                    axis[part.Direction] = 1f;
                    axis = math.mul(part.Rotation, axis);
                    var half = math.max(0f, part.Height * 0.5f - part.Radius);
                    var t = math.clamp(math.dot(p - part.Center, axis), -half, half);
                    return math.distance(p, part.Center + axis * t) <= part.Radius + tolerance;
                }
                default:
                    return false;
            }
        }

        private static void AssertCovers(IReadOnlyList<FittedShape> parts, IEnumerable<float3> points, float tolerance)
        {
            foreach (var p in points)
                Assert.That(parts.Any(part => Contains(part, p, tolerance)), $"Point {p} is not covered by any part");
        }

        private static void AssertValidParts(IEnumerable<FittedShape> parts)
        {
            foreach (var part in parts)
                if (part.Kind == ShapeKind.ConvexMesh)
                    Assert.That(part.Hull.TriangleCount, Is.LessThanOrEqualTo(PhysicsLimits.MaxConvexPolygons));
                else
                    Assert.That(part.Kind, Is.Not.EqualTo(ShapeKind.ConcaveMesh), "Parts must be convex");
        }

        private static float PartVolume(FittedShape part) => part.Kind == ShapeKind.ConvexMesh ? part.Hull.Volume : part.Volume;

        [Test]
        public void TriangleBoxOverlap_Cases()
        {
            var half = new float3(0.5f);
            Assert.That(Voxelizer.TriangleBoxOverlap(float3.zero, half, new float3(-1, 0, -1), new float3(1, 0, -1), new float3(0, 0, 1)), "Triangle through the center");
            Assert.That(Voxelizer.TriangleBoxOverlap(float3.zero, half, new float3(2, 2, 2), new float3(3, 2, 2), new float3(2, 3, 2)), Is.False, "Far away");
            Assert.That(Voxelizer.TriangleBoxOverlap(float3.zero, half, new float3(-1, 0.6f, -1), new float3(1, 0.6f, -1), new float3(0, 0.6f, 1)), Is.False, "Parallel, just above");
            Assert.That(Voxelizer.TriangleBoxOverlap(float3.zero, half, new float3(0.4f, 0.4f, 0.4f), new float3(5, 0.4f, 0.4f), new float3(0.4f, 5, 0.4f)), "Corner inside");
        }

        [Test]
        public void Voxelize_ClosedBox_IsSolid_OpenGrid_IsShell()
        {
            var box = Voxelizer.Voxelize(TestMeshes.Box(float3.zero, new float3(2, 1, 1)).Vertices,
                TestMeshes.Box(float3.zero, new float3(2, 1, 1)).Triangles, 40);
            Assert.That(box.Cells.Count(c => c == VoxelGrid.Interior), Is.GreaterThan(0));
            Assert.That(box.SolidVolume, Is.EqualTo(2f).Within(2f * 0.15f));

            var grid = TestMeshes.Grid(2f, 8);
            var shell = Voxelizer.Voxelize(grid.Vertices, grid.Triangles, 40);
            Assert.That(shell.Cells.Count(c => c == VoxelGrid.Interior), Is.EqualTo(0));
            Assert.That(shell.Samples.Length, Is.GreaterThan(0));
        }

        [Test]
        public void Decompose_Sphere_IsOnePart()
        {
            var sphere = TestMeshes.UvSphere(1f, 24, 48);

            var hull = ConvexDecomposer.Decompose(sphere, Options(primitiveFit: 1f), Static);
            var primitive = ConvexDecomposer.Decompose(sphere, Options(), Static);

            Assert.That(hull.Parts.Count, Is.EqualTo(1));
            Assert.That(hull.Parts[0].Kind, Is.EqualTo(ShapeKind.ConvexMesh));
            Assert.That(primitive.Parts.Single().Kind, Is.EqualTo(ShapeKind.Sphere), "A spherical part becomes a sphere primitive");
            AssertCovers(primitive.Parts, sphere.Vertices, 0.05f);
        }

        [Test]
        public void Decompose_LShape_SplitsIntoTightConvexParts()
        {
            var l = TestMeshes.LShape();

            var result = ConvexDecomposer.Decompose(l, Options(primitiveFit: 1f), Dynamic);

            Assert.That(result.Parts.Count, Is.InRange(2, 4));
            AssertValidParts(result.Parts);
            AssertCovers(result.Parts, l.Vertices, result.VoxelSize * 1.5f);
            var total = result.Parts.Sum(PartVolume);
            Assert.That(total, Is.LessThan(5f * 1.2f), "Parts hug the L (true volume 5)");
            Assert.That(total, Is.LessThan(7f * 0.9f), "Clearly tighter than one hull (volume 7)");
        }

        [Test]
        public void Decompose_LShape_ArmsBecomeBoxes()
        {
            var result = ConvexDecomposer.Decompose(TestMeshes.LShape(), Options(), Dynamic);

            // Initial blocks need not line up with the inner corner, so one small irregular hull may remain.
            Assert.That(result.Parts.Count(p => p.Kind == ShapeKind.Box), Is.GreaterThanOrEqualTo(2),
                $"Kinds: {string.Join(",", result.Parts.Select(p => p.Kind))}");
            AssertValidParts(result.Parts);
        }

        [Test]
        public void Decompose_Torus_KeepsTheHole()
        {
            var torus = TestMeshes.Torus(1f, 0.25f);

            var result = ConvexDecomposer.Decompose(torus, Options(primitiveFit: 1f), Dynamic);

            Assert.That(result.Parts.Count, Is.GreaterThanOrEqualTo(3));
            AssertValidParts(result.Parts);
            AssertCovers(result.Parts, torus.Vertices, result.VoxelSize * 1.5f);
            Assert.That(result.Parts.Any(p => Contains(p, float3.zero, 0f)), Is.False, "The hole in the middle stays open");
        }

        [Test]
        public void Decompose_MaxParts_IsRespectedForConnectedShapes()
        {
            var result = ConvexDecomposer.Decompose(TestMeshes.Torus(1f, 0.25f), Options(maxParts: 2, primitiveFit: 1f), Dynamic);

            Assert.That(result.Parts.Count, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Decompose_DisconnectedPieces_KeepOnePartEach()
        {
            var two = TestMeshes.Merge(TestMeshes.UvSphere(0.5f, 12, 24), TestMeshes.UvSphere(0.5f, 12, 24, new float3(3, 0, 0)));

            var result = ConvexDecomposer.Decompose(two, Options(maxParts: 1), Dynamic);

            Assert.That(result.Components, Is.EqualTo(2));
            Assert.That(result.Parts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Decompose_IsDeterministic()
        {
            var torus = TestMeshes.Torus(1f, 0.3f, 24, 12);

            var a = ConvexDecomposer.Decompose(torus, Options(primitiveFit: 1f), Dynamic);
            var b = ConvexDecomposer.Decompose(torus, Options(primitiveFit: 1f), Dynamic);

            Assert.That(a.Parts.Count, Is.EqualTo(b.Parts.Count));
            for (var i = 0; i < a.Parts.Count; i++)
                Assert.That(a.Parts[i].Hull.Vertices, Is.EqualTo(b.Parts[i].Hull.Vertices));
        }

        [Test]
        public void PrimitiveReplacer_FitsOnlyWhenTight()
        {
            QuickHull3D.TryBuild(TestShapes.BoxCorners(new float3(2, 1, 1)), out var box);
            QuickHull3D.TryBuild(TestShapes.UvSphere(1f), out var sphere);
            QuickHull3D.TryBuild(TestMeshes.Octahedron(1f).Vertices, out var octahedron);

            Assert.That(PrimitiveReplacer.TryReplace(box, Static, 0.85f).Kind, Is.EqualTo(ShapeKind.Box));
            Assert.That(PrimitiveReplacer.TryReplace(sphere, Static, 0.85f).Kind, Is.EqualTo(ShapeKind.Sphere));
            Assert.That(PrimitiveReplacer.TryReplace(octahedron, Static, 0.85f), Is.Null);
            Assert.That(PrimitiveReplacer.TryReplace(box, Static, 1f), Is.Null, "Threshold 1 disables replacement");
        }

        [Test]
        public void Convexity_ExactForClosedMeshes()
        {
            Assert.That(AutoStrategy.IsClosed(TestMeshes.Box(float3.zero, new float3(1))));
            Assert.That(AutoStrategy.IsClosed(TestMeshes.Grid(1f, 4)), Is.False);
            Assert.That(AutoStrategy.Convexity(FitInput.Create(TestMeshes.Box(float3.zero, new float3(1)))), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(AutoStrategy.Convexity(FitInput.Create(TestMeshes.LShape())), Is.EqualTo(5f / 7f).Within(0.01f));
        }

        private static ShapeKind[] Auto(MeshGeometry mesh, FitContext context) =>
            new AutoStrategy(new GenerationSettings()).Fit(FitInput.Create(mesh), context).Select(s => s.Kind).ToArray();

        [Test]
        public void Auto_PicksTheCheapestFittingCollider()
        {
            Assert.That(Auto(TestMeshes.UvSphere(1f, 16, 32), Static), Is.EqualTo(new[] { ShapeKind.Sphere }));
            Assert.That(Auto(TestMeshes.Box(float3.zero, new float3(2, 1, 1)), Static), Is.EqualTo(new[] { ShapeKind.Box }));
            Assert.That(Auto(TestMeshes.Octahedron(1f), Static), Is.EqualTo(new[] { ShapeKind.ConvexMesh }));
            Assert.That(Auto(TestMeshes.Octahedron(1f), Dynamic), Is.EqualTo(new[] { ShapeKind.ConvexMesh }));
            Assert.That(Auto(TestMeshes.LShape(), Static), Is.EqualTo(new[] { ShapeKind.ConcaveMesh }), "Static concave → optimized mesh");

            var dynamicL = Auto(TestMeshes.LShape(), Dynamic);
            Assert.That(dynamicL.Length, Is.GreaterThan(1), "Moving concave → several convex parts");
            Assert.That(dynamicL, Has.None.EqualTo(ShapeKind.ConcaveMesh));
        }

        [Test]
        public void Auto_CylinderBecomesCapsule()
        {
            var points = TestShapes.Cylinder(0.25f, 3f, new float3(0, 0, 1), 48);
            var input = FitInput.Create(new MeshGeometry("c", points, new int[0]));

            var shapes = new AutoStrategy(new GenerationSettings()).Fit(input, Static);

            Assert.That(shapes.Single().Kind, Is.EqualTo(ShapeKind.Capsule));
            Assert.That(shapes[0].Note, Does.Contain(Strings.KindCapsule));
        }

        [Test]
        public void ShapeMesher_PrimitivesMatchTheirShapes()
        {
            var box = ShapeMesher.ToMesh(new FittedShape { Kind = ShapeKind.Box, Size = new float3(1, 2, 3), Rotation = quaternion.identity });
            Assert.That(GeometryUtils.MeshVolume(box.Vertices, box.Triangles), Is.EqualTo(6f).Within(1e-4f));

            var capsule = new FittedShape { Kind = ShapeKind.Capsule, Direction = 0, Radius = 0.5f, Height = 3f, Rotation = quaternion.identity };
            foreach (var v in ShapeMesher.ToMesh(capsule).Vertices)
                Assert.That(Contains(capsule, v, 1e-4f));
            GeometryUtils.MinMax(ShapeMesher.ToMesh(capsule).Vertices, out var min, out var max);
            Assert.That(max.x - min.x, Is.EqualTo(3f).Within(1e-4f), "Capsule spans its height along X");
        }
    }
}
