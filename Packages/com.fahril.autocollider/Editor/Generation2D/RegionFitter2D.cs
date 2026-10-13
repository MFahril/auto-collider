using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// 2D counterpart of <see cref="RegionFitter"/>: the union of a region's painted triangles (sprite paint grid or flat-mesh
    /// triangles, projected to the target's XY plane) is traced and fitted by the region's shape. Used by the paint tool and
    /// by generation, so painting and regenerating give identical colliders.
    /// </summary>
    internal static class RegionFitter2D
    {
        /// <summary>Paintable triangles of a 2D renderer, in the target's 2D space — the indices regions store.</summary>
        public static void PaintGeometry(Renderer renderer, GenerationSettings settings, in TargetSpace space, out float2[] vertices, out int[] triangles)
        {
            if (renderer is SpriteRenderer sprite)
            {
                SpriteSource.PaintTriangles(sprite, settings, out var local, out triangles);
                var toScaled = SpriteSource.SpriteToScaled(sprite, space);
                vertices = new float2[local.Length];
                for (var i = 0; i < local.Length; i++)
                {
                    var p = toScaled.MultiplyPoint3x4(new Vector3(local[i].x, local[i].y, 0f));
                    vertices[i] = new float2(p.x, p.y);
                }
                return;
            }

            var geometry = MeshSource.Read(renderer, space);
            vertices = new float2[geometry.Vertices.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = geometry.Vertices[i].xy;
            triangles = geometry.Triangles;
        }

        public static List<FittedShape2D> Fit(PaintedRegion region, float2[] vertices, int[] triangles, GenerationSettings settings,
            FitContext context, out int invalidTriangles)
        {
            invalidTriangles = 0;
            var selected = new List<int>(region.Triangles.Length * 3);
            var count = triangles.Length / 3;
            foreach (var t in region.Triangles)
            {
                if (t < 0 || t >= count)
                {
                    invalidTriangles++;
                    continue;
                }
                selected.Add(triangles[t * 3]);
                selected.Add(triangles[t * 3 + 1]);
                selected.Add(triangles[t * 3 + 2]);
            }

            var shapes = new List<FittedShape2D>();
            if (selected.Count == 0) return shapes;
            var loops = OutlineTracer.Trace(OutlineTracer.RasterizeTriangles(vertices, selected.ToArray(), settings.OutlineResolution));
            var outline = new Outline2D(region.Label, loops);
            if (outline.IsEmpty) return shapes;

            var kind = region.Shape switch
            {
                PartShape.Box => ColliderStrategy2D.Box,
                PartShape.Sphere => ColliderStrategy2D.Circle,
                PartShape.Capsule => ColliderStrategy2D.Capsule,
                PartShape.ConvexHull => ColliderStrategy2D.ConvexPolygon,
                PartShape.ConvexParts => ColliderStrategy2D.Polygon,
                _ => ColliderStrategy2D.Auto,
            };
            shapes.AddRange(Strategies2D.Get(kind, settings).Fit(outline, context));
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                shape.Label = shapes.Count == 1 ? region.Label : $"{region.Label} {i + 1}";
                shape.RegionId = region.Id;
                shape.Note = null;
                GenerationPipeline2D.Inflate(shape, settings.Inflate);
            }
            return shapes;
        }
    }
}
