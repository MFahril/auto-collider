using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Per-bone colliders for skinned meshes. Each vertex belongs to its strongest bone; each bone's vertices are fitted in
    /// that bone's local (bind-pose) space, so a collider parented to the bone follows the animation with no runtime code.
    /// </summary>
    internal static class BoneFitter
    {
        public readonly struct BoneShape
        {
            public BoneShape(int bone, FittedShape shape)
            {
                Bone = bone;
                Shape = shape;
            }

            public int Bone { get; }

            /// <summary>Shape in the bone's local space.</summary>
            public FittedShape Shape { get; }
        }

        /// <summary>Strongest bone per vertex, or -1 when its weight is below <paramref name="threshold"/>.</summary>
        public static int[] StrongestBones(IReadOnlyList<BoneWeight> weights, float threshold)
        {
            var result = new int[weights.Count];
            for (var i = 0; i < weights.Count; i++)
            {
                var w = weights[i];
                int bone = w.boneIndex0;
                var best = w.weight0;
                if (w.weight1 > best) { best = w.weight1; bone = w.boneIndex1; }
                if (w.weight2 > best) { best = w.weight2; bone = w.boneIndex2; }
                if (w.weight3 > best) { best = w.weight3; bone = w.boneIndex3; }
                result[i] = best >= threshold && best > 0f ? bone : -1;
            }
            return result;
        }

        /// <param name="vertices">Mesh-space (bind pose) vertices.</param>
        /// <param name="boneOfVertex">From <see cref="StrongestBones"/>.</param>
        /// <param name="bindposes">Mesh space → bone local space, per bone.</param>
        public static List<BoneShape> Fit(IReadOnlyList<float3> vertices, int[] boneOfVertex, IReadOnlyList<float4x4> bindposes,
            PartShape shape, int minVertices)
        {
            var points = new List<float3>[bindposes.Count];
            for (var v = 0; v < vertices.Count; v++)
            {
                var bone = boneOfVertex[v];
                if (bone < 0 || bone >= points.Length) continue;
                (points[bone] ??= new List<float3>()).Add(math.transform(bindposes[bone], vertices[v]));
            }

            var context = new FitContext(allowRotation: true);
            var result = new List<BoneShape>();
            for (var bone = 0; bone < points.Length; bone++)
            {
                if (points[bone] == null || points[bone].Count < math.max(minVertices, PhysicsLimits.MinHullVertices)) continue;
                var input = FitInput.Create(new MeshGeometry("bone", points[bone].ToArray(), new int[0]));
                var fitted = FitShape(input, shape, context);
                if (fitted != null) result.Add(new BoneShape(bone, fitted));
            }
            return result;
        }

        private static FittedShape FitShape(FitInput input, PartShape shape, FitContext context)
        {
            switch (shape)
            {
                case PartShape.Box:
                    return BoxStrategy.FitBox(input, context);
                case PartShape.Sphere:
                    return SphereStrategy.FitSphere(input);
                case PartShape.Capsule:
                    return CapsuleStrategy.FitCapsule(input, context);
                case PartShape.ConvexHull:
                case PartShape.ConvexParts:
                    return new ConvexHullStrategy().Fit(input, context)[0];
                default:
                    return PrimitiveReplacer.BestFit(input, context, out _) ?? CapsuleStrategy.FitCapsule(input, context);
            }
        }

        /// <summary>
        /// Left / right pairs (matched by name, see <see cref="ColliderShapes.MirrorLabel"/>) get the larger of the two sizes,
        /// so a character's colliders stay symmetric even when its skinning is not.
        /// </summary>
        public static void Symmetrize(IReadOnlyList<(string name, FittedShape shape)> shapes)
        {
            var byName = new Dictionary<string, FittedShape>();
            foreach (var (name, shape) in shapes) byName[name] = shape;

            foreach (var (name, shape) in shapes)
            {
                var otherName = ColliderShapes.MirrorLabel(name);
                if (otherName == name || !byName.TryGetValue(otherName, out var other) || other.Kind != shape.Kind) continue;
                if (string.CompareOrdinal(name, otherName) > 0) continue; // handle each pair once

                switch (shape.Kind)
                {
                    case ShapeKind.Box:
                        shape.Size = other.Size = math.max(shape.Size, other.Size);
                        break;
                    case ShapeKind.Sphere:
                        shape.Radius = other.Radius = math.max(shape.Radius, other.Radius);
                        break;
                    case ShapeKind.Capsule:
                        shape.Radius = other.Radius = math.max(shape.Radius, other.Radius);
                        shape.Height = other.Height = math.max(shape.Height, other.Height);
                        break;
                }
            }
        }
    }
}
