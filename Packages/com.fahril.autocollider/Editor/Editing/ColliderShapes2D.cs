using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// 2D counterpart of <see cref="ColliderShapes"/>: live <see cref="Collider2D"/>s ↔ <see cref="FittedShape2D"/>s in the
    /// group's world-sized 2D space (its XY plane), plus containment, sample points and mirroring.
    /// </summary>
    internal static class ColliderShapes2D
    {
        private const int CircleSamples = 32;

        public static bool TryFind(Collider2D collider, out ColliderGroup group, out ColliderEntry entry)
        {
            entry = null;
            group = collider != null ? collider.GetComponentInParent<ColliderGroup>(true) : null;
            return group != null && group.TryGetEntry(collider, out entry);
        }

        /// <summary>True when the collider sits alone on an object Auto Collider made for it (it is moved / rotated through that transform).</summary>
        public static bool IsDedicatedChild(Collider2D collider, ColliderGroup group)
        {
            var t = collider.transform;
            if (t == group.transform || t == group.Container2D) return false;
            return group.TryGetEntry(collider, out var entry) && entry.OwnsGameObject;
        }

        /// <summary>Group-space 2D point of a world point.</summary>
        public static float2 ToScaled(in TargetSpace space, Vector3 world) => ColliderShapes.ToScaled(space, world).xy;

        /// <summary>World point of a group-space 2D point (in the group's XY plane, at the given local Z).</summary>
        public static Vector3 ToWorld(in TargetSpace space, float2 scaled, float z = 0f) => ColliderShapes.ToWorld(space, new float3(scaled, z));

        /// <summary>Z (in group space) of the collider's plane.</summary>
        public static float PlaneZ(Collider2D collider, in TargetSpace space) => ColliderShapes.ToScaled(space, collider.transform.position).z;

        /// <summary>Rotation of the collider's object around the group's Z axis, in degrees.</summary>
        public static float AngleOf(Collider2D collider, ColliderGroup group)
        {
            var relative = Quaternion.Inverse(group.transform.rotation) * collider.transform.rotation;
            var x = relative * Vector3.right;
            return math.degrees(math.atan2(x.y, x.x));
        }

        public static FittedShape2D ToShape(Collider2D collider, ColliderGroup group, in TargetSpace space)
        {
            var t = collider.transform;
            var scale = math.abs(((float3)t.lossyScale).xy);
            var angle = AngleOf(collider, group);
            var center = ToScaled(space, t.TransformPoint((Vector2)collider.offset));
            FittedShape2D shape;

            switch (collider)
            {
                case BoxCollider2D box:
                    shape = new FittedShape2D { Kind = ShapeKind2D.Box, Center = center, Size = (float2)box.size * scale, Angle = angle };
                    shape.Area = shape.Size.x * shape.Size.y;
                    break;
                case CircleCollider2D circle:
                    shape = new FittedShape2D { Kind = ShapeKind2D.Circle, Center = center, Radius = circle.radius * math.cmax(scale) };
                    shape.Area = math.PI * shape.Radius * shape.Radius;
                    break;
                case CapsuleCollider2D capsule:
                {
                    var size = (float2)capsule.size * scale;
                    var vertical = capsule.direction == CapsuleDirection2D.Vertical;
                    var radius = (vertical ? size.x : size.y) * 0.5f;
                    shape = new FittedShape2D
                    {
                        Kind = ShapeKind2D.Capsule, Center = center, Angle = angle, Vertical = vertical, Radius = radius,
                        Height = math.max(vertical ? size.y : size.x, 2f * radius),
                    };
                    shape.Area = 2f * radius * (shape.Height - 2f * radius) + math.PI * radius * radius;
                    break;
                }
                case PolygonCollider2D polygon:
                {
                    var paths = new float2[polygon.pathCount][];
                    for (var p = 0; p < paths.Length; p++) paths[p] = ToScaledPath(polygon.GetPath(p), collider, space);
                    shape = FittedShape2D.FromPaths(paths, false);
                    break;
                }
                case EdgeCollider2D edge:
                {
                    var points = edge.points;
                    // Closed loops repeat their first point; the shape keeps one copy.
                    var count = points.Length > 2 && points[0] == points[points.Length - 1] ? points.Length - 1 : points.Length;
                    var open = new Vector2[count];
                    System.Array.Copy(points, open, count);
                    shape = FittedShape2D.FromPaths(new[] { ToScaledPath(open, collider, space) }, true);
                    break;
                }
                default:
                    return null;
            }

            shape.Label = TryFind(collider, out _, out var entry) && !string.IsNullOrEmpty(entry.Label) ? entry.Label : collider.name;
            return shape;
        }

        private static float2[] ToScaledPath(Vector2[] points, Collider2D collider, in TargetSpace space)
        {
            var t = collider.transform;
            var result = new float2[points.Length];
            for (var i = 0; i < points.Length; i++) result[i] = ToScaled(space, t.TransformPoint(points[i] + collider.offset));
            return result;
        }

        /// <summary>Points on the shape's outline (primitives sampled), in group space.</summary>
        public static float2[] SurfacePoints(FittedShape2D shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                {
                    var (u, v) = Axes(shape.Angle);
                    var hu = u * shape.Size.x * 0.5f;
                    var hv = v * shape.Size.y * 0.5f;
                    return new[] { shape.Center - hu - hv, shape.Center + hu - hv, shape.Center + hu + hv, shape.Center - hu + hv };
                }
                case ShapeKind2D.Circle:
                {
                    var points = new float2[CircleSamples];
                    for (var i = 0; i < CircleSamples; i++)
                    {
                        var a = 2f * math.PI * i / CircleSamples;
                        points[i] = shape.Center + new float2(math.cos(a), math.sin(a)) * shape.Radius;
                    }
                    return points;
                }
                case ShapeKind2D.Capsule:
                    return ShapeDrawing2D.Capsule(shape);
                default:
                {
                    var points = new List<float2>();
                    foreach (var path in shape.Paths) points.AddRange(path);
                    return points.ToArray();
                }
            }
        }

        /// <summary>Point inside (or within <paramref name="tolerance"/> of) the shape. Edges count as their enclosed loop.</summary>
        public static bool Contains(FittedShape2D shape, float2 p, float tolerance)
        {
            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                {
                    var (u, v) = Axes(shape.Angle);
                    var d = p - shape.Center;
                    return math.abs(math.dot(d, u)) <= shape.Size.x * 0.5f + tolerance && math.abs(math.dot(d, v)) <= shape.Size.y * 0.5f + tolerance;
                }
                case ShapeKind2D.Circle:
                    return math.distance(p, shape.Center) <= shape.Radius + tolerance;
                case ShapeKind2D.Capsule:
                {
                    var axis = CapsuleAxis(shape);
                    var half = math.max(0f, shape.Height * 0.5f - shape.Radius);
                    var t = math.clamp(math.dot(p - shape.Center, axis), -half, half);
                    return math.distance(p, shape.Center + axis * t) <= shape.Radius + tolerance;
                }
                default:
                {
                    if (Polygon2D.Contains(shape.Paths, p)) return true;
                    foreach (var path in shape.Paths)
                        for (var i = 0; i < path.Length; i++)
                            if (Polygon2D.DistanceToSegment(p, path[i], path[(i + 1) % path.Length]) <= tolerance)
                                return true;
                    return false;
                }
            }
        }

        public static float2 CapsuleAxis(FittedShape2D shape)
        {
            var a = math.radians(shape.Angle + (shape.Vertical ? 90f : 0f));
            return new float2(math.cos(a), math.sin(a));
        }

        public static (float2 u, float2 v) Axes(float angleDegrees)
        {
            var r = math.radians(angleDegrees);
            var u = new float2(math.cos(r), math.sin(r));
            return (u, new float2(-u.y, u.x));
        }

        /// <summary>Mirror across the group's X (axis 0) or Y (axis 1) line through its origin; paths stay counter-clockwise.</summary>
        public static FittedShape2D Mirror(FittedShape2D shape, int axis)
        {
            var s = axis == 0 ? new float2(-1f, 1f) : new float2(1f, -1f);
            var mirrored = shape.Clone();
            mirrored.Label = ColliderShapes.MirrorLabel(shape.Label);
            mirrored.Center = shape.Center * s;
            // Reflecting a rotated rectangle / capsule negates its angle.
            mirrored.Angle = -shape.Angle;
            if (shape.Paths != null)
            {
                var paths = new float2[shape.Paths.Length][];
                for (var p = 0; p < paths.Length; p++)
                {
                    var path = shape.Paths[p];
                    var flipped = new float2[path.Length];
                    for (var i = 0; i < path.Length; i++) flipped[path.Length - 1 - i] = path[i] * s;
                    paths[p] = flipped;
                }
                mirrored.Paths = paths;
            }
            return mirrored;
        }

        /// <summary>Rotated boxes / capsules can't be represented under unequal X / Y scale: refit axis-aligned.</summary>
        public static FittedShape2D EnsureRepresentable(FittedShape2D shape, in TargetSpace space)
        {
            if (!shape.IsRotated || GenerationPipeline2D.IsUniform2D(space)) return shape;
            var points = SurfacePoints(shape);
            var fixedShape = shape.Kind == ShapeKind2D.Box
                ? FittedShape2D.FromBox(Box2DFitter.Aabb(points))
                : FittedShape2D.FromCapsule(Capsule2DFitter.Best(points, false));
            fixedShape.Label = shape.Label;
            return fixedShape;
        }
    }
}
