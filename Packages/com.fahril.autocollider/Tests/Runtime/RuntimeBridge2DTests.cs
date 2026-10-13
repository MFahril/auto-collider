using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AutoCollider.Tests
{
    /// <summary>M10 play-mode checks: 2D group events report the right part; the Sprite Collider Switcher follows the sprite.</summary>
    public class RuntimeBridge2DTests
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

        private static void Register(ColliderGroup group, Collider2D collider, string label) =>
            group.AddEntry(new ColliderEntry(collider, label, group.Entries.Count, generated: true));

        /// <summary>Static group: two boxes on one shared 2D container (no Rigidbody2D anywhere on the group).</summary>
        private ColliderGroup Shelf(bool triggers)
        {
            var root = Track(new GameObject("Shelf"));
            var group = root.AddComponent<ColliderGroup>();
            var container = new GameObject("[Colliders 2D]");
            container.transform.SetParent(root.transform, false);
            foreach (var (label, x) in new[] { ("Left", -2f), ("Right", 2f) })
            {
                var box = container.AddComponent<BoxCollider2D>();
                box.offset = new Vector2(x, 0f);
                box.size = new Vector2(2f, 1f);
                box.isTrigger = triggers;
                Register(group, box, label);
            }
            return group;
        }

        private Rigidbody2D Ball(Vector2 position, float gravity = 1f)
        {
            var ball = Track(new GameObject("Ball"));
            ball.transform.position = position;
            ball.AddComponent<CircleCollider2D>().radius = 0.25f;
            var body = ball.AddComponent<Rigidbody2D>();
            body.gravityScale = gravity;
            return body;
        }

        private static IEnumerator FixedSteps(int count)
        {
            for (var i = 0; i < count; i++) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Collision2D_OnSharedContainerWithoutRigidbody_ReportsThePartThatWasHit()
        {
            var group = Shelf(triggers: false);
            var enters = new List<string>();
            var exits = new List<string>();
            group.CollisionEnter2D += (entry, collision) =>
            {
                Assert.That(collision.otherCollider, Is.EqualTo(entry.Collider2D), "otherCollider is the group's side");
                enters.Add(entry.Label);
            };
            group.CollisionExit2D += (entry, _) => exits.Add(entry.Label);

            var body = Ball(new Vector2(2f, 1.5f));
            for (var i = 0; i < 200 && enters.Count == 0; i++) yield return new WaitForFixedUpdate();

            Assert.That(enters, Is.EqualTo(new[] { "Right" }));
            Assert.That(group.GetComponentInChildren<ColliderRelay2D>(), Is.Not.Null, "A 2D relay was added on first subscription");

            body.position = new Vector2(0f, 20f);
            body.linearVelocity = Vector2.zero;
            yield return FixedSteps(4);
            Assert.That(exits, Is.EqualTo(new[] { "Right" }));
        }

        [UnityTest]
        public IEnumerator Collision2D_OnRigidbody2DCompound_ReportsEachPartOncePerContact()
        {
            var ground = Track(new GameObject("Ground"));
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f);

            var root = Track(new GameObject("Robot"));
            root.transform.position = new Vector3(0f, 2f, 0f);
            root.AddComponent<Rigidbody2D>();
            var group = root.AddComponent<ColliderGroup>();
            foreach (var (name, local) in new[] { ("FootL", new Vector2(-0.5f, -0.6f)), ("FootR", new Vector2(0.5f, 0f)) })
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = local;
                var box = go.AddComponent<BoxCollider2D>();
                box.size = Vector2.one * 0.3f;
                Register(group, box, name);
            }

            var events = new List<(string label, int step)>();
            var step = 0;
            group.CollisionEnter2D += (entry, _) => events.Add((entry.Label, step));
            for (var i = 0; i < 200 && events.Count == 0; i++, step++) yield return new WaitForFixedUpdate();

            Assert.That(events, Is.Not.Empty);
            Assert.That(events[0].label, Is.EqualTo("FootL"), "The lower foot touches first");
            Assert.That(events.Distinct().Count(), Is.EqualTo(events.Count), "No contact is reported twice in one step");
        }

        [UnityTest]
        public IEnumerator Trigger2D_OnSharedContainer_ResolvesThePartThatIsTouched()
        {
            var group = Shelf(triggers: true);
            var enters = new List<string>();
            var exits = new List<string>();
            Collider2D lastOther = null;
            group.TriggerEnter2D += (entry, other) =>
            {
                enters.Add(entry.Label);
                lastOther = other;
            };
            group.TriggerExit2D += (entry, _) => exits.Add(entry.Label);

            var probe = Ball(new Vector2(10f, 0f), gravity: 0f);
            yield return FixedSteps(2);
            probe.position = new Vector2(2f, 0f);
            yield return FixedSteps(3);
            Assert.That(enters, Is.EqualTo(new[] { "Right" }));
            Assert.That(lastOther, Is.EqualTo(probe.GetComponent<Collider2D>()));

            probe.position = new Vector2(-2f, 0f);
            yield return FixedSteps(3);
            Assert.That(enters, Is.EqualTo(new[] { "Right", "Left" }));
            Assert.That(exits, Is.EqualTo(new[] { "Right" }));

            probe.position = new Vector2(10f, 0f);
            yield return FixedSteps(3);
            Assert.That(exits, Is.EqualTo(new[] { "Right", "Left" }));
        }

        [UnityTest]
        public IEnumerator Stay2D_IsOnlyRoutedWhenSubscribed()
        {
            var group = Shelf(triggers: true);
            group.TriggerEnter2D += (_, _) => { };
            Assert.That(group.GetComponentInChildren<ColliderStayRelay2D>(), Is.Null);

            var stays = 0;
            group.TriggerStay2D += (entry, _) =>
            {
                Assert.That(entry.Label, Is.EqualTo("Left"));
                stays++;
            };
            Assert.That(group.GetComponentInChildren<ColliderStayRelay2D>(), Is.Not.Null);

            Ball(new Vector2(-2f, 0f), gravity: 0f);
            yield return FixedSteps(5);
            Assert.That(stays, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator SpriteColliderSwitcher_SwapsPathsWhenTheSpriteChanges()
        {
            Sprite MakeSprite(string name, int width, int height)
            {
                var texture = Track(new Texture2D(width, height));
                return Track(Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 32f));
            }
            var small = MakeSprite("Small", 32, 32);
            var wide = MakeSprite("Wide", 96, 32);

            var go = Track(new GameObject("Runner"));
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = small;
            var polygon = go.AddComponent<PolygonCollider2D>();
            var switcher = go.AddComponent<SpriteColliderSwitcher>();
            switcher.Collider = polygon;
            Vector2[] Rect(float hx, float hy) => new[] { new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy) };
            switcher.SetShape(small, new[] { Rect(0.5f, 0.5f) });
            switcher.SetShape(wide, new[] { Rect(1.5f, 0.5f) });

            yield return null;
            Assert.That(polygon.pathCount, Is.EqualTo(1));
            Assert.That(polygon.GetPath(0).Max(p => p.x), Is.EqualTo(0.5f).Within(1e-4f));

            renderer.sprite = wide;
            yield return null;
            Assert.That(polygon.GetPath(0).Max(p => p.x), Is.EqualTo(1.5f).Within(1e-4f));

            renderer.flipY = true;
            switcher.SetShape(wide, new[] { new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) } });
            yield return null;
            Assert.That(polygon.GetPath(0).Min(p => p.y), Is.EqualTo(-1f).Within(1e-4f), "flip Y mirrors the stored path");
        }
    }
}
