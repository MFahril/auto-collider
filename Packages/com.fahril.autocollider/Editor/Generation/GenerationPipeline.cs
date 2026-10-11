using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Plan → Compute stage of generation: reads meshes and fits shapes without touching the scene.
    /// The result can be previewed, then applied by <see cref="ColliderWriter"/>.
    /// </summary>
    internal static class GenerationPipeline
    {
        public static TargetResult Compute(GameObject target, GenerationSettings settings)
        {
            var result = new TargetResult(target);
            if (target == null)
            {
                result.Skip(Strings.SkipMissingTarget);
                return result;
            }

            if (!TargetSpace.TryCreate(target.transform, out var space, out var spaceError))
            {
                result.Skip(spaceError);
                return result;
            }
            result.Space = space;

            var warnings = new List<string>();
            var renderers = MeshSource.CollectRenderers(target, settings, warnings);
            foreach (var w in warnings) result.Warn(w);
            result.RendererCount = renderers.Count;
            if (renderers.Count == 0)
            {
                result.Skip(Strings.SkipNoMeshes);
                return result;
            }

            var allowRotation = settings.AllowRotation && space.IsUniform;
            if (settings.AllowRotation && !space.IsUniform) result.Warn(Strings.WarningNonUniformScale);
            var context = new FitContext(allowRotation);
            var strategy = StrategyRegistry.Get(settings.Strategy);

            var parts = new List<MeshGeometry>(renderers.Count);
            foreach (var renderer in renderers)
            {
                try
                {
                    var geometry = MeshSource.Read(renderer, space);
                    result.SourceTriangles += geometry.TriangleCount;
                    if (geometry.Vertices.Length > 0) parts.Add(geometry);
                }
                catch (Exception e)
                {
                    result.Warn(string.Format(Strings.WarningMeshReadFailed, renderer.name, e.Message));
                }
            }

            if (settings.Granularity == MeshGranularity.Combined && parts.Count > 1)
                parts = new List<MeshGeometry> { MeshGeometry.Combine(target.name, parts) };

            foreach (var part in parts)
            {
                var shapes = strategy.Fit(FitInput.Create(part), context);
                for (var i = 0; i < shapes.Count; i++)
                {
                    shapes[i].Label = shapes.Count == 1 ? part.Label : $"{part.Label} {i + 1}";
                    result.Shapes.Add(shapes[i]);
                }
            }

            if (result.Shapes.Count == 0) result.Skip(Strings.SkipNoMeshes);
            return result;
        }
    }
}
