using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>Scene-view editing: shape round trips, every operation as one Undo step, picking, snapping, selection.</summary>
    public class EditingTests
    {
        private const string MeshFolder = "Assets/__AutoColliderEditTests";
        private HashSet<GameObject> _rootsBefore;

        [SetUp]
        public void SetUp()
        {
            _rootsBefore = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            ColliderOperations.MeshFolderOverride = MeshFolder;
            ColliderEditState.ClearSelection();
            ColliderEditState.ShowAll();
        }

        [TearDown]
        public void TearDown()
        {
            ColliderEditState.ClearSelection();
            ColliderEditState.ShowAll();
            ColliderOperations.MeshFolderOverride = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_rootsBefore.Contains(root)) Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        private static ColliderGroup Generate(GameObject target, ColliderStrategy strategy = ColliderStrategy.Box)
        {
            BatchProcessor.Generate(new[] { target }, new GenerationSettings { Strategy = strategy }, MeshFolder, false);
            return target.GetComponent<ColliderGroup>();
        }

        private static GameObject Cube(string name, Vector3 position, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        private static Collider Only(ColliderGroup group) => group.Entries.Single().Collider;

        private static void NewUndoStep() => Undo.IncrementCurrentGroup();

        private static bool LockAfterEdit => AutoColliderSettings.instance.Data.LockAfterEdit;

        // ---------- Shape round trips ----------

        [Test]
        public void ToShape_ThenApply_LeavesContainerBoxUnchanged_NonUniformScale()
        {
            var cube = Cube("Wide", Vector3.zero);
            cube.transform.localScale = new Vector3(2, 1, 0.5f);
            var group = Generate(cube);
            var box = (BoxCollider)Only(group);
            var size = box.size;
            var center = box.center;

            var shape = ColliderShapes.ToShape(box, group, ColliderShapes.SpaceOf(group));
            Assert.That(shape.Size, Is.EqualTo(new float3(2, 1, 0.5f)), "World-sized");

            var result = ColliderOperations.Apply(box, shape);
            Assert.That(result, Is.SameAs(box), "Updated in place");
            Assert.That(box.size, Is.EqualTo(size));
            Assert.That(box.center, Is.EqualTo(center));
        }

        [Test]
        public void ToShape_ThenApply_LeavesRotatedChildUnchanged()
        {
            var root = new GameObject("Root");
            var tilted = Cube("Tilted", new Vector3(1, 0, 0), root.transform);
            tilted.transform.localRotation = Quaternion.Euler(10, 35, 20);
            var group = Generate(root);
            var box = (BoxCollider)Only(group);
            var position = box.transform.position;
            var rotation = box.transform.rotation;

            ColliderOperations.Apply(box, ColliderShapes.ToShape(box, group, ColliderShapes.SpaceOf(group)));

            Assert.That(Vector3.Distance(box.transform.position, position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(box.transform.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(box.size.x * box.size.y * box.size.z, Is.EqualTo(1f).Within(0.01f));
        }

        // ---------- Handles path (Apply) ----------

        [Test]
        public void Resize_UpdatesInPlace_LocksEntry_OneUndo()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var box = (BoxCollider)Only(group);
            NewUndoStep();

            var shape = ColliderShapes.ToShape(box, group, ColliderShapes.SpaceOf(group));
            shape.Size = new float3(3, 1, 1);
            ColliderOperations.Apply(box, shape, Strings.UndoResize);

            Assert.That(box.size.x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(group.Entries[0].Locked, Is.EqualTo(LockAfterEdit), "Edited colliders are locked so Regenerate keeps them");

            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(group.Entries[0].Locked, Is.False);
        }

        [Test]
        public void Rotate_BoxOnContainer_MovesItToOwnChild_EntryKept_OneUndo()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var box = Only(group);
            Assert.That(box.transform, Is.EqualTo(group.Container));
            NewUndoStep();

            var shape = ColliderShapes.ToShape(box, group, ColliderShapes.SpaceOf(group));
            shape.Rotation = quaternion.RotateY(math.radians(30));
            var result = ColliderOperations.Apply(box, shape, Strings.UndoRotate);

            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Assert.That(group.Entries[0].Collider, Is.SameAs(result));
            Assert.That(ColliderShapes.IsDedicatedChild(result, group));
            Assert.That(Quaternion.Angle(result.transform.localRotation, Quaternion.Euler(0, 30, 0)), Is.LessThan(0.01f));

            Undo.PerformUndo();
            Assert.That(group.Entries[0].Collider == box, "Undo restores the original collider (Unity ==, the wrapper is new)");
            Assert.That(group.Container.childCount, Is.EqualTo(0));
        }

        [Test]
        public void DetachToChild_MeshCollider_KeepsMeshAndEntry()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            var group = Generate(sphere, ColliderStrategy.ConvexHull);
            var mesh = (MeshCollider)Only(group);

            var detached = (MeshCollider)ColliderOperations.DetachToChild(mesh);

            Assert.That(ColliderShapes.IsDedicatedChild(detached, group));
            Assert.That(detached.sharedMesh, Is.SameAs(group.Entries[0].Collider is MeshCollider m ? m.sharedMesh : null));
            Assert.That(detached.convex);
            Assert.That(group.Entries[0].Collider, Is.SameAs(detached));
        }

        // ---------- Operations ----------

        [Test]
        public void Duplicate_And_Delete_AreSingleUndoSteps()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var box = Only(group);

            var copies = ColliderOperations.Duplicate(new[] { box });
            Assert.That(copies.Count, Is.EqualTo(1));
            Assert.That(group.Entries.Count, Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(group.Entries.Count, Is.EqualTo(1));

            Assert.That(ColliderOperations.Delete(new[] { box }), Is.EqualTo(1));
            Assert.That(group.Entries.Count, Is.EqualTo(0));
            Undo.PerformUndo();
            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Assert.That(group.Entries[0].Collider != null, "Deleted collider comes back with its entry");
        }

        [Test]
        public void Mirror_RotatedChild_MirrorsPoseAndShape_OneUndo()
        {
            var root = new GameObject("Root");
            var tilted = Cube("Arm_L", new Vector3(2, 0.5f, 0.3f), root.transform);
            tilted.transform.localRotation = Quaternion.Euler(10, 35, 20);
            var group = Generate(root);
            var original = Only(group);

            var mirrored = ColliderOperations.Mirror(new[] { original }, 0).Single();

            Assert.That(group.Entries.Count, Is.EqualTo(2));
            Assert.That(group.Entries.Last().Label, Is.EqualTo("Arm_R"), "Left/right names swap");
            Assert.That(mirrored.bounds.center.x, Is.EqualTo(-original.bounds.center.x).Within(1e-3f));

            // Every corner of the tilted cube, mirrored across X, must lie on/in the mirrored box.
            var space = ColliderShapes.SpaceOf(group);
            var shape = ColliderShapes.ToShape(mirrored, group, space);
            foreach (var corner in TestShapes.BoxCorners(new float3(1)))
            {
                var world = tilted.transform.TransformPoint((Vector3)corner);
                world.x = -world.x;
                Assert.That(ColliderShapes.Contains(shape, ColliderShapes.ToScaled(space, world), 1e-3f), $"Corner {world} outside mirrored box");
            }

            Undo.PerformUndo();
            Assert.That(group.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void Merge_TwoBoxes_IntoOneEnclosingCollider_OneUndo()
        {
            var root = new GameObject("Table");
            Cube("Top", new Vector3(0, 1, 0), root.transform);
            Cube("Leg", new Vector3(0, 0, 0), root.transform);
            var group = Generate(root);
            var colliders = group.Entries.Select(e => e.Collider).ToList();

            var merged = ColliderOperations.Merge(colliders, ColliderOperations.MergeKind.Box).Single();

            Assert.That(group.Entries.Count, Is.EqualTo(1));
            var box = (BoxCollider)merged;
            Assert.That(box.size.y, Is.EqualTo(2f).Within(0.02f));

            Undo.PerformUndo();
            Assert.That(group.Entries.Count, Is.EqualTo(2));
            Assert.That(group.Entries.All(e => e.Collider != null));
        }

        [Test]
        public void Convert_BoxToSphere_KeepsEntry_Encloses_OneUndo()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var entry = group.Entries[0];

            var result = ColliderOperations.Convert(new[] { entry.Collider }, ShapeKind.Sphere).Single();

            Assert.That(result, Is.TypeOf<SphereCollider>());
            Assert.That(entry.Collider, Is.SameAs(result));
            Assert.That(((SphereCollider)result).radius, Is.EqualTo(math.sqrt(3f) / 2f).Within(0.01f));

            Undo.PerformUndo();
            Assert.That(entry.Collider, Is.TypeOf<BoxCollider>());
        }

        [Test]
        public void Refit_ShrinkWrapsAnOversizedBox()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var box = (BoxCollider)Only(group);
            box.size = new Vector3(1.6f, 1.6f, 1.6f);

            Assert.That(ColliderOperations.Refit(new[] { box }), Is.EqualTo(1));

            Assert.That(box.size.x, Is.EqualTo(1f).Within(1e-4f));
            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(1.6f).Within(1e-4f));
        }

        [Test]
        public void Inflate_GrowsPrimitive_OneUndo()
        {
            var group = Generate(Cube("Crate", Vector3.zero));
            var box = (BoxCollider)Only(group);

            ColliderOperations.Inflate(new[] { box }, 0.1f);

            Assert.That(box.size.x, Is.EqualTo(1.2f).Within(1e-5f));
            Undo.PerformUndo();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void LockAndRename_AreUndoable()
        {
            var root = new GameObject("Root");
            Cube("Tilted", Vector3.zero, root.transform).transform.localRotation = Quaternion.Euler(0, 40, 0);
            var group = Generate(root);
            var collider = Only(group);

            ColliderOperations.SetLocked(new[] { collider }, true);
            Assert.That(group.Entries[0].Locked);
            ColliderOperations.Rename(collider, "Lid");
            Assert.That(group.Entries[0].Label, Is.EqualTo("Lid"));
            Assert.That(collider.gameObject.name, Is.EqualTo("Lid"), "Dedicated child objects follow the name");

            Undo.PerformUndo();
            Assert.That(group.Entries[0].Label, Is.EqualTo("Tilted"));
            Undo.PerformUndo();
            Assert.That(group.Entries[0].Locked, Is.False);
        }

        // ---------- Picking, snapping, state ----------

        [Test]
        public void Picker_SortsByDistance_AndCyclesOnRepeatClicks()
        {
            var near = Generate(Cube("Near", new Vector3(0, 0, 0)));
            var far = Generate(Cube("Far", new Vector3(0, 0, 3)));
            var candidates = new[] { Only(far), Only(near) };
            var ray = new Ray(new Vector3(0, 0, -5), Vector3.forward);

            var hits = ColliderPicker.PickAll(ray, candidates);
            Assert.That(hits, Is.EqualTo(new[] { Only(near), Only(far) }));

            var spot = new Vector2(100, 100);
            var first = ColliderPicker.PickCycling(ray, candidates, spot);
            var second = ColliderPicker.PickCycling(ray, candidates, spot);
            Assert.That(first, Is.SameAs(Only(near)));
            Assert.That(second, Is.SameAs(Only(far)), "Second click at the same spot picks the collider behind");
        }

        [Test]
        public void Picker_RectSelection_UsesProjectedCenters()
        {
            var a = Generate(Cube("A", new Vector3(0, 0, 0)));
            var b = Generate(Cube("B", new Vector3(10, 0, 0)));

            var inside = ColliderPicker.InRect(new Rect(-1, -1, 2, 2), new[] { Only(a), Only(b) }, p => new Vector2(p.x, p.y));

            Assert.That(inside, Is.EqualTo(new[] { Only(a) }));
            Assert.That(ColliderPicker.RectFrom(new Vector2(5, 5), new Vector2(1, 2)), Is.EqualTo(new Rect(1, 2, 4, 3)));
        }

        [Test]
        public void VertexSnapping_MovesOnlyTheDraggedFace()
        {
            float3 oldMin = new(-1), oldMax = new(1);
            float3 newMin = new(-1, -1, -1), newMax = new(1.3f, 1, 1); // +X face dragged

            VertexSnapping.SnapChangedFaces(oldMin, oldMax, ref newMin, ref newMax, new float3(1.5f, 0.2f, 0.7f));

            Assert.That(newMax, Is.EqualTo(new float3(1.5f, 1, 1)));
            Assert.That(newMin, Is.EqualTo(new float3(-1)));

            var found = VertexSnapping.TryNearest(new[] { Vector3.zero, new Vector3(10, 0, 0) }, new Vector2(9, 0),
                p => new Vector2(p.x, p.y), 5f, out var vertex);
            Assert.That(found && vertex == new Vector3(10, 0, 0));
        }

        [TestCase("Arm_L", "Arm_R")]
        [TestCase("Leg.R", "Leg.L")]
        [TestCase("Left Wing", "Right Wing")]
        [TestCase("Bracket_Large", "Bracket_Large (Mirror)")]
        public void MirrorLabel_SwapsSides(string label, string expected)
        {
            Assert.That(ColliderShapes.MirrorLabel(label), Is.EqualTo(expected));
        }

        [Test]
        public void EditState_SelectionOpsAndVisibility()
        {
            var a = Only(Generate(Cube("A", Vector3.zero)));
            var b = Only(Generate(Cube("B", new Vector3(3, 0, 0))));

            ColliderEditState.Select(a);
            ColliderEditState.Select(b, SelectOp.Add);
            Assert.That(ColliderEditState.Selected, Is.EqualTo(new[] { a, b }));
            Assert.That(ColliderEditState.Active, Is.SameAs(b));

            ColliderEditState.Select(a, SelectOp.Toggle);
            Assert.That(ColliderEditState.Selected, Is.EqualTo(new[] { b }));

            ColliderEditState.ToggleSolo();
            Assert.That(ColliderEditState.IsVisible(b) && !ColliderEditState.IsVisible(a));
            ColliderEditState.ShowAll();
            ColliderEditState.Hide(new[] { b });
            Assert.That(ColliderEditState.IsVisible(b), Is.False);
            Assert.That(ColliderEditState.Selected, Is.Empty, "Hidden colliders leave the selection");
        }
    }
}
