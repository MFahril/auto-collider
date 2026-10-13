using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M8: 2D generation end to end — sprites and flat meshes → Collider2D components, Undo, ownership, the group API.</summary>
    public class Generation2DTests
    {
        private const string MeshFolder = "Assets/__AutoCollider2DTests";
        private TestScene _scene;
        private TestSprites _sprites;

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _sprites = new TestSprites();
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Cleanup(MeshFolder);
            _sprites.DestroyAll();
        }

        private static GenerationSettings Settings(ColliderStrategy2D strategy = ColliderStrategy2D.Auto) => new() { Strategy2D = strategy };

        private static BatchReport Generate(GenerationSettings settings, params GameObject[] targets) =>
            BatchProcessor.Generate(targets, settings, MeshFolder, showProgress: false);

        private static List<Collider2D> Generated2D(GameObject go) =>
            go.GetComponent<ColliderGroup>().Entries.Where(e => e.Generated && e.Is2D).Select(e => e.Collider2D).ToList();

        private static float PolygonArea(PolygonCollider2D polygon)
        {
            var area = 0f;
            for (var i = 0; i < polygon.pathCount; i++)
                area += Polygon2D.SignedArea(polygon.GetPath(i).Select(p => (float2)p).ToArray());
            return math.abs(area);
        }

        [Test]
        public void SquareSprite_GetsAnExactBox()
        {
            var go = TestSprites.SpriteObject("Crate", _sprites.Square(), Vector3.zero);

            Generate(Settings(), go);

            var box = (BoxCollider2D)Generated2D(go).Single();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(0.02f));
            Assert.That(box.size.y, Is.EqualTo(1f).Within(0.02f));
            Assert.That(((Vector2)box.offset).magnitude, Is.LessThan(0.02f));
            Assert.That(go.GetComponent<ColliderGroup>().Entries[0].Is2D);
        }

        [Test]
        public void DiscSprite_GetsACircle()
        {
            var go = TestSprites.SpriteObject("Ball", _sprites.Disc(), new Vector3(3, 1, 0));

            Generate(Settings(), go);

            var circle = (CircleCollider2D)Generated2D(go).Single();
            Assert.That(circle.radius, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void LSprite_GetsAPolygonWithTheLArea()
        {
            var go = TestSprites.SpriteObject("Bracket", _sprites.L(), Vector3.zero);

            // The L fills 3 / 3.5 = 0.86 of its hull — just "convex enough" for Auto at the default 0.85.
            Generate(Settings(), go);
            var convex = (PolygonCollider2D)Generated2D(go).Single();
            Assert.That(convex.pathCount, Is.EqualTo(1));
            Assert.That(PolygonArea(convex), Is.EqualTo(3.5f).Within(0.15f));

            Generate(Settings(ColliderStrategy2D.Polygon), go);
            var polygon = (PolygonCollider2D)Generated2D(go).Single();
            Assert.That(PolygonArea(polygon), Is.EqualTo(3f).Within(0.15f));
        }

        [Test]
        public void RingSprite_PolygonKeepsTheHole()
        {
            var go = TestSprites.SpriteObject("Ring", _sprites.Ring(), Vector3.zero);

            Generate(Settings(ColliderStrategy2D.Polygon), go);

            var polygon = (PolygonCollider2D)Generated2D(go).Single();
            Assert.That(polygon.pathCount, Is.EqualTo(2));
            Assert.That(PolygonArea(polygon), Is.EqualTo(math.PI * (1f - 0.25f)).Within(0.15f));
            Assert.That(polygon.OverlapPoint(go.transform.position), Is.False, "The hole is empty");
        }

        [TestCase(OutlineSource.Auto)]
        [TestCase(OutlineSource.Alpha)]
        public void PixelTracing_FitsTheDiscExactly(OutlineSource source)
        {
            var go = TestSprites.SpriteObject("Ball", _sprites.Disc(), Vector3.zero);
            var settings = Settings(ColliderStrategy2D.Circle);
            settings.OutlineSource = source;

            Generate(settings, go);

            var circle = (CircleCollider2D)Generated2D(go).Single();
            Assert.That(circle.radius, Is.EqualTo(0.5f).Within(0.02f));
        }

        [TestCase(OutlineSource.PhysicsShape)]
        [TestCase(OutlineSource.SpriteMesh)]
        public void ShapeAndMeshSources_EncloseTheSprite(OutlineSource source)
        {
            // Unity's generated physics shape / tight mesh are coarse polygons around the opaque pixels.
            var go = TestSprites.SpriteObject("Ball", _sprites.Disc(), Vector3.zero);
            var settings = Settings(ColliderStrategy2D.Circle);
            settings.OutlineSource = source;

            Generate(settings, go);

            var circle = (CircleCollider2D)Generated2D(go).Single();
            Assert.That(circle.radius, Is.InRange(0.49f, 0.62f));
        }

        [Test]
        public void MixedTarget_Gets3DAnd2DContainers()
        {
            var root = new GameObject("Diorama");
            var cube = new GameObject("Cube");
            cube.transform.SetParent(root.transform, false);
            cube.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(TestMeshes.Box(new float3(-0.5f), new float3(0.5f)));
            cube.AddComponent<MeshRenderer>();
            TestSprites.SpriteObject("Sprite", _sprites.Square(), new Vector3(3, 0, 0), root.transform);

            Generate(new GenerationSettings(), root);

            var group = root.GetComponent<ColliderGroup>();
            Assert.That(group.Entries.Count(e => e.Collider != null), Is.EqualTo(1));
            Assert.That(group.Entries.Count(e => e.Is2D), Is.EqualTo(1));
            Assert.That(group.Container != null && group.Container2D != null && group.Container != group.Container2D);
            Assert.That(group.Container2D.name, Is.EqualTo(ColliderWriter.Container2DName(AutoColliderSettings.instance.Data.ResolvedContainerName)));

            BatchProcessor.RemoveGenerated(new[] { root });
            Assert.That(root.transform.childCount, Is.EqualTo(2), "Both containers removed, the cube and sprite stay");
        }

        [Test]
        public void NonUniformScale_CircleCoversTheStretchedSprite()
        {
            var go = TestSprites.SpriteObject("Egg", _sprites.Disc(), Vector3.zero);
            go.transform.localScale = new Vector3(2, 1, 1);

            var report = Generate(Settings(ColliderStrategy2D.Circle), go);

            var circle = (CircleCollider2D)Generated2D(go).Single();
            // Unity scales a 2D circle by the larger axis: world radius = radius × 2 = half the stretched width.
            Assert.That(circle.radius * 2f, Is.EqualTo(1f).Within(0.03f));
            Assert.That(report.Results[0].Warnings, Does.Contain(Strings.WarningNonUniformScale));
        }

        [Test]
        public void RotatedRectangle_GetsARotatedBoxOnItsOwnChild()
        {
            var root = new GameObject("Plank Holder");
            var plank = TestSprites.SpriteObject("Plank", _sprites.Rectangle(2f, 0.5f), Vector3.zero, root.transform);
            plank.transform.localRotation = Quaternion.Euler(0, 0, 30);

            Generate(Settings(ColliderStrategy2D.Box), root);

            var box = (BoxCollider2D)Generated2D(root).Single();
            Assert.That(box.transform.localEulerAngles.z, Is.EqualTo(30f).Within(0.5f).Or.EqualTo(300f).Within(0.5f));
            Assert.That(box.size.x * box.size.y, Is.EqualTo(1f).Within(0.03f));
            Assert.That(root.GetComponent<ColliderGroup>().Entries[0].OwnsGameObject);
        }

        [Test]
        public void FlatMesh_WithRigidbody2D_GetsA2DCollider_WithoutStays3D()
        {
            var flat = new GameObject("Flat");
            flat.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(TestMeshes.Box(new float3(-0.5f, -1, -0.25f), new float3(0.5f, 1, 0.25f)));
            flat.AddComponent<MeshRenderer>();
            flat.AddComponent<Rigidbody2D>();
            var solid = new GameObject("Solid");
            solid.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(TestMeshes.Box(new float3(-0.5f), new float3(0.5f)));
            solid.AddComponent<MeshRenderer>();

            Generate(new GenerationSettings(), flat, solid);

            var box = (BoxCollider2D)Generated2D(flat).Single();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(0.03f));
            Assert.That(box.size.y, Is.EqualTo(2f).Within(0.03f));
            Assert.That(solid.GetComponent<ColliderGroup>().Entries.Single().Collider, Is.InstanceOf<BoxCollider>());
        }

        [Test]
        public void Dimension2D_MakesMeshesFlat_Dimension3D_SkipsSprites()
        {
            var cube = new GameObject("Cube");
            cube.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(TestMeshes.Box(new float3(-0.5f), new float3(0.5f)));
            cube.AddComponent<MeshRenderer>();
            var sprite = TestSprites.SpriteObject("Sprite", _sprites.Square(), new Vector3(3, 0, 0));

            Generate(new GenerationSettings { Dimension = ColliderDimension.TwoD }, cube);
            var report = Generate(new GenerationSettings { Dimension = ColliderDimension.ThreeD }, sprite);

            Assert.That(Generated2D(cube).Single(), Is.InstanceOf<BoxCollider2D>());
            Assert.That(sprite.GetComponent<ColliderGroup>() == null);
            Assert.That(report.Results[0].Status, Is.EqualTo(TargetStatus.Skipped));
        }

        [Test]
        public void TenSprites_OneUndo_UserCollider2DUntouched()
        {
            var targets = new List<GameObject>();
            for (var i = 0; i < 10; i++)
                targets.Add(TestSprites.SpriteObject($"Sprite {i}", i % 2 == 0 ? _sprites.Square() : _sprites.Disc(), new Vector3(i * 2, 0, 0)));
            var user = targets[3].AddComponent<BoxCollider2D>();

            Generate(Settings(), targets.ToArray());
            Assert.That(targets.All(t => Generated2D(t).Count == 1));

            Generate(Settings(), targets.ToArray()); // regenerate replaces generated colliders only
            Assert.That(user != null);
            Assert.That(Generated2D(targets[3]).Count, Is.EqualTo(1));

            Undo.PerformUndo();
            Undo.PerformUndo();
            Assert.That(targets.All(t => t.GetComponent<ColliderGroup>() == null));
            Assert.That(targets.All(t => t.GetComponentsInChildren<Collider2D>().Length == (t == targets[3] ? 1 : 0)));
        }

        [Test]
        public void CombinedGranularity_FitsTheUnion()
        {
            var root = new GameObject("Pair");
            TestSprites.SpriteObject("Left", _sprites.Square(), new Vector3(-0.5f, 0, 0), root.transform);
            TestSprites.SpriteObject("Right", _sprites.Square(), new Vector3(0.5f, 0, 0), root.transform);
            var settings = Settings();
            settings.Granularity = MeshGranularity.Combined;

            Generate(settings, root);

            var box = (BoxCollider2D)Generated2D(root).Single();
            Assert.That(box.size.x, Is.EqualTo(2f).Within(0.05f));
            Assert.That(box.size.y, Is.EqualTo(1f).Within(0.05f));
        }

        [Test]
        public void EdgeStrategy_MakesClosedEdgeLoops()
        {
            var go = TestSprites.SpriteObject("Ring", _sprites.Ring(), Vector3.zero);

            Generate(Settings(ColliderStrategy2D.Edge), go);

            var edges = Generated2D(go).Cast<EdgeCollider2D>().ToList();
            Assert.That(edges.Count, Is.EqualTo(2), "Outer border and hole border");
            Assert.That(edges.All(e => e.points[0] == e.points[e.pointCount - 1]));
        }

        [Test]
        public void RemoveGenerated_Deletes2DCollidersAndTheirObjects()
        {
            var root = new GameObject("Plank Holder");
            TestSprites.SpriteObject("Plank", _sprites.Rectangle(2f, 0.5f), Vector3.zero, root.transform).transform.localRotation =
                Quaternion.Euler(0, 0, 30);
            Generate(Settings(ColliderStrategy2D.Box), root);

            BatchProcessor.RemoveGenerated(new[] { root });

            Assert.That(root.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(root.GetComponent<ColliderGroup>() == null);
            Assert.That(root.transform.childCount, Is.EqualTo(1), "Only the sprite is left");
        }

        [Test]
        public void Group_TreatsBothKindsAlike()
        {
            // Unity doesn't allow 2D and 3D colliders on one object: one child each.
            var go = new GameObject("Mixed");
            var group = go.AddComponent<ColliderGroup>();
            var box3D = new GameObject("3D").AddComponent<BoxCollider>();
            box3D.transform.SetParent(go.transform, false);
            var circle = new GameObject("2D").AddComponent<CircleCollider2D>();
            circle.transform.SetParent(go.transform, false);
            group.AddEntry(new ColliderEntry(box3D, "3D", 0, true));
            group.AddEntry(new ColliderEntry(circle, "2D", 1, true));

            group.SetTrigger(true);
            group.SetEnabled(false);
            group.SetLayerOverrides(1 << 4, 0, 3);

            Assert.That(group.LiveCount, Is.EqualTo(2));
            Assert.That(circle.isTrigger && !circle.enabled && box3D.isTrigger && !box3D.enabled);
            Assert.That(circle.includeLayers.value, Is.EqualTo(1 << 4));
            Assert.That(group.TryGetEntry(circle, out var entry) && entry.Label == "2D");
            Assert.That(group.RemoveMissing(), Is.EqualTo(0), "2D entries are not 'missing'");
            Object.DestroyImmediate(circle);
            Assert.That(group.RemoveMissing(), Is.EqualTo(1));
        }

        [Test]
        public void RepeatedSprites_AreFittedOnce()
        {
            var disc = _sprites.Disc();
            var targets = Enumerable.Range(0, 5).Select(i => TestSprites.SpriteObject($"Ball {i}", disc, new Vector3(i * 2, 0, 0))).ToArray();

            Generate(Settings(), targets);

            Assert.That(FitCache.Hits, Is.EqualTo(4));
        }
    }
}
