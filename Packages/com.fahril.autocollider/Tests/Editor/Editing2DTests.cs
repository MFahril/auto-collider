using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M9: 2D collider editing — shape round trips, operations (one Undo each), point editing and picking.</summary>
    public class Editing2DTests
    {
        private const string MeshFolder = "Assets/__AutoColliderEditing2DTests";
        private TestScene _scene;
        private TestSprites _sprites;

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _sprites = new TestSprites();
            ColliderOperations.MeshFolderOverride = MeshFolder;
            ColliderEditState.ClearSelection();
            ColliderEditState.ShowAll();
        }

        [TearDown]
        public void TearDown()
        {
            ColliderEditState.ClearSelection();
            ColliderOperations.MeshFolderOverride = null;
            _scene.Cleanup(MeshFolder);
            _sprites.DestroyAll();
        }

        /// <summary>A sprite object with generated 2D colliders of the given strategy.</summary>
        private (GameObject go, ColliderGroup group) Generated(Sprite sprite, ColliderStrategy2D strategy, Vector3 position = default)
        {
            var go = TestSprites.SpriteObject(sprite.name, sprite, position);
            BatchProcessor.Generate(new[] { go }, new GenerationSettings { Strategy2D = strategy }, MeshFolder, false);
            return (go, go.GetComponent<ColliderGroup>());
        }

        private static Collider2D Only(ColliderGroup group) => group.Entries.Single(e => e.Is2D).Collider2D;

        private static float Area(PolygonCollider2D polygon) =>
            Enumerable.Range(0, polygon.pathCount).Sum(i => Polygon2D.SignedArea(polygon.GetPath(i).Select(p => (float2)p).ToArray()));

        [TestCase(ColliderStrategy2D.Box)]
        [TestCase(ColliderStrategy2D.Circle)]
        [TestCase(ColliderStrategy2D.Capsule)]
        [TestCase(ColliderStrategy2D.Polygon)]
        [TestCase(ColliderStrategy2D.Edge)]
        public void Shape_RoundTrip_LeavesTheColliderUnchanged(ColliderStrategy2D strategy)
        {
            var (go, group) = Generated(_sprites.L(), strategy, new Vector3(2, 1, 0));
            go.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            Physics2D.SyncTransforms();
            var collider = group.Entries.First(e => e.Is2D).Collider2D;
            var space = ColliderShapes.SpaceOf(group);
            var before = collider.bounds;

            var shape = ColliderShapes2D.ToShape(collider, group, space);
            var result = ColliderOperations2D.Apply(collider, shape);

            Assert.That(result, Is.SameAs(collider), "Same kind → updated in place");
            Assert.That(Vector3.Distance(result.bounds.center, before.center), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(result.bounds.size, before.size), Is.LessThan(1e-3f));
        }

        [Test]
        public void RotatedBox_RoundTripsItsAngle_AndRotatingASharedBoxMovesItToItsOwnChild()
        {
            var (_, group) = Generated(_sprites.Square(), ColliderStrategy2D.Box);
            var box = (BoxCollider2D)Only(group);
            var space = ColliderShapes.SpaceOf(group);
            Assert.That(box.transform, Is.EqualTo(group.Container2D), "Axis-aligned boxes share the 2D container");

            var shape = ColliderShapes2D.ToShape(box, group, space);
            shape.Angle = 30f;
            Undo.IncrementCurrentGroup();
            var rotated = ColliderOperations2D.Apply(box, shape);

            Assert.That(rotated, Is.Not.SameAs(box));
            Assert.That(rotated.transform.parent, Is.EqualTo(group.Container2D));
            Assert.That(ColliderShapes2D.ToShape(rotated, group, space).Angle, Is.EqualTo(30f).Within(0.01f));
            Assert.That(group.Entries.Single().Collider2D, Is.SameAs(rotated), "The same entry follows the rebuilt collider");

            Undo.PerformUndo();
            Assert.That(group.Entries.Single().Collider2D == box, "Undo brings the original back");
        }

        [Test]
        public void Resize_InPlace_LocksTheEntry_AndUndoes()
        {
            var (_, group) = Generated(_sprites.Square(), ColliderStrategy2D.Box);
            var box = (BoxCollider2D)Only(group);
            var shape = ColliderShapes2D.ToShape(box, group, ColliderShapes.SpaceOf(group));
            shape.Size = new float2(2f, 0.5f);

            Undo.IncrementCurrentGroup();
            ColliderOperations2D.Apply(box, shape, Strings.UndoResize);

            Assert.That(box.size.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(group.Entries[0].Locked, Is.EqualTo(AutoColliderSettings.instance.Data.LockAfterEdit));
            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void DuplicateAndDelete_MixedWith3D_OneUndoEach()
        {
            var (_, group) = Generated(_sprites.Disc(), ColliderStrategy2D.Circle);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(5, 0, 0);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            BatchProcessor.Generate(new[] { cube }, new GenerationSettings { Strategy = ColliderStrategy.Box }, MeshFolder, false);
            var circle = Only(group);
            var box = cube.GetComponent<ColliderGroup>().Entries.Single().Collider;

            var copies = ColliderOperations.Duplicate(new Component[] { circle, box });
            Assert.That(copies.Count, Is.EqualTo(2));
            Assert.That(copies.OfType<CircleCollider2D>().Count(), Is.EqualTo(1));
            Assert.That(group.Entries.Count(e => e.Is2D), Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(group.Entries.Count(e => e.Is2D), Is.EqualTo(1));

            Assert.That(ColliderOperations.Delete(new Component[] { circle, box }), Is.EqualTo(2));
            Assert.That(group.Entries.Count(e => e.IsAlive), Is.EqualTo(0));
            Undo.PerformUndo();
            Assert.That(group.Entries.Count(e => e.IsAlive), Is.EqualTo(1));
        }

        [Test]
        public void MirrorX_FlipsThePolygon_AndSwapsSideNames()
        {
            var (_, group) = Generated(_sprites.L(), ColliderStrategy2D.Polygon, new Vector3(0, 0, 0));
            var polygon = (PolygonCollider2D)Only(group);
            group.Entries[0].Label = "Bracket_L";
            var space = ColliderShapes.SpaceOf(group);
            var source = ColliderShapes2D.ToShape(polygon, group, space);

            var mirrored = (PolygonCollider2D)ColliderOperations.Mirror(new Component[] { polygon }, 0).Single();

            var shape = ColliderShapes2D.ToShape(mirrored, group, space);
            Assert.That(group.Entries.Single(e => e.Collider2D == mirrored).Label, Is.EqualTo("Bracket_R"));
            Assert.That(Area(mirrored), Is.EqualTo(Area(polygon)).Within(1e-3f));
            foreach (var p in source.Paths[0])
                Assert.That(shape.Paths[0].Min(q => math.distance(q, new float2(-p.x, p.y))), Is.LessThan(1e-3f));
        }

        [Test]
        public void Merge_Convert_Inflate()
        {
            var a = Generated(_sprites.Square(), ColliderStrategy2D.Box, Vector3.zero);
            var b = TestSprites.SpriteObject("B", _sprites.Square(), new Vector3(1.5f, 0, 0), a.go.transform);
            BatchProcessor.Generate(new[] { a.go }, new GenerationSettings { Strategy2D = ColliderStrategy2D.Box }, MeshFolder, false);
            var boxes = a.group.Entries.Where(e => e.Is2D).Select(e => (Component)e.Collider2D).ToList();
            Assert.That(boxes.Count, Is.EqualTo(2));

            var merged = (BoxCollider2D)ColliderOperations.Merge(boxes, ColliderOperations.MergeKind.Box).Single();
            Assert.That(merged.size.x, Is.EqualTo(2.5f).Within(0.03f));
            Assert.That(a.group.Entries.Count(e => e.Is2D), Is.EqualTo(1));

            var circle = (CircleCollider2D)ColliderOperations.Convert(new Component[] { merged }, ShapeKind.Sphere).Single();
            Assert.That(circle.radius, Is.EqualTo(math.sqrt(2.5f * 2.5f + 1f) / 2f).Within(0.03f));
            var polygon = (PolygonCollider2D)ColliderOperations.Convert(new Component[] { circle }, ShapeKind.ConvexMesh).Single();
            Assert.That(polygon.pathCount, Is.EqualTo(1));

            var before = Area(polygon);
            ColliderOperations.Inflate(new Component[] { polygon }, 0.1f);
            Assert.That(Area((PolygonCollider2D)a.group.Entries.Single(e => e.Is2D).Collider2D), Is.GreaterThan(before));
            Assert.That(b != null);
        }

        [Test]
        public void Refit_ShrinksTheColliderToTheSprite()
        {
            var (_, group) = Generated(_sprites.Disc(), ColliderStrategy2D.Box);
            var box = (BoxCollider2D)Only(group);
            var shape = ColliderShapes2D.ToShape(box, group, ColliderShapes.SpaceOf(group));
            shape.Size *= 1.6f;
            box = (BoxCollider2D)ColliderOperations2D.Apply(box, shape);

            var refit = ColliderOperations.Refit(new Component[] { box });

            Assert.That(refit, Is.EqualTo(1));
            Assert.That(box.size.x, Is.EqualTo(1f).Within(0.03f));
        }

        [Test]
        public void PointEditing_MoveInsertDelete_Simplify()
        {
            var (_, group) = Generated(_sprites.L(), ColliderStrategy2D.Polygon);
            var polygon = (PolygonCollider2D)Only(group);
            var count = polygon.GetPath(0).Length;
            var t = polygon.transform;

            Undo.IncrementCurrentGroup();
            ColliderOperations2D.MovePoint(polygon, 0, 0, t.TransformPoint(new Vector3(-3, -3, 0)));
            Assert.That(Vector2.Distance(polygon.GetPath(0)[0], new Vector2(-3, -3)), Is.LessThan(1e-4f));
            Undo.PerformUndo();
            Assert.That(Vector2.Distance(polygon.GetPath(0)[0], new Vector2(-3, -3)), Is.GreaterThan(0.5f));

            var a = (Vector3)(Vector2)polygon.GetPath(0)[0];
            var b = (Vector3)(Vector2)polygon.GetPath(0)[1];
            ColliderOperations2D.InsertPoint(polygon, 0, 0, t.TransformPoint((a + b) * 0.5f));
            Assert.That(polygon.GetPath(0).Length, Is.EqualTo(count + 1));

            ColliderOperations2D.DeletePoint(polygon, 0, 1);
            Assert.That(polygon.GetPath(0).Length, Is.EqualTo(count));
            Undo.PerformUndo();
            Assert.That(polygon.GetPath(0).Length, Is.EqualTo(count + 1));

            var removed = ColliderOperations2D.Simplify(polygon, 0.01f);
            Assert.That(removed, Is.GreaterThanOrEqualTo(1), "The collinear inserted point is removed");
        }

        [Test]
        public void Picking_Finds2DCollidersAlongTheRay_NearestFirst()
        {
            var (_, front) = Generated(_sprites.Square(), ColliderStrategy2D.Box, new Vector3(0, 0, 0));
            var (_, back) = Generated(_sprites.Disc(), ColliderStrategy2D.Circle, new Vector3(0, 0, 5));
            var (_, edges) = Generated(_sprites.Square(), ColliderStrategy2D.Edge, new Vector3(4, 0, 0));
            var candidates = ColliderShapes.ComponentsOf(new[] { front, back, edges }).ToList();

            var hits = ColliderPicker.PickAll(new Ray(new Vector3(0.1f, 0.1f, -10), Vector3.forward), candidates);
            Assert.That(hits, Is.EqualTo(new Component[] { Only(front), Only(back) }));

            var onEdge = ColliderPicker.PickAll(new Ray(new Vector3(4.5f, 0, -10), Vector3.forward), candidates);
            var inside = ColliderPicker.PickAll(new Ray(new Vector3(4f, 0, -10), Vector3.forward), candidates);
            Assert.That(onEdge, Is.EqualTo(new Component[] { Only(edges) }));
            Assert.That(inside, Is.Empty, "Edge colliders are hollow");
        }

        [Test]
        public void LockAndRename_Work_On2D()
        {
            var (_, group) = Generated(_sprites.Square(), ColliderStrategy2D.Box);
            var box = Only(group);

            ColliderOperations.SetLocked(new Component[] { box }, true);
            ColliderOperations.Rename(box, "Lid");

            Assert.That(group.Entries[0].Locked);
            Assert.That(group.Entries[0].Label, Is.EqualTo("Lid"));
        }
    }
}
