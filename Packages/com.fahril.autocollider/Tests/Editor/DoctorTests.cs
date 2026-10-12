using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M7: the Collider Doctor finds seeded problems, fixes them (one Undo step each / for all) and stays quiet on clean setups.</summary>
    public class DoctorTests
    {
        private const string MeshFolder = "Assets/__AutoColliderDoctorTests";
        private static readonly ColliderDoctor.Options Default = new(false, 1000);
        private TestScene _scene;
        private readonly List<GameObject> _roots = new();

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _roots.Clear();
            ColliderDoctor.MeshFolderOverride = MeshFolder;
        }

        [TearDown]
        public void TearDown()
        {
            ColliderDoctor.MeshFolderOverride = null;
            _scene.Cleanup();
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _roots.Add(go);
            return go;
        }

        /// <summary>A closed cube surface with 12·n² triangles.</summary>
        private static Mesh DenseCube(int n) => TestMeshes.ToMesh(TestMeshes.SubdividedCube(1f, n));

        private MeshCollider MeshColliderObject(string name, Mesh mesh, bool convex = false)
        {
            var collider = Make(name).AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = convex;
            return collider;
        }

        private List<DoctorIssue> Scan(ColliderDoctor.Options? options = null) => ColliderDoctor.Scan(_roots, options ?? Default);

        private static DoctorIssue Find(List<DoctorIssue> issues, DoctorCheck check) => issues.SingleOrDefault(i => i.Check == check);

        [Test]
        public void ConcaveOnMovingRigidbody_IsAnError_FixMakesItConvex()
        {
            var collider = MeshColliderObject("Crate", DenseCube(2));
            collider.gameObject.AddComponent<Rigidbody>();

            var issue = Find(Scan(), DoctorCheck.ConcaveOnMovingBody);

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Severity, Is.EqualTo(DoctorSeverity.Error));
            ColliderDoctor.FixOne(issue);
            Assert.That(collider.convex);
            Undo.PerformUndo();
            Assert.That(collider.convex, Is.False);
        }

        [Test]
        public void ConcaveTrigger_IsReported()
        {
            var collider = MeshColliderObject("Zone", DenseCube(2));
            // The setter refuses concave triggers, but old / hand-edited scene data can still hold one. Disabled, so PhysX
            // creates no shape (and logs nothing) while we seed it.
            collider.enabled = false;
            var serialized = new SerializedObject(collider);
            serialized.FindProperty("m_IsTrigger").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(Find(Scan(), DoctorCheck.ConcaveTrigger), Is.Not.Null);
        }

        [Test]
        public void MissingMesh_AssignsTheFilterMesh_OrRemovesTheCollider()
        {
            var withFilter = MeshColliderObject("With Filter", null);
            var mesh = DenseCube(1);
            withFilter.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var bare = MeshColliderObject("Bare", null);

            var issues = Scan().Where(i => i.Check == DoctorCheck.MissingMesh).ToList();
            Assert.That(issues.Count, Is.EqualTo(2));
            ColliderDoctor.FixAll(issues);

            Assert.That(withFilter.sharedMesh, Is.EqualTo(mesh));
            Assert.That(bare == null, "Collider without any mesh source is removed");
        }

        [Test]
        public void DetailedConcaveMesh_FixOptimizesIt()
        {
            var collider = MeshColliderObject("Rock", DenseCube(40)); // 19,200 triangles > budget 1,000
            var before = ColliderDoctor.TriangleCount(collider.sharedMesh);

            var issue = Find(Scan(), DoctorCheck.DetailedMesh);
            Assert.That(issue, Is.Not.Null);
            ColliderDoctor.FixOne(issue);

            Assert.That(ColliderDoctor.TriangleCount(collider.sharedMesh), Is.LessThan(before));
            Assert.That(Find(Scan(new ColliderDoctor.Options(false, 100_000)), DoctorCheck.DetailedMesh), Is.Null);
        }

        [Test]
        public void ComplexConvex_FixBuildsACappedHull()
        {
            var collider = MeshColliderObject("Boulder", DenseCube(10), convex: true); // 1,200 triangles, but a simple hull (no cooking warning)

            var issue = Find(Scan(), DoctorCheck.ComplexConvex);
            Assert.That(issue, Is.Not.Null);
            ColliderDoctor.FixOne(issue);

            Assert.That(ColliderDoctor.TriangleCount(collider.sharedMesh), Is.LessThanOrEqualTo(PhysicsLimits.MaxConvexPolygons));
        }

        [Test]
        public void NegativeScale_AndScaledUserPrimitive_AreInfo_GeneratedOnesAreNot()
        {
            var mirrored = MeshColliderObject("Mirrored", DenseCube(1));
            mirrored.transform.localScale = new Vector3(-1, 1, 1);
            var stretched = Make("Stretched");
            stretched.transform.localScale = new Vector3(3, 1, 1);
            stretched.AddComponent<SphereCollider>();
            var capsuleAlongX = Make("Capsule X");
            capsuleAlongX.transform.localScale = new Vector3(3, 1, 1);
            capsuleAlongX.AddComponent<CapsuleCollider>().direction = 0; // radial axes Y/Z are equal → fine

            var generated = Make("Generated");
            generated.transform.localScale = new Vector3(3, 1, 1);
            var group = generated.AddComponent<ColliderGroup>();
            var sphere = generated.AddComponent<SphereCollider>();
            group.AddEntry(new ColliderEntry(sphere, "Sphere", 0, generated: true));

            var issues = Scan();
            Assert.That(Find(issues, DoctorCheck.NegativeScale).Severity, Is.EqualTo(DoctorSeverity.Info));
            var scaled = issues.Where(i => i.Check == DoctorCheck.ScaledPrimitive).Select(i => ((Component)i.Target).gameObject).ToList();
            Assert.That(scaled, Is.EqualTo(new[] { stretched }));
        }

        [Test]
        public void DuplicateCollider_FixRemovesTheCopy_AndItsEntry()
        {
            var go = Make("Twins");
            var group = go.AddComponent<ColliderGroup>();
            var first = go.AddComponent<BoxCollider>();
            var second = go.AddComponent<BoxCollider>();
            go.AddComponent<BoxCollider>().size = new Vector3(2, 1, 1); // different → not a duplicate
            group.AddEntry(new ColliderEntry(second, "Copy", 0, generated: true));

            var issue = Find(Scan(), DoctorCheck.DuplicateCollider);
            Assert.That(issue.Target, Is.EqualTo(second));
            ColliderDoctor.FixOne(issue);

            Assert.That(second == null && first != null);
            Assert.That(group.Entries, Is.Empty);
            Undo.PerformUndo();
            Assert.That(go.GetComponents<BoxCollider>().Length, Is.EqualTo(3));
            Assert.That(group.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingEntries_FixCleansTheGroup()
        {
            var go = Make("Group");
            var group = go.AddComponent<ColliderGroup>();
            var box = go.AddComponent<BoxCollider>();
            group.AddEntry(new ColliderEntry(box, "Gone", 0, generated: true));
            Object.DestroyImmediate(box);

            var issue = Find(Scan(), DoctorCheck.MissingEntries);
            Assert.That(issue, Is.Not.Null);
            ColliderDoctor.FixOne(issue);
            Assert.That(group.Entries, Is.Empty);
        }

        [Test]
        public void AnimatedColliderWithoutReferences_IsReported()
        {
            Make("Animated").AddComponent<AnimatedMeshCollider>();

            Assert.That(Find(Scan(), DoctorCheck.AnimatedSetup), Is.Not.Null);
        }

        [Test]
        public void MeshesWithoutColliders_OnlyWhenAsked_FixGenerates()
        {
            var go = Make("Decor");
            go.AddComponent<MeshFilter>().sharedMesh = DenseCube(1);
            go.AddComponent<MeshRenderer>();

            Assert.That(Find(Scan(), DoctorCheck.NoCollider), Is.Null);
            var issue = Find(Scan(new ColliderDoctor.Options(true, 1000)), DoctorCheck.NoCollider);
            Assert.That(issue, Is.Not.Null);

            ColliderDoctor.FixOne(issue);
            Assert.That(go.GetComponent<ColliderGroup>(), Is.Not.Null);
            Assert.That(Find(Scan(new ColliderDoctor.Options(true, 1000)), DoctorCheck.NoCollider), Is.Null);
        }

        [Test]
        public void FixAll_IsOneUndoStep_AndErrorsComeFirst()
        {
            var moving = MeshColliderObject("Moving", DenseCube(2));
            moving.gameObject.AddComponent<Rigidbody>();
            var twins = Make("Twins");
            twins.AddComponent<SphereCollider>();
            twins.AddComponent<SphereCollider>();
            var mirrored = MeshColliderObject("Mirrored", DenseCube(1), convex: true);
            mirrored.transform.localScale = new Vector3(1, -1, 1);

            var issues = Scan();
            Assert.That(issues[0].Severity, Is.EqualTo(DoctorSeverity.Error));
            Assert.That(issues.Select(i => i.Severity), Is.Ordered);

            var fixedCount = ColliderDoctor.FixAll(issues);
            Assert.That(fixedCount, Is.EqualTo(2));
            Assert.That(Scan().Select(i => i.Check), Is.EqualTo(new[] { DoctorCheck.NegativeScale }), "Only the unfixable info remains");

            Undo.PerformUndo();
            Assert.That(Scan().Count, Is.EqualTo(issues.Count));
        }

        [Test]
        public void GeneratedColliders_AreHealthy()
        {
            var go = Make("Crate");
            go.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(TestMeshes.Box(float3.zero, new float3(1, 2, 1)));
            go.AddComponent<MeshRenderer>();
            BatchProcessor.Generate(new[] { go }, new GenerationSettings(), MeshFolder, false);

            Assert.That(Scan(new ColliderDoctor.Options(true, 1000)), Is.Empty);
        }
    }
}
