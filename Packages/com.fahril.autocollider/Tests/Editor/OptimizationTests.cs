using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace AutoCollider.Editor.Tests
{
    public class OptimizationTests
    {
        private static GenerationSettings Balanced()
        {
            var settings = new GenerationSettings();
            BuiltInPresets.Balanced.ApplyTo(settings);
            return settings;
        }

        [Test]
        public void Optimize_DenseSphere_Balanced_Removes80PercentWithinError()
        {
            var sphere = TestMeshes.UvSphere(1f, 128, 256);
            GeometryUtils.MinMax(sphere.Vertices, out var min, out var max);
            var diagonal = math.length(max - min);

            var result = MeshOptimizer.Optimize(sphere, Balanced());

            Assert.That(result.Stats.Savings, Is.GreaterThanOrEqualTo(0.8f), $"{result.Stats.SourceTriangles} → {result.Stats.ResultTriangles}");
            Assert.That(result.Stats.MaxError, Is.LessThan(diagonal * 0.015f), "Max error under 1.5% of the size");
            Assert.That(MeshCleaner.CountDegenerate(result.Mesh), Is.EqualTo(0));

            GeometryUtils.MinMax(result.Mesh.Vertices, out var rMin, out var rMax);
            var cell = ClusterSimplifier.CellSizeFor(0.5f, diagonal);
            Assert.That(math.all(math.abs(rMin - min) <= cell) && math.all(math.abs(rMax - max) <= cell), "Bounds preserved within one cell");
        }

        [Test]
        public void Simplify_IsDeterministic()
        {
            var sphere = TestMeshes.UvSphere(1f, 64, 128);
            var options = new ClusterSimplifier.Options { Quality = 0.4f, Fidelity = 0.7f };

            var a = ClusterSimplifier.Simplify(sphere.Vertices, sphere.Triangles, options);
            var b = ClusterSimplifier.Simplify(sphere.Vertices, sphere.Triangles, options);

            Assert.That(a.Vertices, Is.EqualTo(b.Vertices));
            Assert.That(a.Triangles, Is.EqualTo(b.Triangles));
        }

        [Test]
        public void Simplify_QuadricRepresentatives_KeepCornersBetterThanAveraging()
        {
            var cube = MeshCleaner.Weld(TestMeshes.SubdividedCube(2f, 24), 1e-4f);

            float MaxError(float fidelity)
            {
                var simplified = ClusterSimplifier.Simplify(cube.Vertices, cube.Triangles,
                    new ClusterSimplifier.Options { Quality = 0.15f, Fidelity = fidelity });
                return ErrorMetric.Measure(cube.Vertices, simplified.Vertices, simplified.Triangles).Max;
            }

            var mean = MaxError(0f);
            var quadric = MaxError(1f);
            Assert.That(quadric, Is.LessThan(mean * 0.5f), $"quadric {quadric} vs mean {mean}");
        }

        [Test]
        public void Simplify_MaxTriangles_IsRespected()
        {
            var sphere = TestMeshes.UvSphere(1f, 64, 128);

            var result = ClusterSimplifier.Simplify(sphere.Vertices, sphere.Triangles,
                new ClusterSimplifier.Options { Quality = 1f, Fidelity = 0.5f, MaxTriangles = 500 });

            Assert.That(result.TriangleCount, Is.LessThanOrEqualTo(500));
            Assert.That(result.TriangleCount, Is.GreaterThan(50));
        }

        [Test]
        public void Simplify_HigherQuality_KeepsMoreTriangles()
        {
            var sphere = TestMeshes.UvSphere(1f, 96, 192);

            int Triangles(float quality) => ClusterSimplifier.Simplify(sphere.Vertices, sphere.Triangles,
                new ClusterSimplifier.Options { Quality = quality, Fidelity = 0.5f }).TriangleCount;

            Assert.That(Triangles(0.2f), Is.LessThan(Triangles(0.5f)));
            Assert.That(Triangles(0.5f), Is.LessThan(Triangles(0.8f)));
        }

        [Test]
        public void Weld_SplitCube_SharesVertices()
        {
            const int n = 4;
            var split = TestMeshes.SubdividedCube(1f, n);

            var welded = MeshCleaner.Weld(split, 1e-4f);

            var expected = (n + 1) * (n + 1) * (n + 1) - (n - 1) * (n - 1) * (n - 1);
            Assert.That(split.Vertices.Length, Is.EqualTo(6 * (n + 1) * (n + 1)));
            Assert.That(welded.Vertices.Length, Is.EqualTo(expected));
            Assert.That(welded.TriangleCount, Is.EqualTo(split.TriangleCount));
        }

        [Test]
        public void Weld_DropsDegenerateAndDuplicateTriangles()
        {
            var vertices = new[] { new float3(0, 0, 0), new float3(1, 0, 0), new float3(0, 1, 0), new float3(0, 0, 0) };
            var triangles = new[] { 0, 1, 2, 2, 1, 0, 0, 3, 1 }; // duplicate (reversed) + collapses after weld
            var mesh = new MeshGeometry("t", vertices, triangles);

            var welded = MeshCleaner.Weld(mesh, 1e-4f);

            Assert.That(welded.TriangleCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveSmallIslands_DropsTinyPiecesOnly()
        {
            var main = TestMeshes.UvSphere(1f, 16, 32);
            var bolt = TestMeshes.UvSphere(0.02f, 6, 12, new float3(3, 0, 0));
            var merged = TestMeshes.Merge(main, bolt);

            Assert.That(MeshCleaner.RemoveSmallIslands(merged, 0f).TriangleCount, Is.EqualTo(merged.TriangleCount));
            Assert.That(MeshCleaner.RemoveSmallIslands(merged, 0.05f).TriangleCount, Is.EqualTo(main.TriangleCount));
        }

        [Test]
        public void Inflate_Sphere_GrowsRadius()
        {
            var sphere = TestMeshes.UvSphere(1f, 24, 48);

            var inflated = InflateDeflate.Offset(sphere.Vertices, sphere.Triangles, 0.1f);

            foreach (var v in inflated) Assert.That(math.length(v), Is.EqualTo(1.1f).Within(2e-3f));
        }

        [Test]
        public void InflateShape_Primitives()
        {
            var box = new FittedShape { Kind = ShapeKind.Box, Size = new float3(1, 2, 3) };
            var capsule = new FittedShape { Kind = ShapeKind.Capsule, Radius = 0.5f, Height = 2f };

            InflateDeflate.Apply(box, 0.1f);
            InflateDeflate.Apply(capsule, -0.1f);

            Assert.That(box.Size, Is.EqualTo(new float3(1.2f, 2.2f, 3.2f)));
            Assert.That(capsule.Radius, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(capsule.Height, Is.EqualTo(1.8f).Within(1e-6f));
        }

        [Test]
        public void ErrorMetric_IdenticalIsZero_OffsetIsDistance()
        {
            var grid = TestMeshes.Grid(2f, 10);
            var lifted = TestMeshes.Grid(2f, 10, 0.05f);

            var same = ErrorMetric.Measure(grid.Vertices, grid.Vertices, grid.Triangles);
            var offset = ErrorMetric.Measure(grid.Vertices, lifted.Vertices, lifted.Triangles);

            Assert.That(same.Max, Is.LessThan(1e-6f));
            Assert.That(offset.Max, Is.EqualTo(0.05f).Within(1e-5f));
            Assert.That(offset.Mean, Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void ClosestPointOnTriangle_Regions()
        {
            float3 a = new(0, 0, 0), b = new(1, 0, 0), c = new(0, 1, 0);

            Assert.That(ErrorMetric.ClosestPointOnTriangle(new float3(0.2f, 0.2f, 5), a, b, c), Is.EqualTo(new float3(0.2f, 0.2f, 0)));
            Assert.That(ErrorMetric.ClosestPointOnTriangle(new float3(-1, -1, 0), a, b, c), Is.EqualTo(a));
            Assert.That(ErrorMetric.ClosestPointOnTriangle(new float3(0.5f, -2, 0), a, b, c), Is.EqualTo(new float3(0.5f, 0, 0)));
        }

        [Test]
        public void Presets_ApplyAndIdentify()
        {
            var settings = new GenerationSettings();
            Assert.That(BuiltInPresets.Identify(settings), Is.EqualTo(Strings.PresetBalanced), "Defaults are Balanced");

            BuiltInPresets.Mobile.ApplyTo(settings);
            Assert.That(settings.MaxTriangles, Is.EqualTo(500));
            Assert.That(BuiltInPresets.Identify(settings), Is.EqualTo(Strings.PresetMobile));

            settings.Quality = 0.123f;
            Assert.That(BuiltInPresets.Identify(settings), Is.Null);
            Assert.That(PresetMenu.CurrentName(settings), Is.EqualTo(Strings.PresetCustom));
        }

        [Test]
        public void ColliderPreset_CapturesACopy()
        {
            var preset = UnityEngine.ScriptableObject.CreateInstance<ColliderPreset>();
            var settings = new GenerationSettings { Quality = 0.9f, Strategy = ColliderStrategy.OptimizedMesh };

            preset.Capture(settings);
            settings.Quality = 0.1f;

            Assert.That(preset.Settings.Quality, Is.EqualTo(0.9f));
            Assert.That(preset.Settings.Strategy, Is.EqualTo(ColliderStrategy.OptimizedMesh));
            UnityEngine.Object.DestroyImmediate(preset);
        }

        [Test]
        public void ColliderStats_SavingsAndAdd()
        {
            var a = new ColliderStats { SourceTriangles = 1000, ResultTriangles = 100, MaxError = 0.01f };
            var b = new ColliderStats { SourceTriangles = 1000, ResultTriangles = 300, MaxError = 0.02f };

            a.Add(b);

            Assert.That(a.SourceTriangles, Is.EqualTo(2000));
            Assert.That(a.Savings, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(a.MaxError, Is.EqualTo(0.02f));
        }

        [Test]
        public void Format_Units()
        {
            Assert.That(Format.Distance(0.005f), Is.EqualTo("5 mm"));
            Assert.That(Format.Distance(0.25f), Is.EqualTo("25 cm"));
            Assert.That(Format.Bytes(2048), Is.EqualTo("2 KB"));
        }

        [Test]
        public void OptimizedMeshStrategy_ProducesConcaveShapeWithStats()
        {
            var sphere = TestMeshes.UvSphere(1f, 64, 128);

            var shapes = new OptimizedMeshStrategy(Balanced()).Fit(FitInput.Create(sphere), new FitContext(true));

            Assert.That(shapes.Single().Kind, Is.EqualTo(ShapeKind.ConcaveMesh));
            Assert.That(shapes[0].Stats.HasValue && shapes[0].Stats.Value.Savings > 0.5f);
            Assert.That(shapes[0].Error, Is.Not.Null);
        }
    }
}
