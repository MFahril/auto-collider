using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M6 edit-mode checks: refresh throttling, LOD math, per-LOD generation, group lookup and bulk API, animated collider setup.</summary>
    public class RuntimeBridgeEditTests
    {
        private const string MeshFolder = "Assets/__AutoColliderBridgeTests";
        private HashSet<GameObject> _rootsBefore;

        [SetUp]
        public void SetUp() => _rootsBefore = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());

        [TearDown]
        public void TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_rootsBefore.Contains(root)) Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(MeshFolder);
        }

        // ---------- Throttling ----------

        private static int CountRefreshes(ColliderUpdateMode mode, int n, float hz, int frames, float fps)
        {
            int lastFrame = -1, count = 0;
            var lastTime = 0f;
            for (var frame = 0; frame < frames; frame++)
            {
                var time = frame / fps;
                if (!RefreshSchedule.IsDue(mode, n, hz, frame, time, lastFrame, lastTime)) continue;
                lastFrame = frame;
                lastTime = time;
                count++;
            }
            return count;
        }

        [Test]
        public void RefreshSchedule_RespectsEachMode()
        {
            Assert.That(CountRefreshes(ColliderUpdateMode.EveryFrame, 1, 0, 60, 60), Is.EqualTo(60));
            Assert.That(CountRefreshes(ColliderUpdateMode.EveryNFrames, 3, 0, 60, 60), Is.EqualTo(20));
            Assert.That(CountRefreshes(ColliderUpdateMode.TargetHz, 1, 10, 120, 60), Is.EqualTo(20), "10 Hz for 2 s at 60 fps");
            Assert.That(CountRefreshes(ColliderUpdateMode.TargetHz, 1, 30, 60, 60), Is.EqualTo(30), "30 Hz at 60 fps = every other frame");
            Assert.That(CountRefreshes(ColliderUpdateMode.TargetHz, 1, 100, 60, 60), Is.EqualTo(60), "Never more than once per frame");
            Assert.That(RefreshSchedule.IsDue(ColliderUpdateMode.TargetHz, 1, 1, 5, 0f, -1, 0f), "Never refreshed → due");
        }

        // ---------- LOD ----------

        /// <summary>LOD Group with a box at LOD 0 and an octahedron at LOD 1 (plus an extra LOD 0 piece when asked).</summary>
        private static (GameObject root, LODGroup lodGroup) LodObject(bool twoPiecesAtLod0 = false)
        {
            var root = new GameObject("Statue");
            var lodGroup = root.AddComponent<LODGroup>();

            Renderer Part(string name, MeshGeometry geometry, Vector3 offset)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = offset;
                go.AddComponent<MeshFilter>().sharedMesh = TestMeshes.ToMesh(geometry);
                return go.AddComponent<MeshRenderer>();
            }

            var lod0 = new List<Renderer> { Part("Detailed", TestMeshes.Box(new float3(-1, 0, -1), new float3(1, 2, 1)), Vector3.zero) };
            if (twoPiecesAtLod0) lod0.Add(Part("Detailed Base", TestMeshes.Box(new float3(-2, -1, -2), new float3(2, 0, 2)), Vector3.zero));
            var lod1 = Part("Simple", TestMeshes.Octahedron(1.2f), new Vector3(0, 1, 0));
            lodGroup.SetLODs(new[] { new LOD(0.4f, lod0.ToArray()), new LOD(0.02f, new[] { lod1 }) });
            lodGroup.RecalculateBounds();
            return (root, lodGroup);
        }

        [Test]
        public void LodMath_PicksTheLevelForTheCameraDistance()
        {
            var (_, lodGroup) = LodObject();
            var camera = new GameObject("LOD Camera").AddComponent<Camera>();
            var bias = QualitySettings.lodBias;
            try
            {
                QualitySettings.lodBias = 1f;
                camera.transform.position = new Vector3(0, 1, -3);
                Assert.That(LodMath.ActiveLod(lodGroup, camera), Is.EqualTo(0));
                camera.transform.position = new Vector3(0, 1, -40);
                Assert.That(LodMath.ActiveLod(lodGroup, camera), Is.EqualTo(1));
                camera.transform.position = new Vector3(0, 1, -10000);
                Assert.That(LodMath.ActiveLod(lodGroup, camera), Is.EqualTo(2), "Culled = lodCount");

                QualitySettings.lodBias = 100f;
                camera.transform.position = new Vector3(0, 1, -40);
                Assert.That(LodMath.ActiveLod(lodGroup, camera), Is.EqualTo(0), "LOD bias stretches the distances");
            }
            finally
            {
                QualitySettings.lodBias = bias;
            }
        }

        [Test]
        public void PerLodColliders_TagsEntries_StartsAtLod0_AddsFollower_OneUndo()
        {
            var (root, lodGroup) = LodObject();
            var settings = new GenerationSettings { Strategy = ColliderStrategy.Box, PerLodColliders = true };

            BatchProcessor.Generate(new[] { root }, settings, MeshFolder, false);

            var group = root.GetComponent<ColliderGroup>();
            Assert.That(group.Entries.Select(e => e.Lod), Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(group.Entries.Single(e => e.Lod == 0).Collider.enabled, Is.True);
            Assert.That(group.Entries.Single(e => e.Lod == 1).Collider.enabled, Is.False, "Only LOD 0's colliders are on until the follower runs");
            var follower = root.GetComponent<ColliderLodFollower>();
            Assert.That(follower, Is.Not.Null);
            Assert.That(follower.LodGroup, Is.EqualTo(lodGroup));

            follower.Apply(1);
            Assert.That(group.Entries.Single(e => e.Lod == 1).Collider.enabled, Is.True);
            Assert.That(group.Entries.Single(e => e.Lod == 0).Collider.enabled, Is.False);

            Undo.PerformUndo();
            Assert.That(root.GetComponent<ColliderGroup>() == null);
            Assert.That(root.GetComponent<ColliderLodFollower>() == null);
        }

        [Test]
        public void PerLodColliders_Off_UsesLod0Only()
        {
            var (root, _) = LodObject();

            BatchProcessor.Generate(new[] { root }, new GenerationSettings { Strategy = ColliderStrategy.Box }, MeshFolder, false);

            var group = root.GetComponent<ColliderGroup>();
            Assert.That(group.Entries.Count, Is.EqualTo(1));
            Assert.That(group.Entries[0].Lod, Is.EqualTo(-1));
            Assert.That(root.GetComponent<ColliderLodFollower>() == null);
        }

        [Test]
        public void PerLodColliders_Combined_OnePartPerLevel()
        {
            var (root, _) = LodObject(twoPiecesAtLod0: true);
            var settings = new GenerationSettings
            {
                Strategy = ColliderStrategy.Box, PerLodColliders = true, Granularity = MeshGranularity.Combined, AllowRotation = false,
            };

            BatchProcessor.Generate(new[] { root }, settings, MeshFolder, false);

            var entries = root.GetComponent<ColliderGroup>().Entries;
            Assert.That(entries.Count, Is.EqualTo(2));
            var lod0 = (BoxCollider)entries.Single(e => e.Lod == 0).Collider;
            Assert.That(lod0.size.y, Is.EqualTo(3f).Within(1e-3f), "LOD 0's two pieces were combined into one box");
        }

        [Test]
        public void LodFollower_LevelWithoutColliders_UsesTheClosestMoreDetailedLevel()
        {
            var go = new GameObject("Follower");
            var group = go.AddComponent<ColliderGroup>();
            var a = go.AddComponent<BoxCollider>();
            var b = go.AddComponent<SphereCollider>();
            group.AddEntry(new ColliderEntry(a, "A", 0, true) { Lod = 0 });
            group.AddEntry(new ColliderEntry(b, "B", 1, true) { Lod = 2 });
            var follower = go.AddComponent<ColliderLodFollower>();

            follower.Apply(1);
            Assert.That(follower.ActiveLevel, Is.EqualTo(0));
            follower.Apply(3);
            Assert.That(follower.ActiveLevel, Is.EqualTo(2));
            Assert.That(!a.enabled && b.enabled);
        }

        // ---------- Group ----------

        [Test]
        public void Entry_LodDefaultsToEveryLod()
        {
            Assert.That(new ColliderEntry(null, "x", 0, true).Lod, Is.EqualTo(-1));
            Assert.That(new ColliderEntry(null, "x", 0, true) { Lod = 3 }.Lod, Is.EqualTo(3));
        }

        [Test]
        public void TryGetEntry_FollowsRepointedAndUndoneEntries()
        {
            var go = new GameObject("Group");
            var group = go.AddComponent<ColliderGroup>();
            var first = go.AddComponent<BoxCollider>();
            var entry = new ColliderEntry(first, "Part", 0, true);
            group.AddEntry(entry);
            Assert.That(group.TryGetEntry(first, out var found) && found == entry);

            var second = go.AddComponent<SphereCollider>();
            entry.Collider = second;
            Assert.That(group.TryGetEntry(second, out found) && found == entry, "Re-pointed entry is found");
            Assert.That(group.TryGetEntry(first, out _), Is.False);

            Undo.RecordObject(group, "remove");
            group.RemoveEntry(entry);
            Assert.That(group.TryGetEntry(second, out _), Is.False);
            Undo.PerformUndo();
            Assert.That(group.TryGetEntry(second, out found) && found.Label == "Part", "Undo restores a findable entry");
        }

        [Test]
        public void BulkApi_LayerOverridesAndContacts()
        {
            var go = new GameObject("Group");
            var group = go.AddComponent<ColliderGroup>();
            var box = go.AddComponent<BoxCollider>();
            var sphere = go.AddComponent<SphereCollider>();
            group.AddEntry(new ColliderEntry(box, "A", 0, true));
            group.AddEntry(new ColliderEntry(sphere, "B", 1, true));

            group.SetLayerOverrides(1 << 3, 1 << 4, 7);
            group.SetProvidesContacts(true);

            foreach (var c in new Collider[] { box, sphere })
            {
                Assert.That(c.includeLayers.value, Is.EqualTo(1 << 3));
                Assert.That(c.excludeLayers.value, Is.EqualTo(1 << 4));
                Assert.That(c.layerOverridePriority, Is.EqualTo(7));
                Assert.That(c.providesContacts, Is.True);
            }
        }

        // ---------- Animated mesh collider ----------

        private static SkinnedMeshRenderer SkinnedBox(bool dynamic)
        {
            var go = new GameObject("Skinned");
            if (dynamic) go.AddComponent<Rigidbody>();
            var bone = new GameObject("Bone").transform;
            bone.SetParent(go.transform, false);
            var mesh = TestMeshes.ToMesh(TestMeshes.Box(new float3(-0.5f), new float3(0.5f)));
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, mesh.vertexCount).ToArray();
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            return skin;
        }

        [Test]
        public void AddAnimatedColliders_SetsUpEachSkinnedObject_InOneUndo()
        {
            var still = SkinnedBox(dynamic: false);
            var moving = SkinnedBox(dynamic: true);
            var plain = new GameObject("Plain");

            var added = GenerateCommands.AddAnimatedColliders(new[] { still.gameObject, moving.gameObject, plain });

            Assert.That(added, Is.EqualTo(2));
            var animated = still.GetComponent<AnimatedMeshCollider>();
            Assert.That(animated.Source, Is.EqualTo(still));
            Assert.That(animated.Target, Is.EqualTo(still.GetComponent<MeshCollider>()));
            Assert.That(still.GetComponent<MeshCollider>().convex, Is.False);
            Assert.That(moving.GetComponent<MeshCollider>().convex, Is.True, "Moving Rigidbodies need convex");
            Assert.That(plain.GetComponents<Component>().Length, Is.EqualTo(1));

            Undo.PerformUndo();
            Assert.That(still.GetComponent<AnimatedMeshCollider>() == null && still.GetComponent<MeshCollider>() == null);
            Assert.That(moving.GetComponent<AnimatedMeshCollider>() == null);
        }

        [Test]
        public void AnimatedCollider_Refresh_BakesThePose()
        {
            var skin = SkinnedBox(dynamic: false);
            var collider = skin.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = skin.sharedMesh;
            var animated = skin.gameObject.AddComponent<AnimatedMeshCollider>();
            animated.Source = skin;
            animated.Target = collider;

            skin.bones[0].localPosition = new Vector3(0, 2, 0);
            animated.Refresh();

            var baked = collider.sharedMesh;
            Assert.That(baked, Is.Not.EqualTo(skin.sharedMesh));
            Assert.That(baked.bounds.center.y, Is.EqualTo(2f).Within(1e-3f), "Baked in the current pose");
            Assert.That(animated.RefreshCount, Is.EqualTo(1));

            // OnDestroy (restore + free) only runs in play mode — see the play-mode test; clean up here by hand.
            collider.sharedMesh = skin.sharedMesh;
            Object.DestroyImmediate(baked);
        }
    }
}
