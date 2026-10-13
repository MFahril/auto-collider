using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Reads 2D outlines in the target's world-sized 2D space (its XY plane, see <see cref="TargetSpace"/>):
    /// sprites from their physics shape, mesh or opaque pixels; meshes from their silhouette along the target's Z axis.
    /// Never needs Read/Write: sprite pixels are copied through a temporary render texture.
    /// </summary>
    internal static class SpriteSource
    {
        /// <summary>Renderers that produce 2D outlines.</summary>
        public static bool IsSprite(Renderer renderer) => renderer is SpriteRenderer sprite && sprite.sprite != null;

        public static Outline2D Read(Renderer renderer, in TargetSpace space, GenerationSettings settings, ICollection<string> warnings)
        {
            List<float2[]> loops;
            if (renderer is SpriteRenderer spriteRenderer)
            {
                var toScaled = (Matrix4x4)space.WorldToScaled * renderer.transform.localToWorldMatrix * Flip(spriteRenderer);
                var spriteLoops = SpriteLoops(spriteRenderer, settings, warnings);
                loops = new List<float2[]>(spriteLoops.Count);
                foreach (var loop in spriteLoops) loops.Add(Transform(loop, toScaled));
            }
            else
            {
                loops = MeshSilhouette(renderer, space, settings.OutlineResolution);
            }
            return new Outline2D(renderer.name, Polygon2D.OrientByNesting(loops));
        }

        // Within a batch (FitCache scope) each sprite is traced once — scenes repeat the same sprite many times.
        private static readonly Dictionary<(Sprite, OutlineSource, float, int), List<float2[]>> Cache = new();

        internal static void ClearCache() => Cache.Clear();

        /// <summary>Loops in sprite units (relative to the pivot). Treat the result as read-only (it may be cached).</summary>
        public static List<float2[]> SpriteLoops(SpriteRenderer renderer, GenerationSettings settings, ICollection<string> warnings)
        {
            var sprite = renderer.sprite;
            if (renderer.drawMode != SpriteDrawMode.Simple)
            {
                // Sliced / tiled sprites are drawn at renderer.size, not at the sprite's own size: use that rectangle.
                var half = (float2)(Vector2)renderer.size * 0.5f;
                return new List<float2[]> { new[] { -half, new float2(half.x, -half.y), half, new float2(-half.x, half.y) } };
            }

            return Loops(sprite, settings, warnings);
        }

        /// <summary>Outline of <paramref name="sprite"/> itself, in sprite units (relative to the pivot, unflipped). Read-only, may be cached.</summary>
        public static List<float2[]> Loops(Sprite sprite, GenerationSettings settings, ICollection<string> warnings)
        {
            var key = (sprite, settings.OutlineSource, settings.AlphaThreshold, settings.OutlineResolution);
            if (FitCache.Active && Cache.TryGetValue(key, out var cached)) return cached;
            var loops = ReadSprite(sprite, settings, warnings);
            if (FitCache.Active) Cache[key] = loops;
            return loops;
        }

        /// <summary>
        /// Auto: the opaque pixels (exact), else the physics shape, else the mesh. Unity's generated physics shapes and tight
        /// meshes are coarse enclosing polygons, so they are only preferred when chosen explicitly (e.g. a custom physics shape).
        /// </summary>
        private static List<float2[]> ReadSprite(Sprite sprite, GenerationSettings settings, ICollection<string> warnings)
        {
            var source = settings.OutlineSource;
            List<float2[]> loops = null;
            if (source == OutlineSource.Alpha || source == OutlineSource.Auto)
                loops = AlphaOutline(sprite, settings.AlphaThreshold, settings.OutlineResolution, source == OutlineSource.Alpha ? warnings : null);
            if ((loops == null || loops.Count == 0) && (source == OutlineSource.PhysicsShape || source == OutlineSource.Auto))
            {
                loops = PhysicsShape(sprite);
                if (loops.Count == 0 && source == OutlineSource.PhysicsShape) warnings?.Add(string.Format(Strings.WarningNoPhysicsShape, sprite.name));
            }
            if (loops == null || loops.Count == 0) loops = MeshOutline(sprite, settings.OutlineResolution);
            return loops;
        }

        private static List<float2[]> PhysicsShape(Sprite sprite)
        {
            var loops = new List<float2[]>();
            var points = new List<Vector2>();
            for (var i = 0; i < sprite.GetPhysicsShapeCount(); i++)
            {
                points.Clear();
                sprite.GetPhysicsShape(i, points);
                if (points.Count < 3) continue;
                var loop = new float2[points.Count];
                for (var p = 0; p < loop.Length; p++) loop[p] = points[p];
                loops.Add(loop);
            }
            return loops;
        }

        /// <summary>Boundary of the sprite's render mesh (edges used by one triangle), rasterized if the mesh isn't a clean manifold.</summary>
        public static List<float2[]> MeshOutline(Sprite sprite, int resolution)
        {
            var source = sprite.vertices;
            var triangles = sprite.triangles;
            var vertices = new float2[source.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = source[i];
            var indices = new int[triangles.Length];
            for (var i = 0; i < indices.Length; i++) indices[i] = triangles[i];

            var loops = BoundaryLoops(vertices, indices);
            if (loops != null) return loops;
            return OutlineTracer.Trace(OutlineTracer.RasterizeTriangles(vertices, indices, resolution));
        }

        /// <summary>Loops of the edges used by exactly one triangle; null when the boundary branches (then rasterize instead).</summary>
        public static List<float2[]> BoundaryLoops(float2[] vertices, int[] triangles)
        {
            // Weld identical positions first so seams don't count as boundary.
            var weld = new Dictionary<float2, int>();
            var id = new int[vertices.Length];
            var positions = new List<float2>();
            for (var i = 0; i < vertices.Length; i++)
            {
                if (!weld.TryGetValue(vertices[i], out var w))
                {
                    w = positions.Count;
                    weld[vertices[i]] = w;
                    positions.Add(vertices[i]);
                }
                id[i] = w;
            }

            var uses = new Dictionary<long, int>();
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            for (var t = 0; t + 2 < triangles.Length; t += 3)
                for (var e = 0; e < 3; e++)
                {
                    var k = Key(id[triangles[t + e]], id[triangles[t + (e + 1) % 3]]);
                    uses[k] = uses.TryGetValue(k, out var c) ? c + 1 : 1;
                }

            var next = new Dictionary<int, int>();
            var order = new List<int>();
            for (var t = 0; t + 2 < triangles.Length; t += 3)
                for (var e = 0; e < 3; e++)
                {
                    var a = id[triangles[t + e]];
                    var b = id[triangles[t + (e + 1) % 3]];
                    if (a == b || uses[Key(a, b)] != 1) continue;
                    if (next.ContainsKey(a)) return null;
                    next[a] = b;
                    order.Add(a);
                }

            var loops = new List<float2[]>();
            var visited = new HashSet<int>();
            foreach (var start in order)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<float2>();
                var v = start;
                while (visited.Add(v))
                {
                    loop.Add(positions[v]);
                    if (!next.TryGetValue(v, out v)) return null;
                }
                if (v != start) return null;
                if (loop.Count >= 3) loops.Add(loop.ToArray());
            }
            return loops;
        }

        /// <summary>Trace the opaque pixels (alpha ≥ threshold), at most <paramref name="resolution"/> cells along the longest side.</summary>
        /// <summary>Opaque cells of a sprite in sprite units (relative to the pivot), at most <c>resolution</c> cells along the longest side.</summary>
        internal sealed class AlphaMask
        {
            public bool[] Cells;
            public int Width, Height;
            public float2 Origin;
            public float CellSize;
            /// <summary>Upper corner of the readable pixel area (cells may extend past it).</summary>
            public float2 Max;
        }

        public static AlphaMask ReadAlphaMask(Sprite sprite, float threshold, int resolution, ICollection<string> warnings)
        {
            Rect textureRect;
            Vector2 rectOffset;
            try
            {
                textureRect = sprite.textureRect;
                rectOffset = sprite.textureRectOffset;
            }
            catch (UnityException)
            {
                // Tightly packed atlas sprites have no rectangle to read.
                warnings?.Add(string.Format(Strings.WarningAlphaUnavailable, sprite.name));
                return null;
            }

            var width = Mathf.Max(1, Mathf.RoundToInt(textureRect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(textureRect.height));
            var pixels = ReadPixels(sprite.texture, textureRect, width, height);
            if (pixels == null) return null;

            // Downsample to the outline resolution: a cell is solid when any of its pixels is.
            var step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / (float)resolution));
            var w = (width + step - 1) / step;
            var h = (height + step - 1) / step;
            var mask = new bool[w * h];
            var cutoff = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 1, 255);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (pixels[y * width + x].a >= cutoff)
                    mask[y / step * w + x / step] = true;

            var ppu = sprite.pixelsPerUnit;
            var origin = ((float2)(Vector2)rectOffset - (float2)(Vector2)sprite.pivot) / ppu;
            return new AlphaMask
            {
                Cells = mask, Width = w, Height = h, Origin = origin, CellSize = step / ppu,
                Max = origin + new float2(width, height) / ppu,
            };
        }

        public static List<float2[]> AlphaOutline(Sprite sprite, float threshold, int resolution, ICollection<string> warnings)
        {
            var mask = ReadAlphaMask(sprite, threshold, resolution, warnings);
            if (mask == null) return null;
            var loops = OutlineTracer.Trace(OutlineTracer.FromMask(mask.Cells, mask.Width, mask.Height, mask.Origin, mask.CellSize));
            // Cells past the texture edge would make the outline overshoot the sprite: clamp to the readable rectangle.
            for (var i = 0; i < loops.Count; i++)
                for (var p = 0; p < loops[i].Length; p++)
                    loops[i][p] = math.clamp(loops[i][p], mask.Origin, mask.Max);
            return loops;
        }

        /// <summary>
        /// Paintable triangles of a sprite, in sprite units: two per opaque cell of its alpha mask (row-major, deterministic —
        /// painted regions store these indices). Unity's own sprite meshes are far too coarse to paint (a disc has 8 vertices).
        /// Falls back to the sprite mesh for tightly packed atlas sprites.
        /// </summary>
        public static void PaintTriangles(SpriteRenderer renderer, GenerationSettings settings, out float2[] vertices, out int[] triangles)
        {
            var sprite = renderer.sprite;
            var mask = renderer.drawMode == SpriteDrawMode.Simple ? ReadAlphaMask(sprite, settings.AlphaThreshold, settings.OutlineResolution, null) : null;
            if (mask == null)
            {
                var source = sprite.vertices;
                vertices = new float2[source.Length];
                for (var i = 0; i < vertices.Length; i++) vertices[i] = source[i];
                var indices = sprite.triangles;
                triangles = new int[indices.Length];
                for (var i = 0; i < triangles.Length; i++) triangles[i] = indices[i];
                return;
            }

            var verts = new List<float2>();
            var tris = new List<int>();
            for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (!mask.Cells[y * mask.Width + x]) continue;
                var min = math.min(mask.Origin + new float2(x, y) * mask.CellSize, mask.Max);
                var max = math.min(mask.Origin + new float2(x + 1, y + 1) * mask.CellSize, mask.Max);
                var b = verts.Count;
                verts.Add(min);
                verts.Add(new float2(max.x, min.y));
                verts.Add(max);
                verts.Add(new float2(min.x, max.y));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            vertices = verts.ToArray();
            triangles = tris.ToArray();
        }

        /// <summary>Sprite units → the target's 2D space (renderer transform, flips).</summary>
        public static Matrix4x4 SpriteToScaled(SpriteRenderer renderer, in TargetSpace space) =>
            (Matrix4x4)space.WorldToScaled * renderer.transform.localToWorldMatrix * Flip(renderer);

        /// <summary>Sprite units → world.</summary>
        public static Matrix4x4 SpriteToWorld(SpriteRenderer renderer) => renderer.transform.localToWorldMatrix * Flip(renderer);

        private static Color32[] ReadPixels(Texture2D texture, Rect rect, int width, int height)
        {
            if (texture == null) return null;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
                readable.Apply();
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(readable);
            }
        }

        /// <summary>Silhouette of a mesh seen along the target's Z axis (union of its triangles projected onto XY).</summary>
        private static List<float2[]> MeshSilhouette(Renderer renderer, in TargetSpace space, int resolution)
        {
            var geometry = MeshSource.Read(renderer, space);
            if (geometry.TriangleCount == 0) return new List<float2[]>();
            var projected = new float2[geometry.Vertices.Length];
            for (var i = 0; i < projected.Length; i++) projected[i] = geometry.Vertices[i].xy;
            return OutlineTracer.Trace(OutlineTracer.RasterizeTriangles(projected, geometry.Triangles, resolution));
        }

        private static Matrix4x4 Flip(SpriteRenderer renderer) =>
            Matrix4x4.Scale(new Vector3(renderer.flipX ? -1f : 1f, renderer.flipY ? -1f : 1f, 1f));

        private static float2[] Transform(float2[] loop, Matrix4x4 matrix)
        {
            var result = new float2[loop.Length];
            for (var i = 0; i < loop.Length; i++)
            {
                var p = matrix.MultiplyPoint3x4(new Vector3(loop[i].x, loop[i].y, 0f));
                result[i] = new float2(p.x, p.y);
            }
            return result;
        }
    }
}
