using System;
using System.Collections.Generic;
using Unity.Mathematics;
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

            var context = CreateContext(target, settings, space);

            // Painted regions are authored data: when a group has them, they define its colliders.
            var group = target.GetComponent<ColliderGroup>();
            if (group != null && group.Recipe.HasRegions)
            {
                context = CreateContext(target, settings, space, result);
                ComputeRegions(group, settings, context, space, result);
                if (result.ShapeCount == 0) result.Skip(Strings.SkipNoMeshes);
                return result;
            }

            // Per-LOD colliders: collect every level of one LOD Group and remember each renderer's level.
            var lodGroup = settings.PerLodColliders ? target.GetComponentInChildren<LODGroup>(true) : null;
            var collectSettings = settings;
            if (lodGroup != null)
            {
                collectSettings = settings.Clone();
                collectSettings.Lod0Only = false;
            }

            var warnings = new List<string>();
            var renderers = MeshSource.CollectRenderers(target, collectSettings, warnings, includeSprites: true);
            var lodOf = lodGroup != null ? LodLevels(target, lodGroup, renderers) : null;
            if (lodOf != null && lodOf.Count > 0) result.LodGroup = lodGroup;
            foreach (var w in warnings) result.Warn(w);
            var tilemaps = TilemapSetup.Collect(target, settings);
            result.RendererCount = renderers.Count + tilemaps.Count;
            if (renderers.Count == 0 && tilemaps.Count == 0)
            {
                result.Skip(Strings.SkipNoMeshes);
                return result;
            }
            ComputeTilemaps(tilemaps, settings, space, result);

            // Sprites (and meshes moved by a Rigidbody2D, or everything when Dimension is 2D) get 2D colliders.
            var renderers3D = new List<Renderer>(renderers.Count);
            var renderers2D = new List<Renderer>();
            var skippedSprites = 0;
            foreach (var renderer in renderers)
            {
                if (GenerationPipeline2D.Is2D(renderer, target, settings)) renderers2D.Add(renderer);
                else if (SpriteSource.IsSprite(renderer)) skippedSprites++;
                else renderers3D.Add(renderer);
            }
            if (skippedSprites > 0) result.Warn(string.Format(Strings.WarningSpritesSkipped3D, skippedSprites));
            if (renderers2D.Count > 0) GenerationPipeline2D.Compute(target, settings, renderers2D, space, result);
            if (renderers3D.Count > 0 && settings.AllowRotation && !space.IsUniform) result.Warn(Strings.WarningNonUniformScale);

            var strategyKind = settings.Strategy;
            if (strategyKind == ColliderStrategy.OptimizedMesh && context.IsDynamic && renderers3D.Count > 0)
            {
                // PhysX: non-kinematic Rigidbodies cannot use concave mesh colliders.
                result.Warn(Strings.WarningDynamicConcave);
                strategyKind = ColliderStrategy.ConvexHull;
            }

            // Skinned meshes get per-bone colliders (Per Bone, and Auto); everything else uses the mesh strategy
            // (Auto when Per Bone was chosen for a non-skinned mesh).
            var perBone = strategyKind == ColliderStrategy.PerBone || strategyKind == ColliderStrategy.Auto;
            var strategy = StrategyRegistry.Get(strategyKind == ColliderStrategy.PerBone ? ColliderStrategy.Auto : strategyKind, settings);

            var parts = new List<MeshGeometry>(renderers3D.Count);
            var partLods = new List<int>(renderers3D.Count);
            foreach (var renderer in renderers3D)
            {
                var lod = lodOf != null && lodOf.TryGetValue(renderer, out var level) ? level : -1;
                var before = result.Shapes.Count;
                if (perBone && renderer is SkinnedMeshRenderer skinned && TryFitBones(skinned, settings, result))
                {
                    for (var s = before; s < result.Shapes.Count; s++) result.Shapes[s].Lod = lod;
                    continue;
                }
                try
                {
                    var geometry = MeshSource.Read(renderer, space);
                    result.SourceTriangles += geometry.TriangleCount;
                    if (geometry.Vertices.Length == 0) continue;
                    parts.Add(geometry);
                    partLods.Add(lod);
                }
                catch (Exception e)
                {
                    result.Warn(string.Format(Strings.WarningMeshReadFailed, renderer.name, e.Message));
                }
            }

            if (settings.Granularity == MeshGranularity.Combined && parts.Count > 1) CombinePerLod(target.name, parts, partLods);

            var settingsKey = FitCache.Active ? strategyKind + "|" + JsonUtility.ToJson(settings) : null;
            for (var p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                var shapes = FitCache.Fit(strategy, part, context, settingsKey);
                for (var i = 0; i < shapes.Count; i++)
                {
                    var shape = shapes[i];
                    shape.Label = shapes.Count == 1 ? part.Label : $"{part.Label} {i + 1}";
                    shape.Lod = partLods[p];

                    // The optimized-mesh path inflates inside the optimizer; everything else is inflated here.
                    if (shape.Kind != ShapeKind.ConcaveMesh) InflateDeflate.Apply(shape, settings.Inflate);
                    // A lone hull is measured here; multi-part strategies attach stats for the whole set themselves.
                    if (shapes.Count == 1 && shape.Kind == ShapeKind.ConvexMesh && shape.Stats == null) MeasureMesh(shape, part);
                    if (shape.Stats.HasValue) result.Stats.Add(shape.Stats.Value);
                    if (!string.IsNullOrEmpty(shape.Note)) result.Notes.Add($"{part.Label}: {shape.Note}");

                    result.Shapes.Add(shape);
                }
            }

            if (result.ShapeCount == 0 && result.TilemapComposites.Count == 0) result.Skip(Strings.SkipNoMeshes);
            return result;
        }

        /// <summary>Tilemaps: Composite → set up live colliders at apply time; Baked → traced polygon shapes now.</summary>
        private static void ComputeTilemaps(List<UnityEngine.Tilemaps.Tilemap> tilemaps, GenerationSettings settings, in TargetSpace space,
            TargetResult result)
        {
            foreach (var tilemap in tilemaps)
            {
                if (settings.TilemapColliders == TilemapColliderMode.Composite)
                {
                    result.TilemapComposites.Add(tilemap);
                    continue;
                }
                var outline = TilemapSetup.BakedOutline(tilemap, space);
                if (outline == null)
                {
                    result.Warn(string.Format(Strings.WarningTilemapLayout, tilemap.name));
                    result.TilemapComposites.Add(tilemap);
                    continue;
                }
                var shapes = TilemapSetup.BakedShapes(outline, settings);
                result.Shapes2D.AddRange(shapes);
                if (shapes.Count > 0)
                {
                    var points = 0;
                    foreach (var s in shapes) points += s.PointCount;
                    result.Notes.Add(string.Format(Strings.NoteTilemapBaked, tilemap.name, points));
                }
            }
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
            var geometry2D = new Dictionary<Renderer, (float2[] vertices, int[] triangles)>();
            var context2D = new FitContext(settings.AllowRotation && GenerationPipeline2D.IsUniform2D(space));
            foreach (var region in group.Recipe.Regions)
            {
                if (region.Renderer == null)
                {
                    result.Warn(string.Format(Strings.WarningRegionRendererMissing, region.Label));
                    continue;
                }

                if (GenerationPipeline2D.Is2D(region.Renderer, group.gameObject, settings))
                {
                    if (!geometry2D.TryGetValue(region.Renderer, out var paint))
                    {
                        RegionFitter2D.PaintGeometry(region.Renderer, settings, space, out var vertices, out var triangles);
                        geometry2D[region.Renderer] = paint = (vertices, triangles);
                    }
                    var shapes2D = RegionFitter2D.Fit(region, paint.vertices, paint.triangles, settings, context2D, out var invalid2D);
                    if (invalid2D > 0) result.Warn(string.Format(Strings.WarningRegionTrianglesChanged, region.Label, invalid2D));
                    result.Shapes2D.AddRange(shapes2D);
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
            result.RendererCount = geometryCache.Count + geometry2D.Count;
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

        /// <summary>
        /// LOD level of each renderer in <paramref name="lodGroup"/> (-1 when it is shared by several levels). Higher-LOD
        /// renderers of other LOD Groups under the target are dropped from <paramref name="renderers"/>, as with LOD 0 Only.
        /// </summary>
        private static Dictionary<Renderer, int> LodLevels(GameObject target, LODGroup lodGroup, List<Renderer> renderers)
        {
            var levels = new Dictionary<Renderer, int>();
            var lods = lodGroup.GetLODs();
            for (var i = 0; i < lods.Length; i++)
                foreach (var r in lods[i].renderers)
                    if (r != null) levels[r] = levels.ContainsKey(r) && levels[r] != i ? -1 : i;

            var otherHigherLods = new HashSet<Renderer>();
            foreach (var other in target.GetComponentsInChildren<LODGroup>(true))
            {
                if (other == lodGroup) continue;
                var otherLods = other.GetLODs();
                for (var i = 1; i < otherLods.Length; i++)
                    foreach (var r in otherLods[i].renderers)
                        if (r != null && !System.Array.Exists(otherLods[0].renderers, x => x == r)) otherHigherLods.Add(r);
            }
            renderers.RemoveAll(otherHigherLods.Contains);

            var result = new Dictionary<Renderer, int>();
            foreach (var r in renderers)
                if (levels.TryGetValue(r, out var level) && level >= 0) result[r] = level;
            return result;
        }

        /// <summary>Combined granularity with per-LOD parts: one combined part per LOD level (and one for untagged parts).</summary>
        private static void CombinePerLod(string name, List<MeshGeometry> parts, List<int> partLods)
        {
            var levels = new List<int>();
            foreach (var lod in partLods)
                if (!levels.Contains(lod)) levels.Add(lod);

            var combined = new List<MeshGeometry>(levels.Count);
            foreach (var lod in levels)
            {
                var subset = new List<MeshGeometry>();
                for (var i = 0; i < parts.Count; i++)
                    if (partLods[i] == lod) subset.Add(parts[i]);
                combined.Add(MeshGeometry.Combine(lod < 0 || levels.Count == 1 ? name : $"{name} LOD{lod}", subset));
            }
            parts.Clear();
            parts.AddRange(combined);
            partLods.Clear();
            partLods.AddRange(levels);
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
