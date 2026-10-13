using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Per-bone 2D colliders for skinned sprites (2D Animation). Each triangle belongs to the bone most of its vertices are
    /// weighted to; each bone's triangles are traced in that bone's local (bind-pose) space, so a collider parented to the
    /// bone follows the animation with no runtime code.
    /// </summary>
    internal static class BoneFitter2D
    {
        public readonly struct BoneShape
        {
            public BoneShape(int bone, FittedShape2D shape)
            {
                Bone = bone;
                Shape = shape;
            }

            public int Bone { get; }

            /// <summary>Shape in the bone's local space.</summary>
            public FittedShape2D Shape { get; }
        }

        /// <param name="vertices">Sprite-space (bind pose) vertices.</param>
        /// <param name="boneOfVertex">From <see cref="BoneFitter.StrongestBones"/>.</param>
        /// <param name="bindposes">Sprite space → bone local space, per bone.</param>
        public static List<BoneShape> Fit(IReadOnlyList<float2> vertices, int[] triangles, int[] boneOfVertex,
            IReadOnlyList<float4x4> bindposes, PartShape shape, GenerationSettings settings)
        {
            var trianglesOfBone = new List<int>[bindposes.Count];
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var bone = TriangleBone(boneOfVertex[triangles[t]], boneOfVertex[triangles[t + 1]], boneOfVertex[triangles[t + 2]]);
                if (bone < 0 || bone >= trianglesOfBone.Length) continue;
                var list = trianglesOfBone[bone] ??= new List<int>();
                list.Add(triangles[t]);
                list.Add(triangles[t + 1]);
                list.Add(triangles[t + 2]);
            }

            var context = new FitContext(allowRotation: true);
            var result = new List<BoneShape>();
            var local = new float2[vertices.Count];
            for (var bone = 0; bone < trianglesOfBone.Length; bone++)
            {
                if (trianglesOfBone[bone] == null) continue;
                for (var v = 0; v < local.Length; v++)
                    local[v] = math.transform(bindposes[bone], new float3(vertices[v], 0f)).xy;

                var raster = OutlineTracer.RasterizeTriangles(local, trianglesOfBone[bone].ToArray(), settings.OutlineResolution);
                var outline = new Outline2D("bone", OutlineTracer.Trace(raster));
                if (outline.IsEmpty) continue;
                var fitted = FitShape(outline, shape, context, settings);
                if (fitted != null) result.Add(new BoneShape(bone, fitted));
            }
            return result;
        }

        /// <summary>The bone two or three of the triangle's vertices share, else the first vertex's bone.</summary>
        internal static int TriangleBone(int a, int b, int c) =>
            a >= 0 && (a == b || a == c) ? a : b >= 0 && b == c ? b : a >= 0 ? a : b >= 0 ? b : c;

        private static FittedShape2D FitShape(Outline2D outline, PartShape shape, FitContext context, GenerationSettings settings)
        {
            switch (shape)
            {
                case PartShape.Box:
                    return Strategies2D.Box(outline, context);
                case PartShape.Sphere:
                    return Strategies2D.Circle(outline);
                case PartShape.Capsule:
                    return Strategies2D.Capsule(outline, context);
                case PartShape.ConvexHull:
                case PartShape.ConvexParts:
                    return Strategies2D.ConvexPolygon(outline, settings) ?? Strategies2D.Box(outline, context);
                default:
                    return Strategies2D.BestPrimitive(outline, context, out _);
            }
        }

        /// <summary>
        /// Left / right pairs (matched by name, see <see cref="ColliderShapes.MirrorLabel"/>) get the larger of the two sizes,
        /// so a character's colliders stay symmetric even when its skinning is not.
        /// </summary>
        public static void Symmetrize(IReadOnlyList<(string name, FittedShape2D shape)> shapes)
        {
            var byName = new Dictionary<string, FittedShape2D>();
            foreach (var (name, shape) in shapes) byName[name] = shape;

            foreach (var (name, shape) in shapes)
            {
                var otherName = ColliderShapes.MirrorLabel(name);
                if (otherName == name || !byName.TryGetValue(otherName, out var other) || other.Kind != shape.Kind) continue;
                if (string.CompareOrdinal(name, otherName) > 0) continue; // handle each pair once

                switch (shape.Kind)
                {
                    case ShapeKind2D.Box:
                        shape.Size = other.Size = math.max(shape.Size, other.Size);
                        break;
                    case ShapeKind2D.Circle:
                        shape.Radius = other.Radius = math.max(shape.Radius, other.Radius);
                        break;
                    case ShapeKind2D.Capsule:
                        shape.Radius = other.Radius = math.max(shape.Radius, other.Radius);
                        shape.Height = other.Height = math.max(shape.Height, other.Height);
                        break;
                }
            }
        }
    }
}
