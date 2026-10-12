using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M5: painted regions (paint → region → identical regenerate) and per-bone colliders for skinned meshes.</summary>
    public class PaintAndBoneTests
    {
        private const string MeshFolder = "Assets/__AutoColliderPaintTests";
        private HashSet<GameObject> _rootsBefore;

        [SetUp]
        public void SetUp()
        {
            _rootsBefore = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            ColliderOperations.MeshFolderOverride = MeshFolder;
            ColliderEditState.ClearPaint();
        }

        [TearDown]
        public void TearDown()
        {
            ColliderEditState.ClearPaint();
            ColliderOperations.MeshFolderOverride = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_rootsBefore.Contains(root)) Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        // ---------- Skinned rig ----------

        /// <summary>A straight limb along +Y: a cylinder of <paramref name="bones"/> segments, each skinned 100% to one bone.</summary>
        private static (GameObject root, SkinnedMeshRenderer skin, Transform[] bones) Limb(int bones, float radius, float segment,
            string[] names = null, float[] radii = null)
        {
            var root = new GameObject("Character");
            var chain = new Transform[bones];
            for (var i = 0; i < bones; i++)
            {
                chain[i] = new GameObject(names != null ? names[i] : $"Bone{i}").transform;
                chain[i].SetParent(i == 0 ? root.transform : chain[i - 1], false);
                chain[i].localPosition = i == 0 ? Vector3.zero : new Vector3(0, segment, 0);
            }

            var skin = new GameObject("Skin").AddComponent<SkinnedMeshRenderer>();
            skin.transform.SetParent(root.transform, false);

            const int ringsPerSegment = 6;
            const int sides = 16;
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            var rings = bones * ringsPerSegment + 1;
            for (var r = 0; r < rings; r++)
            {
                var y = r * segment / ringsPerSegment;
                var bone = Mathf.Min(r / ringsPerSegment, bones - 1);
                var ringRadius = radii != null ? radii[bone] : radius;
                for (var s = 0; s < sides; s++)
                {
                    var a = 2 * Mathf.PI * s / sides;
                    vertices.Add(new Vector3(Mathf.Cos(a) * ringRadius, y, Mathf.Sin(a) * ringRadius));
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                }
            }
            for (var r = 0; r < rings - 1; r++)
            for (var s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }

            var mesh = new Mesh { name = "Limb" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = chain.Select(b => b.worldToLocalMatrix * skin.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            skin.sharedMesh = mesh;
            skin.bones = chain;
            skin.rootBone = chain[0];
            return (root, skin, chain);
        }

        [Test]
        public void StrongestBones_RespectsThreshold()
        {
            var weights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 0.3f, boneIndex1 = 2, weight1 = 0.7f },
                new BoneWeight { boneIndex0 = 1, weight0 = 0.4f, boneIndex1 = 0, weight1 = 0.35f, boneIndex2 = 2, weight2 = 0.25f },
            };

            Assert.That(BoneFitter.StrongestBones(weights, 0.5f), Is.EqualTo(new[] { 2, -1 }));
            Assert.That(BoneFitter.StrongestBones(weights, 0.3f), Is.EqualTo(new[] { 2, 1 }));
        }

        [Test]
        public void PerBone_LimbGetsOneCapsulePerBone_ParentedToTheBone()
        {
            var (root, _, bones) = Limb(3, 0.1f, 1f);

            BatchProcessor.Generate(new[] { root }, new GenerationSettings { Strategy = ColliderStrategy.PerBone }, MeshFolder, false);

            var group = root.GetComponent<ColliderGroup>();
            Assert.That(group.Entries.Count, Is.EqualTo(3));
            for (var i = 0; i < 3; i++)
            {
                var entry = group.Entries.Single(e => e.Collider.transform.parent == bones[i]);
                var capsule = (CapsuleCollider)entry.Collider;
                Assert.That(entry.OwnsGameObject);
                Assert.That(entry.Label, Is.EqualTo(bones[i].name));
                Assert.That(capsule.radius, Is.EqualTo(0.1f).Within(0.002f));
                var axis = capsule.transform.rotation * (Quaternion.identity * AxisOf(capsule.direction));
                Assert.That(Mathf.Abs(Vector3.Dot(axis, bones[i].up)), Is.EqualTo(1f).Within(1e-3f), "Capsule runs along the bone");
            }
            Assert.That(group.Container == null, "Bone colliders need no [Colliders] container");
        }

        [Test]
        public void PerBone_CollidersFollowTheBones()
        {
            var (root, _, bones) = Limb(3, 0.1f, 1f);
            BatchProcessor.Generate(new[] { root }, new GenerationSettings { Strategy = ColliderStrategy.PerBone }, MeshFolder, false);
            var tip = root.GetComponent<ColliderGroup>().Entries.Single(e => e.Collider.transform.parent == bones[2]).Collider;

            bones[1].localRotation = Quaternion.Euler(0, 0, 90); // bend the "elbow"
            Physics.SyncTransforms();

            // The tip collider stays centred on its bone segment, wherever that segment now is.
            var segmentMid = bones[2].position + bones[2].up * 0.5f;
            Assert.That(Vector3.Distance(tip.bounds.center, segmentMid), Is.LessThan(0.05f));
        }

        [Test]
        public void Auto_OnSkinnedMesh_UsesPerBone_AndRemoveAndUndoCleanUp()
        {
            var (root, _, bones) = Limb(2, 0.1f, 1f);

            BatchProcessor.Generate(new[] { root }, new GenerationSettings(), MeshFolder, false);
            Assert.That(bones[0].childCount, Is.EqualTo(2), "Bone 0 holds its collider + bone 1");

            BatchProcessor.RemoveGenerated(new[] { root });
            Assert.That(bones[0].childCount, Is.EqualTo(1), "Collider objects under bones are removed");
            Assert.That(bones[1].childCount, Is.EqualTo(0));

            Undo.PerformUndo();
            Assert.That(bones[0].childCount, Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(bones[0].childCount, Is.EqualTo(1));
            Assert.That(root.GetComponent<ColliderGroup>() == null);
        }

        [Test]
        public void SymmetricBones_LeftAndRightGetTheLargerSize()
        {
            var left = new FittedShape { Kind = ShapeKind.Capsule, Radius = 0.2f, Height = 1f };
            var right = new FittedShape { Kind = ShapeKind.Capsule, Radius = 0.3f, Height = 0.8f };
            var spine = new FittedShape { Kind = ShapeKind.Capsule, Radius = 0.5f, Height = 2f };

            BoneFitter.Symmetrize(new List<(string, FittedShape)> { ("Arm_L", left), ("Arm_R", right), ("Spine", spine) });

            Assert.That(left.Radius, Is.EqualTo(0.3f));
            Assert.That(right.Radius, Is.EqualTo(0.3f));
            Assert.That(left.Height, Is.EqualTo(1f));
            Assert.That(right.Height, Is.EqualTo(1f));
            Assert.That(spine.Radius, Is.EqualTo(0.5f), "Unpaired bones are untouched");
        }

        [Test]
        public void SymmetricBones_AppliedThroughGeneration()
        {
            var (root, _, bones) = Limb(2, 0.1f, 1f, new[] { "Leg_L", "Leg_R" }, new[] { 0.1f, 0.15f });

            BatchProcessor.Generate(new[] { root }, new GenerationSettings { Strategy = ColliderStrategy.PerBone }, MeshFolder, false);

            var radii = root.GetComponent<ColliderGroup>().Entries.Select(e => ((CapsuleCollider)e.Collider).radius).ToList();
            Assert.That(radii[0], Is.EqualTo(radii[1]).Within(1e-5f));
            Assert.That(radii[0], Is.EqualTo(0.15f).Within(0.003f));
        }

        private static Vector3 AxisOf(int direction) => direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;

        // ---------- Painted regions ----------

        /// <summary>The closed L prism as a scene object with an (empty) Collider Group.</summary>
        private static (GameObject go, MeshRenderer renderer, ColliderGroup group, MeshGeometry geometry) PaintTarget()
        {
            var geometry = TestMeshes.LShape();
            var go = new GameObject("Bracket");
            go.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(geometry);
            var renderer = go.AddComponent<MeshRenderer>();
            var group = go.AddComponent<ColliderGroup>();
            return (go, renderer, group, geometry);
        }

        /// <summary>Triangles of the L split into three areas by centroid: long arm (x > 1), short arm (z > 1), corner.</summary>
        private static Dictionary<string, HashSet<int>> ThreeAreas(MeshGeometry l)
        {
            var areas = new Dictionary<string, HashSet<int>> { ["arm"] = new(), ["leg"] = new(), ["corner"] = new() };
            for (var t = 0; t < l.TriangleCount; t++)
            {
                var c = (l.Vertices[l.Triangles[t * 3]] + l.Vertices[l.Triangles[t * 3 + 1]] + l.Vertices[l.Triangles[t * 3 + 2]]) / 3f;
                areas[c.x > 1f ? "arm" : c.z > 1f ? "leg" : "corner"].Add(t);
            }
            return areas;
        }

        private static List<string> Snapshot(ColliderGroup group) => group.Entries
            .OrderBy(e => e.PartId)
            .Select(e => $"{e.PartId}:{e.Collider.GetType().Name}:{Round(e.Collider.bounds.center)}:{Round(e.Collider.bounds.size)}")
            .ToList();

        private static string Round(Vector3 v) => $"{v.x:F4},{v.y:F4},{v.z:F4}";

        private static void PaintRegions(MeshRenderer renderer, Dictionary<string, HashSet<int>> areas)
        {
            ColliderOperations.CreateRegions(new Dictionary<Renderer, HashSet<int>> { [renderer] = areas["arm"] }, PartShape.Box);
            ColliderOperations.CreateRegions(new Dictionary<Renderer, HashSet<int>> { [renderer] = areas["leg"] }, PartShape.Capsule);
            ColliderOperations.CreateRegions(new Dictionary<Renderer, HashSet<int>> { [renderer] = areas["corner"] }, PartShape.ConvexHull);
        }

        [Test]
        public void ThreePaintedRegions_RegenerateIdentically()
        {
            var (go, renderer, group, geometry) = PaintTarget();
            PaintRegions(renderer, ThreeAreas(geometry));

            Assert.That(group.Recipe.Regions.Count, Is.EqualTo(3));
            Assert.That(group.Entries.Select(e => e.PartId), Is.EqualTo(group.Recipe.Regions.Select(r => r.Id)), "Each collider is tagged with its region");
            var before = Snapshot(group);

            var report = BatchProcessor.Generate(new[] { go }, group.Recipe.Settings, MeshFolder, false);

            Assert.That(Snapshot(group), Is.EqualTo(before), "Regenerate rebuilds exactly the painted colliders");
            Assert.That(report.Results[0].Notes.Any(n => n.Contains("painted region")));
        }

        [Test]
        public void LockedRegionCollider_IsKept_NotDuplicated()
        {
            var (go, renderer, group, geometry) = PaintTarget();
            PaintRegions(renderer, ThreeAreas(geometry));
            var locked = group.Entries[0];
            locked.Locked = true;
            var lockedCollider = locked.Collider;

            BatchProcessor.Generate(new[] { go }, group.Recipe.Settings, MeshFolder, false);

            Assert.That(group.Entries.Count, Is.EqualTo(3));
            Assert.That(group.Entries.Count(e => e.PartId == locked.PartId), Is.EqualTo(1));
            Assert.That(group.Entries.Any(e => e.Collider == lockedCollider));
        }

        [Test]
        public void CreateAndDeleteRegion_AreSingleUndoSteps()
        {
            var (_, renderer, group, geometry) = PaintTarget();
            var areas = ThreeAreas(geometry);

            ColliderOperations.CreateRegions(new Dictionary<Renderer, HashSet<int>> { [renderer] = areas["arm"] }, PartShape.Box);
            Assert.That(group.Recipe.Regions.Count, Is.EqualTo(1));
            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Undo.PerformUndo();
            Assert.That(group.Recipe.Regions.Count, Is.EqualTo(0));
            Assert.That(group.Entries.Count, Is.EqualTo(0));

            PaintRegions(renderer, areas);
            var id = group.Recipe.Regions[1].Id;
            ColliderOperations.DeleteRegion(group, id);
            Assert.That(group.Recipe.Regions.Count, Is.EqualTo(2));
            Assert.That(group.Entries.Any(e => e.PartId == id), Is.False);
            Undo.PerformUndo();
            Assert.That(group.Recipe.Regions.Count, Is.EqualTo(3));
            Assert.That(group.Entries.Count(e => e.PartId == id && e.Collider != null), Is.EqualTo(1));
        }

        [Test]
        public void RegionSubMesh_CountsTrianglesThatNoLongerExist()
        {
            var cube = TestMeshes.Box(float3.zero, new float3(1));

            var sub = RegionFitter.SubMesh(cube, new[] { 0, 1, 99 }, out var invalid);

            Assert.That(sub.TriangleCount, Is.EqualTo(2));
            Assert.That(invalid, Is.EqualTo(1));
        }

        // ---------- Painting ----------

        private static PaintMesh GridMesh(int n = 10, float size = 2f)
        {
            var grid = TestMeshes.Grid(size, n); // XZ plane, normals +Y
            return new PaintMesh(null, null, grid.Vertices.Select(v => (Vector3)(float3)v).ToArray(), grid.Triangles);
        }

        [Test]
        public void Brush_SelectsFrontFacingTrianglesInsideTheRadius()
        {
            var mesh = GridMesh();
            var camera = new Vector3(0, 10, 0);
            var ray = new Ray(camera, Vector3.down);
            System.Func<Vector3, Vector2> project = p => new Vector2(p.x, p.z) * 100f; // 100 px per meter

            var hits = PaintMesh.Brush(new[] { mesh }, ray, camera, Vector2.zero, 50f, 1f, project);

            Assert.That(hits.Count, Is.GreaterThan(4));
            var expected = Enumerable.Range(0, mesh.TriangleCount).Count(t => project(mesh.Centroids[t]).magnitude <= 50f);
            Assert.That(hits.Count, Is.EqualTo(expected).Or.EqualTo(expected + 1), "Everything in the radius (+ the hit triangle)");

            var below = new Vector3(0, -10, 0);
            var fromBelow = PaintMesh.Brush(new[] { mesh }, new Ray(below, Vector3.up), below, Vector2.zero, 50f, 1f, project);
            Assert.That(fromBelow.Count, Is.EqualTo(1), "Back faces are skipped (only the triangle under the mouse)");
        }

        [Test]
        public void GrowAndConnected_StayOnTheTouchedPiece()
        {
            var two = TestMeshes.Merge(TestMeshes.Box(float3.zero, new float3(1)), TestMeshes.Box(new float3(3, 0, 0), new float3(4, 1, 1)));
            var mesh = new PaintMesh(null, null, two.Vertices.Select(v => (Vector3)(float3)v).ToArray(), two.Triangles);

            var connected = mesh.Connected(new[] { 0 });
            var grown = mesh.Grow(new[] { 0 });

            Assert.That(connected.Count, Is.EqualTo(12), "The whole first box, not the second");
            Assert.That(grown.Count, Is.GreaterThan(1));
            Assert.That(grown.IsSubsetOf(connected));
        }

        [Test]
        public void PaintState_PaintEraseAndClear()
        {
            var (_, renderer, _, _) = PaintTarget();

            ColliderEditState.Paint(renderer, new[] { 1, 2, 3 }, erase: false);
            ColliderEditState.Paint(renderer, new[] { 2 }, erase: true);
            Assert.That(ColliderEditState.PaintedCount, Is.EqualTo(2));

            ColliderEditState.ClearPaint();
            Assert.That(ColliderEditState.PaintedCount, Is.EqualTo(0));
        }
    }
}
