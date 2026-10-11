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
            var isDynamic = IsDynamic(target);
            var context = new FitContext(allowRotation, isDynamic);

            var strategyKind = settings.Strategy;
            if (strategyKind == ColliderStrategy.OptimizedMesh && isDynamic)
            {
                // PhysX: non-kinematic Rigidbodies cannot use concave mesh colliders.
                result.Warn(Strings.WarningDynamicConcave);
                strategyKind = ColliderStrategy.ConvexHull;
            }
            var strategy = StrategyRegistry.Get(strategyKind, settings);

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
                    var shape = shapes[i];
                    shape.Label = shapes.Count == 1 ? part.Label : $"{part.Label} {i + 1}";

                    // The optimized-mesh path inflates inside the optimizer; everything else is inflated here.
                    if (shape.Kind != ShapeKind.ConcaveMesh) InflateDeflate.Apply(shape, settings.Inflate);
                    // A lone hull is measured here; multi-part strategies attach stats for the whole set themselves.
                    if (shapes.Count == 1 && shape.Kind == ShapeKind.ConvexMesh && shape.Stats == null) MeasureMesh(shape, part);
                    if (shape.Stats.HasValue) result.Stats.Add(shape.Stats.Value);
                    if (!string.IsNullOrEmpty(shape.Note)) result.Notes.Add($"{part.Label}: {shape.Note}");

                    result.Shapes.Add(shape);
                }
            }

            if (result.Shapes.Count == 0) result.Skip(Strings.SkipNoMeshes);
            return result;
        }

        private static void MeasureMesh(FittedShape shape, MeshGeometry source)
        {
            var collider = new MeshGeometry(shape.Label, shape.MeshVertices, shape.MeshTriangles);
            var measured = MeshOptimizer.Measure(source, collider);
            shape.Stats = measured.Stats;
            shape.Error = measured.Error;
        }

        /// <summary>True when a non-kinematic Rigidbody moves this object.</summary>
        public static bool IsDynamic(GameObject target)
        {
            var body = target.GetComponentInParent<Rigidbody>();
            return body != null && !body.isKinematic;
        }
    }
}
