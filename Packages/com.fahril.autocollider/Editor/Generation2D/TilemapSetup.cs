using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Tilemap colliders. <b>Composite</b>: Unity's live TilemapCollider2D merged by a CompositeCollider2D on a static
    /// Rigidbody2D. <b>Baked</b>: the tiles (grid cells, or the tile sprite's physics shape) are rasterized, traced and
    /// simplified into polygon outlines — far fewer points than per-tile shapes.
    /// </summary>
    internal static class TilemapSetup
    {
        /// <summary>Sub-cells per tile when tiles use their sprite's shape (grid-only tilemaps are traced at one cell per tile).</summary>
        private const int SpriteSubdivisions = 4;

        /// <summary>Tilemaps under the target that get 2D colliders (same child / inactive / container rules as renderers).</summary>
        public static List<Tilemap> Collect(GameObject target, GenerationSettings settings)
        {
            var result = new List<Tilemap>();
            if (settings.Dimension == ColliderDimension.ThreeD) return result;
            var tilemaps = settings.IncludeChildren ? target.GetComponentsInChildren<Tilemap>(true) : target.GetComponents<Tilemap>();
            var group = target.GetComponent<ColliderGroup>();
            foreach (var tilemap in tilemaps)
            {
                if (!settings.IncludeInactive && !tilemap.gameObject.activeInHierarchy) continue;
                if (group != null && group.IsInContainer(tilemap.transform)) continue;
                result.Add(tilemap);
            }
            return result;
        }

        // ---------- Composite ----------

        /// <summary>
        /// Add TilemapCollider2D (merged) + CompositeCollider2D (+ a static Rigidbody2D when the tilemap has none) and register
        /// them. Returns false — doing nothing — when the tilemap already has a TilemapCollider2D of its own.
        /// </summary>
        public static bool AddComposite(Tilemap tilemap, ColliderGroup group, GenerationSettings settings, List<Component> created)
        {
            var go = tilemap.gameObject;
            if (go.TryGetComponent<TilemapCollider2D>(out var existing) && !(group.TryGetEntry(existing, out var e) && e.Generated)) return false;

            if (!go.TryGetComponent<Rigidbody2D>(out var body))
            {
                body = Undo.AddComponent<Rigidbody2D>(go);
                body.bodyType = RigidbodyType2D.Static;
                ColliderWriter.RecordGroup(group, Strings.UndoGenerate);
                group.OwnedComponents.Add(body);
            }

            var tiles = existing != null ? existing : Undo.AddComponent<TilemapCollider2D>(go);
            tiles.compositeOperation = Collider2D.CompositeOperation.Merge;
            if (!go.TryGetComponent<CompositeCollider2D>(out var composite)) composite = Undo.AddComponent<CompositeCollider2D>(go);
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.isTrigger = settings.IsTrigger;
            composite.sharedMaterial = settings.Material2D;

            foreach (var collider in new Collider2D[] { tiles, composite })
            {
                if (group.TryGetEntry(collider, out _)) continue;
                ColliderWriter.RecordGroup(group, Strings.UndoGenerate);
                group.AddEntry(new ColliderEntry(collider, $"{tilemap.name} ({collider.GetType().Name})", -1, generated: true));
                created.Add(collider);
            }
            return true;
        }

        /// <summary>Destroy owned helper components (Rigidbody2D) whose object no longer has a collider that needs them.</summary>
        public static void RemoveUnusedOwned(ColliderGroup group)
        {
            var owned = group.OwnedComponents;
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                var component = owned[i];
                if (component != null && component.GetComponent<CompositeCollider2D>() != null) continue;
                ColliderWriter.RecordGroup(group, Strings.UndoRemove);
                owned.RemoveAt(i);
                if (component != null) Undo.DestroyObjectImmediate(component);
            }
        }

        // ---------- Baked ----------

        /// <summary>
        /// Outline of the tilemap's colliding tiles in the target's 2D space; null for non-rectangular layouts (use Composite).
        /// </summary>
        public static Outline2D BakedOutline(Tilemap tilemap, in TargetSpace space)
        {
            if (tilemap.cellLayout != GridLayout.CellLayout.Rectangle) return null;
            tilemap.CompressBounds();
            var bounds = tilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0) return new Outline2D(tilemap.name, new List<float2[]>());

            var anySprite = false;
            foreach (var cell in bounds.allPositionsWithin)
                if (tilemap.HasTile(cell) && tilemap.GetColliderType(cell) == Tile.ColliderType.Sprite)
                {
                    anySprite = true;
                    break;
                }

            var cellSize = ((float3)tilemap.cellSize).xy;
            var subdivisions = anySprite ? SpriteSubdivisions : 1;
            var origin = ((float3)tilemap.CellToLocal(bounds.min)).xy;
            var raster = new OutlineTracer.Raster
            {
                Width = bounds.size.x * subdivisions + 2,
                Height = bounds.size.y * subdivisions + 2,
            };
            // Square raster cells; tilemaps with non-square cells are traced on the smaller side.
            raster.CellSize = math.cmin(cellSize) / subdivisions;
            var steps = (int2)math.round(cellSize / raster.CellSize);
            raster.Width = bounds.size.x * steps.x + 2;
            raster.Height = bounds.size.y * steps.y + 2;
            raster.Origin = origin - raster.CellSize;
            raster.Cells = new byte[raster.Width * raster.Height];

            var shape = new List<Vector2>();
            foreach (var cell in bounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(cell)) continue;
                var type = tilemap.GetColliderType(cell);
                if (type == Tile.ColliderType.None) continue;

                if (type == Tile.ColliderType.Grid)
                {
                    var x0 = (cell.x - bounds.xMin) * steps.x + 1;
                    var y0 = (cell.y - bounds.yMin) * steps.y + 1;
                    for (var y = y0; y < y0 + steps.y; y++)
                    for (var x = x0; x < x0 + steps.x; x++)
                        raster.Cells[y * raster.Width + x] = 1;
                    continue;
                }

                var sprite = tilemap.GetSprite(cell);
                if (sprite == null) continue;
                // Tile sprites sit at the cell's anchor with the tile's own transform.
                var anchor = tilemap.CellToLocalInterpolated(cell + tilemap.tileAnchor);
                var matrix = Matrix4x4.Translate(anchor) * tilemap.GetTransformMatrix(cell);
                var loops = new List<float2[]>();
                for (var s = 0; s < sprite.GetPhysicsShapeCount(); s++)
                {
                    shape.Clear();
                    sprite.GetPhysicsShape(s, shape);
                    var loop = new float2[shape.Count];
                    for (var i = 0; i < loop.Length; i++)
                    {
                        var p = matrix.MultiplyPoint3x4(shape[i]);
                        loop[i] = new float2(p.x, p.y);
                    }
                    loops.Add(loop);
                }
                if (loops.Count > 0) OutlineTracer.RasterizeLoops(loops, 0, raster);
            }

            // Tilemap local → the target's 2D space.
            var toScaled = (Matrix4x4)space.WorldToScaled * tilemap.transform.localToWorldMatrix;
            var traced = OutlineTracer.Trace(raster);
            for (var i = 0; i < traced.Count; i++)
                for (var p = 0; p < traced[i].Length; p++)
                {
                    var world = toScaled.MultiplyPoint3x4(new Vector3(traced[i][p].x, traced[i][p].y, 0f));
                    traced[i][p] = new float2(world.x, world.y);
                }
            return new Outline2D(tilemap.name, Polygon2D.OrientByNesting(traced));
        }

        /// <summary>Baked colliders: the simplified outline as one polygon (holes included), or edge loops for the Edge strategy.</summary>
        public static List<FittedShape2D> BakedShapes(Outline2D outline, GenerationSettings settings)
        {
            var shapes = new List<FittedShape2D>();
            if (outline == null || outline.IsEmpty) return shapes;
            // Tiles are axis-aligned: a fraction of a cell keeps every corner while merging straight runs.
            var tolerance = math.min(Strategies2D.Tolerance(outline, settings), outline.Diagonal * 0.002f);
            var loops = PolygonSimplifier.Simplify(outline.Loops, tolerance);
            if (settings.Strategy2D == ColliderStrategy2D.Edge)
                foreach (var loop in loops) shapes.Add(FittedShape2D.FromPaths(new[] { loop }, true));
            else if (loops.Count > 0)
                shapes.Add(FittedShape2D.FromPaths(loops.ToArray(), false));
            foreach (var shape in shapes) shape.Label = outline.Label;
            return shapes;
        }
    }
}
