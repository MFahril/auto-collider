using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>
    /// Scene-level generation tests. The test runner already isolates EditMode runs in its own untitled scene;
    /// every root object a test creates is destroyed afterwards, and meshes go to a temporary folder.
    /// </summary>
    public class BatchProcessorTests
    {
        private const string MeshFolder = "Assets/__AutoColliderTestMeshes";

        private HashSet<GameObject> _rootsBefore;

        [SetUp]
        public void SetUp()
        {
            _rootsBefore = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_rootsBefore.Contains(root)) Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        private static GenerationSettings Settings(ColliderStrategy strategy = ColliderStrategy.Box) => new() { Strategy = strategy };

        /// <summary>A primitive whose own (user) collider is kept, so we can check the tool never touches it.</summary>
        private static GameObject Primitive(PrimitiveType type, string name, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            return go;
        }

        private static List<GameObject> Cubes(int count)
        {
            var list = new List<GameObject>();
            for (var i = 0; i < count; i++) list.Add(Primitive(PrimitiveType.Cube, $"Cube {i}", new Vector3(i * 2, 0, 0)));
            return list;
        }

        private static List<Collider> GeneratedColliders(GameObject go) =>
            go.GetComponent<ColliderGroup>().Entries.Where(e => e.Generated).Select(e => e.Collider).ToList();

        [Test]
        public void Generate_TenObjects_OneUndoRevertsAll_UserCollidersUntouched()
        {
            var cubes = Cubes(10);

            var report = BatchProcessor.Generate(cubes, Settings(), MeshFolder, showProgress: false);

            Assert.That(report.CollidersCreated, Is.EqualTo(10));
            foreach (var cube in cubes)
            {
                Assert.That(cube.GetComponent<ColliderGroup>(), Is.Not.Null);
                var box = (BoxCollider)GeneratedColliders(cube).Single();
                Assert.That(box.size, Is.EqualTo(Vector3.one));
                Assert.That(box.transform.parent, Is.EqualTo(cube.transform));
                Assert.That(cube.GetComponent<BoxCollider>(), Is.Not.Null, "User collider must stay");
            }

            Undo.PerformUndo();

            foreach (var cube in cubes)
            {
                Assert.That(cube.GetComponent<ColliderGroup>() == null, "Group should be undone");
                Assert.That(cube.transform.childCount, Is.EqualTo(0), "Container should be undone");
                Assert.That(cube.GetComponent<BoxCollider>(), Is.Not.Null, "User collider must survive undo");
            }
        }

        [Test]
        public void Regenerate_ReplacesGeneratedOnly_LockedSurvives()
        {
            var cube = Primitive(PrimitiveType.Cube, "Cube", Vector3.zero);
            BatchProcessor.Generate(new[] { cube }, Settings(), MeshFolder, false);
            var group = cube.GetComponent<ColliderGroup>();
            group.Entries[0].Locked = true;

            BatchProcessor.Generate(new[] { cube }, Settings(ColliderStrategy.Sphere), MeshFolder, false);

            Assert.That(group.Entries.Count, Is.EqualTo(2));
            Assert.That(group.Entries.Count(e => e.Collider is BoxCollider), Is.EqualTo(1), "Locked box kept");
            Assert.That(group.Entries.Count(e => e.Collider is SphereCollider), Is.EqualTo(1));

            BatchProcessor.Generate(new[] { cube }, Settings(ColliderStrategy.Sphere), MeshFolder, false);
            Assert.That(group.Entries.Count, Is.EqualTo(2), "Regenerate must not accumulate");
            Assert.That(cube.GetComponent<BoxCollider>(), Is.Not.Null, "User collider must stay");
        }

        [Test]
        public void Regenerate_OnExistingGroup_OneUndoRestoresPreviousColliders()
        {
            var cube = Primitive(PrimitiveType.Cube, "Cube", Vector3.zero);
            BatchProcessor.Generate(new[] { cube }, Settings(), MeshFolder, false);
            var group = cube.GetComponent<ColliderGroup>();
            var box = group.Entries.Single().Collider;

            BatchProcessor.Generate(new[] { cube }, Settings(ColliderStrategy.Sphere), MeshFolder, false);
            Assert.That(group.Entries.Single().Collider, Is.TypeOf<SphereCollider>());

            Undo.PerformUndo();
            Assert.That(group.Entries.Count, Is.EqualTo(1), "Entry list restored");
            // Undo of a destroy restores the same object under a new C# wrapper: compare with Unity's ==.
            Assert.That(group.Entries[0].Collider == box, "The previous box is back and tracked");
        }

        [Test]
        public void ReplaceAll_RemovesUserCollidersToo()
        {
            var cube = Primitive(PrimitiveType.Cube, "Cube", Vector3.zero);
            var settings = Settings();
            settings.Replace = ReplaceMode.ReplaceAll;

            BatchProcessor.Generate(new[] { cube }, settings, MeshFolder, false);

            Assert.That(cube.GetComponent<BoxCollider>() == null, "User collider replaced");
            Assert.That(cube.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1));
        }

        [Test]
        public void RemoveGenerated_RemovesGroupAndContainer()
        {
            var cubes = Cubes(3);
            BatchProcessor.Generate(cubes, Settings(), MeshFolder, false);
            Assert.That(BatchProcessor.CountRemovable(cubes), Is.EqualTo(3));

            var report = BatchProcessor.RemoveGenerated(cubes);

            Assert.That(report.CollidersRemoved, Is.EqualTo(3));
            foreach (var cube in cubes)
            {
                Assert.That(cube.GetComponent<ColliderGroup>() == null);
                Assert.That(cube.transform.childCount, Is.EqualTo(0));
                Assert.That(cube.GetComponent<BoxCollider>(), Is.Not.Null);
            }
        }

        [Test]
        public void ConvexHull_SavesDedupedMeshAssetWithinLimit()
        {
            var a = Primitive(PrimitiveType.Sphere, "Ball", Vector3.zero);
            var b = Primitive(PrimitiveType.Sphere, "Ball", new Vector3(3, 0, 0));

            BatchProcessor.Generate(new[] { a, b }, Settings(ColliderStrategy.ConvexHull), MeshFolder, false);

            var meshA = ((MeshCollider)GeneratedColliders(a).Single()).sharedMesh;
            var meshB = ((MeshCollider)GeneratedColliders(b).Single()).sharedMesh;
            Assert.That(meshA, Is.SameAs(meshB), "Identical hulls share one mesh asset");
            Assert.That(AssetDatabase.Contains(meshA), "Mesh is saved as an asset");
            Assert.That(meshA.triangles.Length / 3, Is.LessThanOrEqualTo(PhysicsLimits.MaxConvexPolygons));
            Assert.That(((MeshCollider)GeneratedColliders(a).Single()).convex);
        }

        [Test]
        public void RotatedChild_GetsOrientedBoxOnOwnChildObject()
        {
            var root = new GameObject("Root");
            var cube = Primitive(PrimitiveType.Cube, "Tilted", Vector3.zero);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localRotation = Quaternion.Euler(0, 35, 20);

            BatchProcessor.Generate(new[] { root }, Settings(), MeshFolder, false);

            var box = (BoxCollider)GeneratedColliders(root).Single();
            Assert.That(box.transform.parent.parent, Is.EqualTo(root.transform), "Rotated box lives under the container");
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var boxAxis = box.transform.rotation * axis;
                var alignment = Mathf.Max(
                    Mathf.Abs(Vector3.Dot(boxAxis, cube.transform.right)),
                    Mathf.Abs(Vector3.Dot(boxAxis, cube.transform.up)),
                    Mathf.Abs(Vector3.Dot(boxAxis, cube.transform.forward)));
                Assert.That(alignment, Is.EqualTo(1f).Within(1e-3f), "Box axes follow the tilted cube");
            }
            Assert.That(box.size.x * box.size.y * box.size.z, Is.EqualTo(1f).Within(0.01f));
            Assert.That(cube.GetComponent<BoxCollider>(), Is.Not.Null, "User collider on the child stays");
        }

        [Test]
        public void NonUniformScale_SphereEnclosesMeshInWorldSpace()
        {
            var ball = Primitive(PrimitiveType.Sphere, "Squashed", Vector3.zero);
            ball.transform.localScale = new Vector3(3, 1, 1);

            var report = BatchProcessor.Generate(new[] { ball }, Settings(ColliderStrategy.Sphere), MeshFolder, false);

            Assert.That(report.Results[0].Status, Is.EqualTo(TargetStatus.Warning), "Rotation disabled warning");
            var sphere = (SphereCollider)GeneratedColliders(ball).Single();
            var worldRadius = sphere.radius * 3f;
            Assert.That(worldRadius, Is.EqualTo(1.5f).Within(0.02f));
        }

        [Test]
        public void Granularity_PerRendererVsCombined()
        {
            var root = new GameObject("Table");
            Primitive(PrimitiveType.Cube, "Top", new Vector3(0, 1, 0)).transform.SetParent(root.transform, true);
            Primitive(PrimitiveType.Cube, "Leg", new Vector3(0, 0, 0)).transform.SetParent(root.transform, true);

            var perRenderer = Settings();
            BatchProcessor.Generate(new[] { root }, perRenderer, MeshFolder, false);
            Assert.That(GeneratedColliders(root).Count, Is.EqualTo(2));

            var combined = Settings();
            combined.Granularity = MeshGranularity.Combined;
            BatchProcessor.Generate(new[] { root }, combined, MeshFolder, false);
            var box = (BoxCollider)GeneratedColliders(root).Single();
            Assert.That(box.size.y, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Filters_ExcludePatternAndInactive()
        {
            var root = new GameObject("Root");
            Primitive(PrimitiveType.Cube, "Keep", Vector3.zero).transform.SetParent(root.transform);
            Primitive(PrimitiveType.Cube, "LOD_Skip", Vector3.zero).transform.SetParent(root.transform);
            var hidden = Primitive(PrimitiveType.Cube, "Hidden", Vector3.zero);
            hidden.transform.SetParent(root.transform);
            hidden.SetActive(false);

            var settings = Settings();
            settings.ExcludeNamePattern = "^LOD_";
            BatchProcessor.Generate(new[] { root }, settings, MeshFolder, false);

            Assert.That(GeneratedColliders(root).Count, Is.EqualTo(1));
        }

        [Test]
        public void TargetResolver_DropsNestedTargetsAndContainers()
        {
            var parent = Primitive(PrimitiveType.Cube, "Parent", Vector3.zero);
            var child = Primitive(PrimitiveType.Cube, "Child", Vector3.zero);
            child.transform.SetParent(parent.transform);
            BatchProcessor.Generate(new[] { parent }, Settings(), MeshFolder, false);
            var container = parent.GetComponent<ColliderGroup>().Container.gameObject;

            var targets = TargetResolver.Resolve(new[] { child, parent, container }, includeChildren: true);

            Assert.That(targets, Is.EqualTo(new[] { parent }));
        }

        [Test]
        public void PrefabInstance_GeneratedObjectsAreInstanceOverrides_AssetUntouched()
        {
            GeneratedMeshStore.EnsureFolder(MeshFolder);
            var source = Primitive(PrimitiveType.Cube, "PrefabSource", Vector3.zero);
            var prefabPath = MeshFolder + "/TestCube.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            BatchProcessor.Generate(new[] { instance }, Settings(), MeshFolder, false);

            var group = instance.GetComponent<ColliderGroup>();
            Assert.That(PrefabUtility.GetAddedComponents(instance).Any(c => c.instanceComponent == group), "Group is an added override");
            Assert.That(PrefabUtility.GetAddedGameObjects(instance).Any(g => g.instanceGameObject == group.Container.gameObject), "Container is an added override");
            Assert.That(prefab.GetComponent<ColliderGroup>() == null, "Prefab asset is not modified");
        }

        [Test]
        public void NoMeshes_IsSkipped()
        {
            var empty = new GameObject("Empty");

            var report = BatchProcessor.Generate(new[] { empty }, Settings(), MeshFolder, false);

            Assert.That(report.Results[0].Status, Is.EqualTo(TargetStatus.Skipped));
            Assert.That(empty.GetComponent<ColliderGroup>() == null);
        }
    }
}
