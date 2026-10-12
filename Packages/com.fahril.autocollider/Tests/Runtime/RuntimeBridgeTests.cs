using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AutoCollider.Tests
{
    /// <summary>M6 play-mode checks: group events report the right part, animated colliders throttle, LOD following.</summary>
    public class RuntimeBridgeTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.Destroy(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private static ColliderEntry Register(ColliderGroup group, Collider collider, string label)
        {
            var entry = new ColliderEntry(collider, label, group.Entries.Count, generated: true);
            group.AddEntry(entry);
            return entry;
        }

        /// <summary>Static group: two boxes on one shared container object (no Rigidbody anywhere on the group).</summary>
        private (ColliderGroup group, BoxCollider left, BoxCollider right) Shelf(bool triggers)
        {
            var root = Track(new GameObject("Shelf"));
            var group = root.AddComponent<ColliderGroup>();
            var container = new GameObject("[Colliders]");
            container.transform.SetParent(root.transform, false);
            var left = container.AddComponent<BoxCollider>();
            left.center = new Vector3(-2f, 0f, 0f);
            left.size = new Vector3(2f, 1f, 2f);
            left.isTrigger = triggers;
            var right = container.AddComponent<BoxCollider>();
            right.center = new Vector3(2f, 0f, 0f);
            right.size = new Vector3(2f, 1f, 2f);
            right.isTrigger = triggers;
            Register(group, left, "Left");
            Register(group, right, "Right");
            return (group, left, right);
        }

        private static IEnumerator FixedSteps(int count)
        {
            for (var i = 0; i < count; i++) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Collision_OnSharedContainerWithoutRigidbody_ReportsThePartThatWasHit()
        {
            var (group, _, _) = Shelf(triggers: false);
            var enters = new List<string>();
            var exits = new List<string>();
            group.CollisionEnter += (entry, _) => enters.Add(entry.Label);
            group.CollisionExit += (entry, _) => exits.Add(entry.Label);

            var ball = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            ball.transform.position = new Vector3(2f, 1.5f, 0f);
            var body = ball.AddComponent<Rigidbody>();
            for (var i = 0; i < 200 && enters.Count == 0; i++) yield return new WaitForFixedUpdate();

            Assert.That(enters, Is.Not.Empty, "The falling ball hits the shelf");
            Assert.That(enters, Is.All.EqualTo("Right"));
            Assert.That(group.GetComponentInChildren<ColliderRelay>(), Is.Not.Null, "A relay was added on first subscription");

            body.position = new Vector3(0f, 20f, 0f);
            body.linearVelocity = Vector3.zero;
            yield return FixedSteps(4);
            Assert.That(exits, Does.Contain("Right"));
            Assert.That(exits, Has.None.EqualTo("Left"));
        }

        [UnityTest]
        public IEnumerator Collision_OnRigidbodyCompound_ReportsEachPartOncePerContact()
        {
            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);

            var root = Track(new GameObject("Robot"));
            root.transform.position = new Vector3(0f, 2f, 0f);
            root.AddComponent<Rigidbody>();
            var group = root.AddComponent<ColliderGroup>();
            Collider Foot(string name, Vector3 local)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = local;
                var box = go.AddComponent<BoxCollider>();
                box.size = Vector3.one * 0.3f;
                Register(group, box, name);
                return box;
            }
            Foot("FootL", new Vector3(-0.5f, -0.6f, 0f));
            Foot("FootR", new Vector3(0.5f, 0f, 0f));

            var events = new List<(string label, int step)>();
            var step = 0;
            group.CollisionEnter += (entry, collision) =>
            {
                Assert.That(collision.thisCollider, Is.EqualTo(entry.Collider));
                events.Add((entry.Label, step));
            };
            for (var i = 0; i < 200 && events.Count == 0; i++, step++) yield return new WaitForFixedUpdate();

            Assert.That(events, Is.Not.Empty);
            Assert.That(events[0].label, Is.EqualTo("FootL"), "The lower foot touches first");
            Assert.That(events.Distinct().Count(), Is.EqualTo(events.Count), "No contact is reported twice in one step");
        }

        [UnityTest]
        public IEnumerator Trigger_OnSharedContainer_ResolvesThePartByOverlap()
        {
            var (group, _, _) = Shelf(triggers: true);
            var enters = new List<string>();
            var exits = new List<string>();
            Collider lastOther = null;
            group.TriggerEnter += (entry, other) =>
            {
                enters.Add(entry.Label);
                lastOther = other;
            };
            group.TriggerExit += (entry, _) => exits.Add(entry.Label);

            var probe = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            probe.transform.localScale = Vector3.one * 0.5f;
            probe.transform.position = new Vector3(10f, 0f, 0f);
            var body = probe.AddComponent<Rigidbody>();
            body.isKinematic = true;
            yield return FixedSteps(2);

            body.position = new Vector3(2f, 0f, 0f);
            yield return FixedSteps(3);
            Assert.That(enters, Is.EqualTo(new[] { "Right" }));
            Assert.That(lastOther, Is.EqualTo(probe.GetComponent<Collider>()));

            body.position = new Vector3(-2f, 0f, 0f);
            yield return FixedSteps(3);
            Assert.That(enters, Is.EqualTo(new[] { "Right", "Left" }));
            Assert.That(exits, Is.EqualTo(new[] { "Right" }));

            body.position = new Vector3(10f, 0f, 0f);
            yield return FixedSteps(3);
            Assert.That(exits, Is.EqualTo(new[] { "Right", "Left" }));
        }

        [UnityTest]
        public IEnumerator Stay_IsOnlyRoutedWhenSubscribed()
        {
            var (group, _, _) = Shelf(triggers: true);
            group.TriggerEnter += (_, _) => { };
            Assert.That(group.GetComponentInChildren<ColliderStayRelay>(), Is.Null);

            var stays = 0;
            group.TriggerStay += (entry, _) => stays++;
            Assert.That(group.GetComponentInChildren<ColliderStayRelay>(), Is.Not.Null);

            var probe = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            probe.transform.localScale = Vector3.one * 0.5f;
            probe.transform.position = new Vector3(2f, 0f, 0f);
            probe.AddComponent<Rigidbody>().isKinematic = true;
            yield return FixedSteps(5);
            Assert.That(stays, Is.GreaterThanOrEqualTo(2));
        }

        // ---------- Animated mesh collider ----------

        private AnimatedMeshCollider AnimatedCube()
        {
            var go = Track(new GameObject("Animated"));
            var bone = new GameObject("Bone").transform;
            bone.SetParent(go.transform, false);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(cube);
            Track(mesh);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, mesh.vertexCount).ToArray();
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };

            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            var animated = go.AddComponent<AnimatedMeshCollider>();
            animated.Source = skin;
            animated.Target = collider;
            animated.OnlyWhenVisible = false;
            return animated;
        }

        [UnityTest]
        public IEnumerator AnimatedCollider_TargetHz_IsThrottled()
        {
            var animated = AnimatedCube();
            animated.UpdateMode = ColliderUpdateMode.TargetHz;
            animated.TargetHz = 10f;
            yield return null;
            var start = animated.RefreshCount;
            var startTime = Time.time;

            yield return new WaitForSeconds(1f);

            var expected = (Time.time - startTime) * 10f;
            Assert.That(animated.RefreshCount - start, Is.InRange(Mathf.FloorToInt(expected) - 2, Mathf.CeilToInt(expected) + 1));
        }

        [UnityTest]
        public IEnumerator AnimatedCollider_EveryNFrames_IsThrottled_AndFollowsTheBone()
        {
            var animated = AnimatedCube();
            animated.UpdateMode = ColliderUpdateMode.EveryNFrames;
            animated.EveryNFrames = 5;
            yield return null;
            var start = animated.RefreshCount;

            for (var i = 0; i < 30; i++) yield return null;
            Assert.That(animated.RefreshCount - start, Is.InRange(5, 7));

            var bone = animated.Source.bones[0];
            bone.localPosition = new Vector3(0f, 3f, 0f);
            for (var i = 0; i < 6; i++) yield return null;
            Physics.SyncTransforms();
            Assert.That(animated.Target.bounds.center.y, Is.EqualTo(3f).Within(0.05f), "Collider follows the skinned pose");

            var collider = animated.Target;
            var original = animated.Source.sharedMesh;
            Object.Destroy(animated);
            yield return null;
            Assert.That(collider.sharedMesh, Is.EqualTo(original), "Destroying the component puts the original mesh back");
        }

        [UnityTest]
        public IEnumerator AnimatedCollider_OnlyWhenVisible_SkipsInvisibleRenderers()
        {
            var animated = AnimatedCube();
            animated.UpdateMode = ColliderUpdateMode.EveryFrame;
            animated.OnlyWhenVisible = true;
            animated.Source.enabled = false; // a disabled renderer is never visible
            var start = animated.RefreshCount;

            for (var i = 0; i < 5; i++) yield return null;
            Assert.That(animated.RefreshCount, Is.EqualTo(start));

            animated.OnlyWhenVisible = false;
            for (var i = 0; i < 3; i++) yield return null;
            Assert.That(animated.RefreshCount, Is.GreaterThan(start));
        }

        // ---------- LOD follower ----------

        [UnityTest]
        public IEnumerator LodFollower_EnablesTheActiveLevelsColliders()
        {
            var root = Track(new GameObject("Rock"));
            var lodGroup = root.AddComponent<LODGroup>();
            var group = root.AddComponent<ColliderGroup>();
            Renderer Level(string name)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root.transform, false);
                return go.GetComponent<Renderer>();
            }
            lodGroup.SetLODs(new[] { new LOD(0.5f, new[] { Level("LOD0") }), new LOD(0.01f, new[] { Level("LOD1") }) });
            lodGroup.RecalculateBounds();

            var detailed = root.AddComponent<BoxCollider>();
            var simple = root.AddComponent<SphereCollider>();
            var always = root.AddComponent<CapsuleCollider>();
            Register(group, detailed, "Detailed").Lod = 0;
            Register(group, simple, "Simple").Lod = 1;
            Register(group, always, "Always");

            var camera = Track(new GameObject("Camera")).AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -2f);
            var follower = root.AddComponent<ColliderLodFollower>();
            follower.LodGroup = lodGroup;
            follower.Camera = camera;
            yield return null;
            yield return null;

            Assert.That(follower.ActiveLevel, Is.EqualTo(0));
            Assert.That(detailed.enabled && !simple.enabled && always.enabled);

            camera.transform.position = new Vector3(0f, 0f, -60f);
            yield return null;
            yield return null;
            Assert.That(follower.ActiveLevel, Is.EqualTo(1));
            Assert.That(!detailed.enabled && simple.enabled && always.enabled);

            camera.transform.position = new Vector3(0f, 0f, -100000f);
            yield return null;
            yield return null;
            Assert.That(follower.ActiveLevel, Is.EqualTo(1), "Culled keeps the lowest-detail colliders");
        }
    }
}
