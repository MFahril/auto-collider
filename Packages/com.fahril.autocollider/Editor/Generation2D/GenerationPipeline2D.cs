using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.U2D;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Compute stage for 2D colliders: reads sprite / flat-mesh outlines and fits shapes into <see cref="TargetResult.Shapes2D"/>
    /// without touching the scene. Called by <see cref="GenerationPipeline.Compute"/> for the renderers that resolve to 2D.
    /// </summary>
    internal static class GenerationPipeline2D
    {
        /// <summary>
        /// True when <paramref name="renderer"/> gets 2D colliders: sprites unless Dimension is 3D; meshes when Dimension is 2D
        /// or (Auto) a Rigidbody2D moves the target.
        /// </summary>
        public static bool Is2D(Renderer renderer, GameObject target, GenerationSettings settings)
        {
            if (SpriteSource.IsSprite(renderer)) return settings.Dimension != ColliderDimension.ThreeD;
            if (renderer is SpriteRenderer) return false;
            return settings.Dimension == ColliderDimension.TwoD
                   || (settings.Dimension == ColliderDimension.Auto && target.GetComponentInParent<Rigidbody2D>() != null);
        }

        /// <summary>2D rotated primitives need equal, positive X / Y scale (a rotated child under non-uniform scale is sheared).</summary>
        public static bool IsUniform2D(in TargetSpace space)
        {
            var s = space.Scale;
            return s.x > 0f && s.y > 0f && math.abs(s.x - s.y) <= 1e-4f * s.x;
        }

        public static void Compute(GameObject target, GenerationSettings settings, IReadOnlyList<Renderer> renderers, in TargetSpace space,
            TargetResult result)
        {
            var uniform = IsUniform2D(space);
            if (settings.AllowRotation && !uniform) result.Warn(Strings.WarningNonUniformScale);
            var context = new FitContext(settings.AllowRotation && uniform);

            // Skinned sprites get per-bone colliders (Per Bone, and Auto); everything else uses the outline strategy.
            var perBone = settings.Strategy2D == ColliderStrategy2D.PerBone || settings.Strategy2D == ColliderStrategy2D.Auto;
            var outlines = new List<Outline2D>(renderers.Count);
            var warnings = new List<string>();
            foreach (var renderer in renderers)
            {
                if (perBone && renderer is SpriteRenderer sprite && TryFitBones(sprite, settings, result)) continue;
                try
                {
                    var outline = SpriteSource.Read(renderer, space, settings, warnings);
                    if (!outline.IsEmpty) outlines.Add(outline);
                }
                catch (Exception e)
                {
                    result.Warn(string.Format(Strings.WarningMeshReadFailed, renderer.name, e.Message));
                }
            }
            foreach (var w in warnings) result.Warn(w);

            if (settings.Granularity == MeshGranularity.Combined && outlines.Count > 1)
                outlines = new List<Outline2D> { Union(target.name, outlines, settings.OutlineResolution) };

            var strategyKind = settings.Strategy2D == ColliderStrategy2D.PerBone ? ColliderStrategy2D.Auto : settings.Strategy2D;
            var strategy = Strategies2D.Get(strategyKind, settings);
            var settingsKey = FitCache.Active ? "2D|" + strategyKind + "|" + JsonUtility.ToJson(settings) : null;
            foreach (var outline in outlines)
            {
                var shapes = FitCache.Fit2D(strategy, outline, context, settingsKey);
                for (var i = 0; i < shapes.Count; i++)
                {
                    var shape = shapes[i];
                    shape.Label = shapes.Count == 1 ? outline.Label : $"{outline.Label} {i + 1}";
                    Inflate(shape, settings.Inflate);
                    if (!string.IsNullOrEmpty(shape.Note)) result.Notes.Add($"{outline.Label}: {shape.Note}");
                    result.Shapes2D.Add(shape);
                }
            }
        }

        /// <summary>
        /// Per-bone colliders for a sprite deformed by a 2D Animation SpriteSkin, parented to its bones. False when the sprite
        /// has no skinning data or no SpriteSkin (the caller then fits its outline instead).
        /// </summary>
        internal static bool TryFitBones(SpriteRenderer renderer, GenerationSettings settings, TargetResult result)
        {
            var sprite = renderer.sprite;
            var bones = SpriteSkinAccess.Bones(renderer);
            if (sprite == null || bones == null || bones.Length == 0) return false;
            if (!sprite.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.BlendWeight)) return false;

            float2[] vertices;
            int[] triangles;
            BoneWeight[] weights;
            float4x4[] bindposes;
            try
            {
                var positions = sprite.GetVertexAttribute<Vector3>(UnityEngine.Rendering.VertexAttribute.Position);
                var boneWeights = sprite.GetVertexAttribute<BoneWeight>(UnityEngine.Rendering.VertexAttribute.BlendWeight);
                var indices = sprite.GetIndices();
                var poses = sprite.GetBindPoses();

                vertices = new float2[positions.Length];
                for (var i = 0; i < vertices.Length; i++) vertices[i] = ((float3)positions[i]).xy;
                weights = new BoneWeight[boneWeights.Length];
                for (var i = 0; i < weights.Length; i++) weights[i] = boneWeights[i];
                triangles = new int[indices.Length];
                for (var i = 0; i < triangles.Length; i++) triangles[i] = indices[i];
                bindposes = new float4x4[poses.Length];
                for (var i = 0; i < bindposes.Length; i++) bindposes[i] = poses[i];
            }
            catch (Exception e)
            {
                result.Warn(string.Format(Strings.WarningMeshReadFailed, renderer.name, e.Message));
                return false;
            }
            if (weights.Length != vertices.Length || bindposes.Length != bones.Length) return false;

            var fits = BoneFitter2D.Fit(vertices, triangles, BoneFitter.StrongestBones(weights, settings.BoneWeightThreshold), bindposes,
                settings.BoneShape, settings);
            var named = new List<(string, FittedShape2D)>();
            foreach (var fit in fits)
            {
                var bone = bones[fit.Bone];
                if (bone == null) continue;
                var shape = fit.Shape;
                shape.Parent = bone;
                shape.Label = bone.name;
                Inflate(shape, settings.Inflate);
                named.Add((bone.name, shape));
                result.Shapes2D.Add(shape);
            }
            if (named.Count == 0) return false;

            if (settings.SymmetricBones) BoneFitter2D.Symmetrize(named);
            result.Notes.Add(string.Format(Strings.NoteBones, renderer.name, named.Count));
            return true;
        }

        /// <summary>One outline covering all of them (even-odd raster union, traced back to loops).</summary>
        private static Outline2D Union(string label, List<Outline2D> outlines, int resolution)
        {
            var loops = new List<float2[]>();
            foreach (var outline in outlines) loops.AddRange(outline.Loops);
            // Overlapping outer loops must add, not cancel: fill each outline separately into one raster.
            Polygon2D.Bounds(loops, out var min, out var max);
            var raster = OutlineTracer.Raster.Over(min, max, resolution);
            foreach (var outline in outlines) OutlineTracer.RasterizeLoops(outline.Loops, resolution, raster);
            return new Outline2D(label, OutlineTracer.Trace(raster));
        }

        /// <summary>Grow (+) or shrink (−) a 2D shape along its outline.</summary>
        public static void Inflate(FittedShape2D shape, float distance)
        {
            if (distance == 0f) return;
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                    shape.Size = math.max(shape.Size + 2f * distance, PhysicsLimits.MinExtent);
                    break;
                case ShapeKind2D.Circle:
                    shape.Radius = math.max(shape.Radius + distance, PhysicsLimits.MinExtent);
                    break;
                case ShapeKind2D.Capsule:
                    shape.Radius = math.max(shape.Radius + distance, PhysicsLimits.MinExtent);
                    shape.Height = math.max(shape.Height + 2f * distance, 2f * shape.Radius);
                    break;
                default:
                    var paths = new float2[shape.Paths.Length][];
                    for (var i = 0; i < paths.Length; i++) paths[i] = Polygon2D.Offset(shape.Paths[i], distance);
                    shape.Paths = paths;
                    break;
            }
        }
    }
}
