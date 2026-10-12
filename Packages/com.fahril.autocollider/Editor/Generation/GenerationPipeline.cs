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

            var context = CreateContext(target, settings, space, result);

            // Painted regions are authored data: when a group has them, they define its colliders.
            var group = target.GetComponent<ColliderGroup>();
            if (group != null && group.Recipe.HasRegions)
            {
                ComputeRegions(group, settings, context, space, result);
                if (result.Shapes.Count == 0) result.Skip(Strings.SkipNoMeshes);
                return result;
            }

            var warnings = new List<string>();
            var renderers = MeshSource.CollectRenderers(target, settings, warnings);
            foreach (var w in warnings) result.Warn(w);
            result.RendererCount = renderers.Count;
            if (renderers.Count == 0)
            {
                result.Skip(Strings.SkipNoMeshes);
                return result;
            }

            var strategyKind = settings.Strategy;
            if (strategyKind == ColliderStrategy.OptimizedMesh && context.IsDynamic)
            {
                // PhysX: non-kinematic Rigidbodies cannot use concave mesh colliders.
                result.Warn(Strings.WarningDynamicConcave);
                strategyKind = ColliderStrategy.ConvexHull;
            }

            // Skinned meshes get per-bone colliders (Per Bone, and Auto); everything else uses the mesh strategy
            // (Auto when Per Bone was chosen for a non-skinned mesh).
            var perBone = strategyKind == ColliderStrategy.PerBone || strategyKind == ColliderStrategy.Auto;
            var strategy = StrategyRegistry.Get(strategyKind == ColliderStrategy.PerBone ? ColliderStrategy.Auto : strategyKind, settings);

            var parts = new List<MeshGeometry>(renderers.Count);
            foreach (var renderer in renderers)
            {
                if (perBone && renderer is SkinnedMeshRenderer skinned && TryFitBones(skinned, settings, result)) continue;
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

        /// <summary>Fit options for a target (rotation needs uniform scale; Rigidbody makes it dynamic).</summary>
        public static FitContext CreateContext(GameObject target, GenerationSettings settings, in TargetSpace space, TargetResult result = null)
        {
            var allowRotation = settings.AllowRotation && space.IsUniform;
            if (settings.AllowRotation && !space.IsUniform) result?.Warn(Strings.WarningNonUniformScale);
            return new FitContext(allowRotation, IsDynamic(target));
        }

        private static void ComputeRegions(ColliderGroup group, GenerationSettings settings, FitContext context, in TargetSpace space,
            TargetResult result)
        {
            var geometryCache = new Dictionary<Renderer, MeshGeometry>();
            foreach (var region in group.Recipe.Regions)
            {
                if (region.Renderer == null)
                {
                    result.Warn(string.Format(Strings.WarningRegionRendererMissing, region.Label));
                    continue;
                }
                if (!geometryCache.TryGetValue(region.Renderer, out var geometry))
                {
                    geometry = MeshSource.Read(region.Renderer, space);
                    geometryCache[region.Renderer] = geometry;
                    result.SourceTriangles += geometry.TriangleCount;
                }

                var shapes = RegionFitter.Fit(region, geometry, settings, context, out var invalid);
                if (invalid > 0) result.Warn(string.Format(Strings.WarningRegionTrianglesChanged, region.Label, invalid));
                result.Shapes.AddRange(shapes);
            }
            result.RendererCount = geometryCache.Count;
            result.Notes.Add(string.Format(Strings.NoteRegions, group.Recipe.Regions.Count));
        }

        /// <summary>
        /// Per-bone colliders for a skinned mesh, parented to its bones. False when the mesh has no usable skinning data
        /// (the caller then fits the baked mesh instead).
        /// </summary>
        private static bool TryFitBones(SkinnedMeshRenderer skinned, GenerationSettings settings, TargetResult result)
        {
            var mesh = skinned.sharedMesh;
            var bones = skinned.bones;
            if (mesh == null || bones == null || bones.Length == 0) return false;

            BoneWeight[] weights;
            Matrix4x4[] bindposes;
            Vector3[] vertices;
            try
            {
                weights = mesh.boneWeights;
                bindposes = mesh.bindposes;
                vertices = mesh.vertices;
            }
            catch (Exception e)
            {
                result.Warn(string.Format(Strings.WarningMeshReadFailed, skinned.name, e.Message));
                return false;
            }
            if (weights.Length != vertices.Length || bindposes.Length != bones.Length) return false;

            var points = new Unity.Mathematics.float3[vertices.Length];
            for (var i = 0; i < points.Length; i++) points[i] = vertices[i];
            var poses = new Unity.Mathematics.float4x4[bindposes.Length];
            for (var i = 0; i < poses.Length; i++) poses[i] = bindposes[i];

            var fits = BoneFitter.Fit(points, BoneFitter.StrongestBones(weights, settings.BoneWeightThreshold), poses,
                settings.BoneShape, settings.MinBoneVertices);
            var named = new List<(string, FittedShape)>();
            foreach (var fit in fits)
            {
                var bone = bones[fit.Bone];
                if (bone == null) continue;
                var shape = fit.Shape;
                shape.Parent = bone;
                shape.Label = bone.name;
                InflateDeflate.Apply(shape, settings.Inflate);
                named.Add((bone.name, shape));
                result.Shapes.Add(shape);
            }
            if (named.Count == 0) return false;

            if (settings.SymmetricBones) BoneFitter.Symmetrize(named);
            result.SourceTriangles += mesh.triangles.Length / 3;
            result.Notes.Add(string.Format(Strings.NoteBones, skinned.name, named.Count));
            return true;
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
