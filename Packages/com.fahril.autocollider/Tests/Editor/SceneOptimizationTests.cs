using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>Optimized Mesh generation and in-place optimization of existing MeshColliders.</summary>
    public class SceneOptimizationTests
    {
        private const string Folder = "Assets/__AutoColliderOptimizeTests";

        private TestScene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            GeneratedMeshStore.EnsureFolder(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Cleanup();
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>Dense sphere mesh saved as an asset (like an imported model).</summary>
        private static Mesh DenseMeshAsset(string name)
        {
            var mesh = TestMeshes.ToMesh(TestMeshes.UvSphere(1f, 96, 192));
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, $"{Folder}/{name}.asset");
            return mesh;
        }

        private static GameObject Renderable(Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        private static GenerationSettings OptimizedMesh() => new() { Strategy = ColliderStrategy.OptimizedMesh };

        [Test]
        public void Generate_OptimizedMesh_CreatesSimplifiedConcaveCollider()
        {
            var go = Renderable(DenseMeshAsset("Rock"), "Rock");

            var report = BatchProcessor.Generate(new[] { go }, OptimizedMesh(), Folder, false);

            var collider = (MeshCollider)go.GetComponent<ColliderGroup>().Entries.Single().Collider;
            Assert.That(collider.convex, Is.False);
            Assert.That(AssetDatabase.Contains(collider.sharedMesh));
            Assert.That(report.Stats.Savings, Is.GreaterThan(0.8f));
            Assert.That(collider.sharedMesh.triangles.Length / 3, Is.EqualTo(report.Stats.ResultTriangles));
        }

        [Test]
        public void Generate_OptimizedMesh_OnDynamicRigidbody_FallsBackToConvex()
        {
            var go = Renderable(DenseMeshAsset("Crate"), "Crate");
            go.AddComponent<Rigidbody>();

            var report = BatchProcessor.Generate(new[] { go }, OptimizedMesh(), Folder, false);

            var collider = (MeshCollider)go.GetComponent<ColliderGroup>().Entries.Single().Collider;
            Assert.That(collider.convex, Is.True);
            Assert.That(report.Results[0].Status, Is.EqualTo(TargetStatus.Warning));
        }

        [Test]
        public void Generate_Auto_OnDynamicConcaveObject_CreatesConvexParts_OneUndo()
        {
            var mesh = TestMeshes.ToMesh(TestMeshes.LShape());
            AssetDatabase.CreateAsset(mesh, $"{Folder}/L.asset");
            var go = Renderable(mesh, "Bracket");
            go.AddComponent<Rigidbody>();

            var report = BatchProcessor.Generate(new[] { go }, new GenerationSettings(), Folder, false);

            var colliders = go.GetComponent<ColliderGroup>().Entries.Select(e => e.Collider).ToList();
            Assert.That(colliders.Count, Is.GreaterThan(1), "Several parts");
            Assert.That(colliders.All(c => !(c is MeshCollider { convex: false })), "Every part is convex (allowed on a moving Rigidbody)");
            Assert.That(report.Results[0].Notes.Any(n => n.Contains(Strings.StrategyConvexDecomposition)), "Auto explains its choice");
            Assert.That(report.Stats.ResultParts, Is.EqualTo(colliders.Count));

            Undo.PerformUndo();
            Assert.That(go.GetComponent<ColliderGroup>() == null, "One Undo removes all parts");
        }

        [Test]
        public void ReadMesh_IgnoresUnreferencedVertices()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(100, 100, 100) }, triangles = new[] { 0, 1, 2 } };

            MeshSource.ReadMesh(mesh, out var vertices, out var triangles);

            Assert.That(vertices.Length, Is.EqualTo(3), "The far, unused vertex must not influence colliders");
            Assert.That(triangles, Is.EqualTo(new[] { 0, 1, 2 }));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Generate_Inflate_GrowsPrimitive()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var settings = new GenerationSettings { Strategy = ColliderStrategy.Box, Inflate = 0.05f };

            BatchProcessor.Generate(new[] { cube }, settings, Folder, false);

            var box = (BoxCollider)cube.GetComponent<ColliderGroup>().Entries.Single().Collider;
            Assert.That(box.size.x, Is.EqualTo(1.1f).Within(1e-5f));
        }

        [Test]
        public void OptimizeExisting_ApplyRestoreAndUndo()
        {
            var original = DenseMeshAsset("Statue");
            var go = new GameObject("Statue");
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = original;

            var colliders = ColliderOptimizer.Collect(new[] { go });
            Assert.That(colliders, Is.EqualTo(new[] { collider }));

            var analysis = ColliderOptimizer.Analyze(collider, new GenerationSettings());
            Assert.That(analysis.CanApply);
            Assert.That(analysis.Result.Stats.Savings, Is.GreaterThan(0.8f));
            Assert.That(collider.sharedMesh, Is.SameAs(original), "Analyze must not change anything");

            Assert.That(ColliderOptimizer.Apply(new[] { analysis }, Folder), Is.EqualTo(1));
            var optimized = collider.sharedMesh;
            Assert.That(optimized, Is.Not.SameAs(original));
            Assert.That(GeneratedMeshStore.TryGetSource(optimized, out var source) && source == original, "Optimized mesh remembers its source");

            // Re-analysis starts from the original, not from the optimized mesh.
            var again = ColliderOptimizer.Analyze(collider, new GenerationSettings());
            Assert.That(again.SourceMesh, Is.SameAs(original));
            Assert.That(again.IsOptimized);

            Undo.PerformUndo();
            Assert.That(collider.sharedMesh, Is.SameAs(original), "One Undo reverts the optimization");

            Undo.PerformRedo();
            Assert.That(ColliderOptimizer.RestoreOriginals(new[] { collider }), Is.EqualTo(1));
            Assert.That(collider.sharedMesh, Is.SameAs(original));
        }

        [Test]
        public void OptimizeExisting_Convex_StaysWithinPhysXLimit()
        {
            // Small enough for PhysX to cook as convex without warnings; the 255-triangle cap itself is covered by QuickHullTests.
            var mesh = TestMeshes.ToMesh(TestMeshes.UvSphere(1f, 8, 16));
            AssetDatabase.CreateAsset(mesh, $"{Folder}/Barrel.asset");
            var go = new GameObject("Barrel");
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            var analysis = ColliderOptimizer.Analyze(collider, new GenerationSettings());

            Assert.That(analysis.Problem, Is.Null);
            Assert.That(analysis.Result.Mesh.TriangleCount, Is.LessThanOrEqualTo(PhysicsLimits.MaxConvexPolygons));
            Assert.That(analysis.Result.Stats.MaxError, Is.LessThan(0.01f), "Hull of a convex mesh matches it");
        }

        [Test]
        public void OptimizeExisting_ConcaveOnDynamicBody_IsReportedNotApplied()
        {
            var go = new GameObject("Bad");
            go.AddComponent<Rigidbody>().isKinematic = false;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = DenseMeshAsset("Bad");

            var analysis = ColliderOptimizer.Analyze(collider, new GenerationSettings());

            Assert.That(analysis.Problem, Is.Not.Null);
            Assert.That(analysis.CanApply, Is.False);
        }
    }
}
