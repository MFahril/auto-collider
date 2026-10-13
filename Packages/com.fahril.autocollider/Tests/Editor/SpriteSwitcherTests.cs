using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M10: Sprite Collider Switcher — baking per-sprite polygons from Animator clips and applying them.</summary>
    public class SpriteSwitcherTests
    {
        private TestScene _scene;
        private TestSprites _sprites;
        private Object[] _assets = new Object[0];

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _sprites = new TestSprites();
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Cleanup();
            _sprites.DestroyAll();
            foreach (var asset in _assets)
                if (asset != null) Object.DestroyImmediate(asset);
        }

        private static GenerationSettings Settings => new() { Quality = 1f };

        private static Rect PathBounds(PolygonCollider2D polygon)
        {
            var points = Enumerable.Range(0, polygon.pathCount).SelectMany(polygon.GetPath).ToArray();
            var min = new Vector2(points.Min(p => p.x), points.Min(p => p.y));
            var max = new Vector2(points.Max(p => p.x), points.Max(p => p.y));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>An in-memory Animator Controller whose one clip flips the renderer through <paramref name="frames"/>.</summary>
        private RuntimeAnimatorController Controller(params Sprite[] frames)
        {
            var clip = new AnimationClip { name = "Run" };
            var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
            var keys = frames.Select((s, i) => new ObjectReferenceKeyframe { time = i / 12f, value = s }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            var controller = new AnimatorController { name = "Runner" };
            controller.AddLayer("Base");
            controller.AddMotion(clip);
            _assets = new Object[] { clip, controller };
            return controller;
        }

        [Test]
        public void Bake_CollectsCurrentAnimatedAndExtraSprites()
        {
            var square = _sprites.Square();
            var plank = _sprites.Rectangle(2f, 0.5f);
            var l = _sprites.L();
            var extra = _sprites.Disc();
            var go = TestSprites.SpriteObject("Runner", square, Vector3.zero);
            go.AddComponent<Animator>().runtimeAnimatorController = Controller(plank, l, plank);
            var switcher = go.AddComponent<SpriteColliderSwitcher>();
            switcher.ExtraSprites.Add(extra);

            Assert.That(SpriteColliderBaker.CollectSprites(switcher), Is.EqualTo(new[] { square, plank, l, extra }));

            Undo.IncrementCurrentGroup();
            Assert.That(SpriteColliderBaker.Bake(switcher, Settings), Is.EqualTo(4));
            Assert.That(switcher.ShapeCount, Is.EqualTo(4));
            Assert.That(SpriteColliderBaker.CountUnbaked(switcher), Is.EqualTo(0));

            var polygon = go.GetComponent<PolygonCollider2D>();
            Assert.That(switcher.Collider, Is.EqualTo(polygon));
            var bounds = PathBounds(polygon);
            Assert.That(bounds.width, Is.EqualTo(1f).Within(0.02f), "the current sprite (square) is applied");
            Assert.That(bounds.height, Is.EqualTo(1f).Within(0.02f));

            Undo.PerformUndo();
            Assert.That(switcher.ShapeCount, Is.EqualTo(0));
            Assert.That(go.GetComponent<PolygonCollider2D>() == null);
        }

        [Test]
        public void Apply_SwapsPathsWithTheSprite_AndMirrorsFlips()
        {
            var square = _sprites.Square();
            var l = _sprites.L();
            var go = TestSprites.SpriteObject("Runner", square, Vector3.zero);
            var switcher = go.AddComponent<SpriteColliderSwitcher>();
            switcher.ExtraSprites.Add(l);
            SpriteColliderBaker.Bake(switcher, Settings);
            var polygon = switcher.Collider;

            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = l;
            Assert.That(switcher.Apply());
            Assert.That(PathBounds(polygon).width, Is.EqualTo(2f).Within(0.03f));
            Physics2D.SyncTransforms();
            // The L's missing quarter is top-right (x > 0, y > 0): no point lies deep inside it.
            Assert.That(polygon.OverlapPoint(new Vector2(0.5f, 0.5f)), Is.False);

            renderer.flipX = true;
            switcher.Apply();
            Physics2D.SyncTransforms();
            Assert.That(polygon.OverlapPoint(new Vector2(0.5f, 0.5f)), Is.True, "flipped: the gap moves to the left");
            Assert.That(polygon.OverlapPoint(new Vector2(-0.5f, 0.5f)), Is.False);

            renderer.sprite = _sprites.Disc();
            Assert.That(switcher.Apply(), Is.False, "unknown sprite keeps the last shape");
            Assert.That(PathBounds(polygon).width, Is.EqualTo(2f).Within(0.03f));
        }

        [Test]
        public void AddTo_AddsAndBakesOnSpriteObjectsOnly()
        {
            var sprite = TestSprites.SpriteObject("Crate", _sprites.Square(), Vector3.zero);
            var plain = new GameObject("Plain");
            Undo.IncrementCurrentGroup();

            Assert.That(SpriteColliderBaker.AddTo(new[] { sprite, plain }, Settings), Is.EqualTo(1));
            Assert.That(sprite.GetComponent<SpriteColliderSwitcher>().ShapeCount, Is.EqualTo(1));
            Assert.That(plain.GetComponent<SpriteColliderSwitcher>() == null);

            Undo.PerformUndo();
            Assert.That(sprite.GetComponent<SpriteColliderSwitcher>() == null);
            Assert.That(sprite.GetComponent<PolygonCollider2D>() == null);
        }
    }
}
