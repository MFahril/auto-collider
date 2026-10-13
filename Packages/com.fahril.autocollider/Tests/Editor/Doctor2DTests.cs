using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M10: the Collider Doctor's 2D checks and fixes.</summary>
    public class Doctor2DTests
    {
        private const string MeshFolder = "Assets/__AutoColliderDoctor2DTests";
        private static readonly ColliderDoctor.Options Default = new(false, 1000);
        private TestScene _scene;
        private TestSprites _sprites;
        private readonly List<GameObject> _roots = new();

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _sprites = new TestSprites();
            _roots.Clear();
            ColliderDoctor.MeshFolderOverride = MeshFolder;
        }

        [TearDown]
        public void TearDown()
        {
            ColliderDoctor.MeshFolderOverride = null;
            _scene.Cleanup(MeshFolder);
            _sprites.DestroyAll();
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _roots.Add(go);
            return go;
        }

        private List<DoctorIssue> Scan(ColliderDoctor.Options? options = null) => ColliderDoctor.Scan(_roots, options ?? Default);

        private static List<DoctorIssue> Of(List<DoctorIssue> issues, DoctorCheck check) => issues.Where(i => i.Check == check).ToList();

        [Test]
        public void DetailedPolygon_IsReported_SimplifyFixesIt_AndUndoes()
        {
            var polygon = Make("Blob").AddComponent<PolygonCollider2D>();
            var circle = Enumerable.Range(0, 300).Select(i => (Vector2)(Quaternion.Euler(0, 0, i * 1.2f) * Vector3.right)).ToArray();
            polygon.SetPath(0, circle);

            var issue = Of(Scan(), DoctorCheck.ComplexPolygon2D).Single();
            Assert.That(issue.Severity, Is.EqualTo(DoctorSeverity.Warning));

            ColliderDoctor.FixOne(issue);
            Assert.That(polygon.GetTotalPointCount(), Is.LessThanOrEqualTo(ColliderDoctor.PolygonPointBudget));
            Assert.That(polygon.GetTotalPointCount(), Is.GreaterThan(8), "still round");
            Assert.That(Of(Scan(), DoctorCheck.ComplexPolygon2D), Is.Empty);

            Undo.PerformUndo();
            Assert.That(polygon.GetTotalPointCount(), Is.EqualTo(300));
        }

        [Test]
        public void TilemapWithoutComposite_FixMergesTheTiles()
        {
            var grid = Make("Grid");
            grid.AddComponent<Grid>();
            var map = Make("Ground", grid.transform);
            map.AddComponent<Tilemap>();
            var tiles = map.AddComponent<TilemapCollider2D>();
            Undo.IncrementCurrentGroup();

            var issue = Of(Scan(), DoctorCheck.TilemapWithoutComposite).Single();
            ColliderDoctor.FixOne(issue);

            Assert.That(tiles.compositeOperation, Is.EqualTo(Collider2D.CompositeOperation.Merge));
            Assert.That(map.GetComponent<CompositeCollider2D>() != null);
            Assert.That(map.GetComponent<Rigidbody2D>().bodyType, Is.EqualTo(RigidbodyType2D.Static));
            Assert.That(Of(Scan(), DoctorCheck.TilemapWithoutComposite), Is.Empty);

            Undo.PerformUndo();
            Assert.That(tiles.compositeOperation, Is.EqualTo(Collider2D.CompositeOperation.None));
            Assert.That(map.GetComponent<CompositeCollider2D>() == null);
            Assert.That(map.GetComponent<Rigidbody2D>() == null);
        }

        [Test]
        public void CollidersUnderTheOtherDimensionsRigidbody_AreReported()
        {
            var body3D = Make("Body3D");
            body3D.AddComponent<Rigidbody>();
            Make("Part2D", body3D.transform).AddComponent<BoxCollider2D>();

            var body2D = Make("Body2D");
            body2D.AddComponent<Rigidbody2D>();
            Make("Part3D", body2D.transform).AddComponent<BoxCollider>();
            Make("Fine", body2D.transform).AddComponent<CircleCollider2D>();

            var mixed = Of(Scan(), DoctorCheck.MixedPhysics).Select(i => ((Component)i.Target).name).OrderBy(n => n).ToList();
            Assert.That(mixed, Is.EqualTo(new[] { "Part2D", "Part3D" }));
        }

        [Test]
        public void Collider2DTiltedAroundXOrY_IsReported_ZRotationIsFine()
        {
            var tilted = Make("Tilted");
            tilted.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            tilted.AddComponent<BoxCollider2D>();
            var spun = Make("Spun");
            spun.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            spun.AddComponent<BoxCollider2D>();

            var issue = Of(Scan(), DoctorCheck.Tilted2D).Single();
            Assert.That(((Component)issue.Target).name, Is.EqualTo("Tilted"));
            Assert.That(issue.Severity, Is.EqualTo(DoctorSeverity.Info));
        }

        [Test]
        public void Duplicate2DColliders_FixRemovesTheSecond()
        {
            var go = Make("Coin");
            var first = go.AddComponent<CircleCollider2D>();
            var second = go.AddComponent<CircleCollider2D>();
            Undo.IncrementCurrentGroup();

            var issue = Of(Scan(), DoctorCheck.DuplicateCollider).Single();
            Assert.That(issue.Target, Is.EqualTo(second));
            ColliderDoctor.FixOne(issue);

            Assert.That(go.GetComponents<CircleCollider2D>(), Is.EqualTo(new[] { first }));
            Undo.PerformUndo();
            Assert.That(go.GetComponents<CircleCollider2D>().Length, Is.EqualTo(2));
        }

        [Test]
        public void UserCircleUnderNonUniformScale_IsInfo()
        {
            var go = Make("Wheel");
            go.transform.localScale = new Vector3(2f, 1f, 1f);
            go.AddComponent<CircleCollider2D>();

            var issue = Of(Scan(), DoctorCheck.ScaledPrimitive).Single();
            Assert.That(issue.Severity, Is.EqualTo(DoctorSeverity.Info));
        }

        [Test]
        public void SpriteWithoutCollider_IsReported_GenerateFixesIt()
        {
            var go = TestSprites.SpriteObject("Crate", _sprites.Square(), Vector3.zero);
            _roots.Add(go);
            var withCollider = TestSprites.SpriteObject("Ball", _sprites.Disc(), Vector3.right * 3f);
            withCollider.AddComponent<CircleCollider2D>();
            _roots.Add(withCollider);

            var options = new ColliderDoctor.Options(true, 1000);
            var issue = Of(Scan(options), DoctorCheck.NoCollider).Single();
            Assert.That(issue.Target, Is.EqualTo(go));
            Assert.That(issue.Message, Is.EqualTo(Strings.DoctorNoCollider2D));

            ColliderDoctor.FixOne(issue);
            Assert.That(go.GetComponent<ColliderGroup>().Entries.Single().Is2D);
            Assert.That(Scan(options), Is.Empty, "generated 2D colliders are clean");
        }
    }
}
