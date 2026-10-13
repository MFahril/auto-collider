using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M9: tilemap colliders (Composite / Baked) and painted 2D regions.</summary>
    public class Tilemap2DTests
    {
        private const string MeshFolder = "Assets/__AutoColliderTilemapTests";
        private TestScene _scene;
        private TestSprites _sprites;
        private readonly List<Object> _assets = new();

        [SetUp]
        public void SetUp()
        {
            _scene = new TestScene();
            _sprites = new TestSprites();
            ColliderOperations.MeshFolderOverride = MeshFolder;
            ColliderEditState.ClearPaint();
        }

        [TearDown]
        public void TearDown()
        {
            ColliderEditState.ClearPaint();
            ColliderOperations.MeshFolderOverride = null;
            _scene.Cleanup(MeshFolder);
            _sprites.DestroyAll();
            foreach (var o in _assets)
                if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        /// <summary>Grid + Tilemap with grid-collider tiles at the given cells (cell size 1).</summary>
        private (GameObject grid, Tilemap tilemap) Tilemap(IEnumerable<Vector2Int> cells, Tile.ColliderType type = Tile.ColliderType.Grid)
        {
            var grid = new GameObject("Grid");
            grid.AddComponent<Grid>();
            var go = new GameObject("Ground");
            go.transform.SetParent(grid.transform, false);
            var tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _sprites.Square();
            tile.colliderType = type;
            _assets.Add(tile);
            foreach (var c in cells) tilemap.SetTile(new Vector3Int(c.x, c.y, 0), tile);
            return (grid, tilemap);
        }

        /// <summary>An L of 4 tiles plus one separate tile.</summary>
        private static IEnumerable<Vector2Int> LAndIsland() => new[]
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(5, 3),
        };

        private static float Area(PolygonCollider2D polygon) =>
            math.abs(Enumerable.Range(0, polygon.pathCount).Sum(i => Polygon2D.SignedArea(polygon.GetPath(i).Select(p => (float2)p).ToArray())));

        [Test]
        public void Baked_TracesTheTilesIntoOnePolygon()
        {
            var (grid, _) = Tilemap(LAndIsland());

            var report = BatchProcessor.Generate(new[] { grid }, new GenerationSettings { TilemapColliders = TilemapColliderMode.Baked }, MeshFolder, false);

            var polygon = (PolygonCollider2D)grid.GetComponent<ColliderGroup>().Entries.Single().Collider2D;
            Assert.That(polygon.pathCount, Is.EqualTo(2), "The L and the separate tile");
            Assert.That(Area(polygon), Is.EqualTo(5f).Within(1e-3f));
            Assert.That(polygon.GetTotalPointCount(), Is.EqualTo(6 + 4), "Only corners are kept");
            Assert.That(report.Results[0].Notes.Any(n => n.Contains("baked")));
        }

        [Test]
        public void Baked_EdgeStrategy_MakesEdgeLoops()
        {
            var (grid, _) = Tilemap(LAndIsland());

            BatchProcessor.Generate(new[] { grid },
                new GenerationSettings { TilemapColliders = TilemapColliderMode.Baked, Strategy2D = ColliderStrategy2D.Edge }, MeshFolder, false);

            Assert.That(grid.GetComponent<ColliderGroup>().Entries.Select(e => e.Collider2D), Is.All.InstanceOf<EdgeCollider2D>());
            Assert.That(grid.GetComponent<ColliderGroup>().Entries.Count, Is.EqualTo(2));
        }

        [Test]
        public void Composite_SetsUpLiveColliders_RemoveCleansUp_UndoRestores()
        {
            var (grid, tilemap) = Tilemap(LAndIsland());
            var go = tilemap.gameObject;

            BatchProcessor.Generate(new[] { grid }, new GenerationSettings(), MeshFolder, false);
            var group = grid.GetComponent<ColliderGroup>();
            Assert.That(go.GetComponent<TilemapCollider2D>().compositeOperation, Is.EqualTo(Collider2D.CompositeOperation.Merge));
            Assert.That(go.GetComponent<CompositeCollider2D>(), Is.Not.Null);
            Assert.That(go.GetComponent<Rigidbody2D>().bodyType, Is.EqualTo(RigidbodyType2D.Static));
            Assert.That(group.Entries.Count, Is.EqualTo(2));

            BatchProcessor.Generate(new[] { grid }, new GenerationSettings(), MeshFolder, false);
            Assert.That(group.Entries.Count, Is.EqualTo(2), "Regenerating doesn't duplicate");
            Assert.That(go.GetComponents<Rigidbody2D>().Length, Is.EqualTo(1));

            BatchProcessor.RemoveGenerated(new[] { grid });
            Assert.That(go.GetComponent<TilemapCollider2D>() == null && go.GetComponent<CompositeCollider2D>() == null);
            Assert.That(go.GetComponent<Rigidbody2D>() == null, "The Rigidbody2D Auto Collider added goes too");

            Undo.PerformUndo();
            Assert.That(go.GetComponent<CompositeCollider2D>() != null && go.GetComponent<Rigidbody2D>() != null);
        }

        [Test]
        public void Composite_LeavesAUsersTilemapColliderAlone()
        {
            var (grid, tilemap) = Tilemap(LAndIsland());
            var users = tilemap.gameObject.AddComponent<TilemapCollider2D>();

            var report = BatchProcessor.Generate(new[] { grid }, new GenerationSettings(), MeshFolder, false);

            Assert.That(users != null && users.compositeOperation == Collider2D.CompositeOperation.None);
            Assert.That(tilemap.GetComponent<CompositeCollider2D>() == null);
            Assert.That(report.Results[0].Warnings.Any(w => w.Contains("TilemapCollider2D")));
        }

        // ---------- Painted 2D regions ----------

        [Test]
        public void SpritePaintGrid_HasTwoTrianglesPerOpaqueCell()
        {
            var go = TestSprites.SpriteObject("Crate", _sprites.Square(), Vector3.zero);
            var settings = new GenerationSettings { OutlineResolution = 16 };

            SpriteSource.PaintTriangles(go.GetComponent<SpriteRenderer>(), settings, out var vertices, out var triangles);

            Assert.That(triangles.Length / 3, Is.EqualTo(16 * 16 * 2));
            Assert.That(vertices.Min(v => v.x), Is.EqualTo(-0.5f).Within(1e-5f));
            Assert.That(vertices.Max(v => v.y), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void PaintedSpriteRegion_RegeneratesIdentically_AndLockedRegionIsKept()
        {
            var go = TestSprites.SpriteObject("Crate", _sprites.Rectangle(2f, 1f), Vector3.zero);
            var group = Undo.AddComponent<ColliderGroup>(go);
            group.Recipe.Settings = new GenerationSettings();
            var paint = PaintMesh.Build(go.GetComponent<SpriteRenderer>(), group);
            // Paint the left half of the sprite.
            var left = new HashSet<int>(Enumerable.Range(0, paint.TriangleCount).Where(t => paint.Centroids[t].x < 0f));

            var created = ColliderOperations.CreateRegions(new Dictionary<Renderer, HashSet<int>> { [go.GetComponent<SpriteRenderer>()] = left }, PartShape.Box);

            var box = (BoxCollider2D)created.Single();
            Assert.That(box.size.x, Is.EqualTo(1f).Within(0.03f));
            Assert.That(box.size.y, Is.EqualTo(1f).Within(0.03f));
            Assert.That(group.Entries.Single().PartId, Is.EqualTo(group.Recipe.Regions.Single().Id));
            var before = box.bounds;

            BatchProcessor.Generate(new[] { go }, group.Recipe.Settings, MeshFolder, false);
            var regenerated = group.Entries.Single().Collider2D;
            Assert.That(regenerated.bounds.center, Is.EqualTo(before.center));
            Assert.That(regenerated.bounds.size, Is.EqualTo(before.size));

            group.Entries.Single().Locked = true;
            BatchProcessor.Generate(new[] { go }, group.Recipe.Settings, MeshFolder, false);
            Assert.That(group.Entries.Count, Is.EqualTo(1), "Locked region collider replaces its region");
            Assert.That(group.Entries.Single().Collider2D, Is.SameAs(regenerated));
        }
    }
}
