using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M10: per-bone 2D colliders for skinned sprites (2D Animation SpriteSkin, read by reflection).</summary>
    public class Skeleton2DTests
    {
        private const string MeshFolder = "Assets/__AutoColliderSkeletonTests";
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

        [Test]
        public void BoneFitter2D_FitsEachBoneInItsOwnSpace()
        {
            var vertices = new[]
            {
                new float2(-1f, -0.25f), new float2(-1f, 0.25f), new float2(0f, -0.25f), new float2(0f, 0.25f),
                new float2(0f, -0.25f), new float2(0f, 0.25f), new float2(1f, -0.25f), new float2(1f, 0.25f),
            };
            var triangles = new[] { 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6 };
            var boneOfVertex = new[] { 0, 0, 0, 0, 1, 1, 1, 1 };
            var bindposes = new[] { float4x4.Translate(new float3(1f, 0f, 0f)), float4x4.identity };

            var fits = BoneFitter2D.Fit(vertices, triangles, boneOfVertex, bindposes, PartShape.Box, new GenerationSettings());

            Assert.That(fits.Select(f => f.Bone), Is.EqualTo(new[] { 0, 1 }));
            foreach (var fit in fits)
            {
                Assert.That(fit.Shape.Kind, Is.EqualTo(ShapeKind2D.Box));
                Assert.That(fit.Shape.Center.x, Is.EqualTo(0.5f).Within(0.02f), "bone-local: each half spans x 0..1");
                Assert.That(fit.Shape.Center.y, Is.EqualTo(0f).Within(0.02f));
                Assert.That(fit.Shape.Size.x, Is.EqualTo(1f).Within(0.03f));
                Assert.That(fit.Shape.Size.y, Is.EqualTo(0.5f).Within(0.03f));
            }
        }

        [Test]
        public void TriangleBone_TakesTheMajority()
        {
            Assert.That(BoneFitter2D.TriangleBone(2, 1, 1), Is.EqualTo(1));
            Assert.That(BoneFitter2D.TriangleBone(0, 3, 0), Is.EqualTo(0));
            Assert.That(BoneFitter2D.TriangleBone(-1, 4, 5), Is.EqualTo(4));
            Assert.That(BoneFitter2D.TriangleBone(-1, -1, -1), Is.EqualTo(-1));
        }

        [Test]
        public void SymmetricBones_GetTheLargerSize()
        {
            var left = new FittedShape2D { Kind = ShapeKind2D.Capsule, Radius = 0.2f, Height = 1f };
            var right = new FittedShape2D { Kind = ShapeKind2D.Capsule, Radius = 0.3f, Height = 0.8f };

            BoneFitter2D.Symmetrize(new[] { ("Arm_L", left), ("Arm_R", right) });

            Assert.That(left.Radius, Is.EqualTo(0.3f));
            Assert.That(right.Height, Is.EqualTo(1f));
        }

        [Test]
        public void SkinnedSprite_GetsCollidersOnItsBones_ThatFollowThem()
        {
            Assume.That(SpriteSkinAccess.IsAvailable, "2D Animation package not installed");

            var go = TestSprites.SpriteObject("Arm", _sprites.SkinnedArm(), Vector3.zero);
            var upper = new GameObject("Upper").transform;
            upper.SetParent(go.transform, false);
            upper.localPosition = new Vector3(-1f, 0f, 0f);
            var lower = new GameObject("Lower").transform;
            lower.SetParent(upper, false);
            lower.localPosition = new Vector3(1f, 0f, 0f);
            var skin = go.AddComponent(SpriteSkinAccess.SkinType);
            SpriteSkinAccess.SkinType.GetMethod("SetBoneTransforms").Invoke(skin, new object[] { new[] { upper, lower } });
            Undo.IncrementCurrentGroup();

            BatchProcessor.Generate(new[] { go }, new GenerationSettings(), MeshFolder, showProgress: false);

            var entries = go.GetComponent<ColliderGroup>().Entries;
            Assert.That(entries.Count, Is.EqualTo(2));
            foreach (var bone in new[] { upper, lower })
            {
                var entry = entries.Single(e => e.Label == bone.name);
                var capsule = (CapsuleCollider2D)entry.Collider2D;
                Assert.That(capsule.transform.parent, Is.EqualTo(bone));
                Assert.That(entry.OwnsGameObject);
                Assert.That(capsule.direction, Is.EqualTo(CapsuleDirection2D.Horizontal));
                Assert.That(capsule.size.y, Is.EqualTo(0.5f).Within(0.05f));
                Assert.That(capsule.transform.localPosition.x, Is.EqualTo(0.5f).Within(0.05f));
            }

            // Bending the elbow moves the forearm collider with it.
            lower.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Physics2D.SyncTransforms();
            var forearm = entries.Single(e => e.Label == "Lower").Collider2D;
            Assert.That(Vector2.Distance(forearm.bounds.center, new Vector2(0f, 0.5f)), Is.LessThan(0.05f));

            Undo.PerformUndo();
            Assert.That(go.GetComponent<ColliderGroup>() == null);
            Assert.That(upper.childCount, Is.EqualTo(1), "only the Lower bone remains under Upper");
            Assert.That(lower.childCount, Is.EqualTo(0));
        }

        [Test]
        public void SpriteWithoutSkin_FallsBackToItsOutline()
        {
            var go = TestSprites.SpriteObject("Arm", _sprites.SkinnedArm(), Vector3.zero);

            BatchProcessor.Generate(new[] { go }, new GenerationSettings { Strategy2D = ColliderStrategy2D.PerBone }, MeshFolder, showProgress: false);

            var entry = go.GetComponent<ColliderGroup>().Entries.Single();
            Assert.That(entry.Collider2D.transform.parent, Is.EqualTo(go.transform), "on the 2D container");
        }
    }
}
