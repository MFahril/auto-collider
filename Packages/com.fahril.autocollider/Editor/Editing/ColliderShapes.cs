using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Bridges live collider components and <see cref="FittedShape"/>s in a group's scaled space, plus shape queries
    /// (containment, mirroring, sample points). Everything the edit tool does goes through here, so it is unit-testable.
    /// </summary>
    internal static class ColliderShapes
    {
        /// <summary>The group a collider belongs to (its entry may be null for untracked colliders).</summary>
        public static bool TryFind(Collider collider, out ColliderGroup group, out ColliderEntry entry)
        {
            entry = null;
            group = collider != null ? collider.GetComponentInParent<ColliderGroup>(true) : null;
            return group != null && group.TryGetEntry(collider, out entry);
        }

        public static TargetSpace SpaceOf(ColliderGroup group)
        {
            TargetSpace.TryCreate(group.transform, out var space, out _);
            return space;
        }

        /// <summary>True when the collider sits alone on its own child of the container (move / rotate the transform).</summary>
        public static bool IsDedicatedChild(Collider collider, ColliderGroup group)
        {
            var t = collider.transform;
            return group.Container != null && t != group.Container && t.parent == group.Container
                   && t.childCount == 0 && collider.GetComponents<Component>().Length == 2;
        }

        /// <summary>Describe a live collider as a shape in the group's scaled space (world-sized, group-oriented).</summary>
        public static FittedShape ToShape(Collider collider, ColliderGroup group, in TargetSpace space)
        {
            var t = collider.transform;
            var scale = math.abs((float3)t.lossyScale);
            var rotation = math.mul(math.inverse((quaternion)group.transform.rotation), (quaternion)t.rotation);
            FittedShape shape;

            switch (collider)
            {
                case BoxCollider box:
                    shape = new FittedShape { Kind = ShapeKind.Box, Size = (float3)box.size * scale };
                    shape.Center = ToScaled(space, t.TransformPoint(box.center));
                    shape.Rotation = rotation;
                    shape.Volume = shape.Size.x * shape.Size.y * shape.Size.z;
                    break;
                case SphereCollider sphere:
                    shape = new FittedShape { Kind = ShapeKind.Sphere, Radius = sphere.radius * math.cmax(scale) };
                    shape.Center = ToScaled(space, t.TransformPoint(sphere.center));
                    shape.Rotation = rotation;
                    shape.Volume = 4f / 3f * math.PI * shape.Radius * shape.Radius * shape.Radius;
                    break;
                case CapsuleCollider capsule:
                {
                    var d = capsule.direction;
                    var radius = capsule.radius * math.max(scale[(d + 1) % 3], scale[(d + 2) % 3]);
                    shape = new FittedShape
                    {
                        Kind = ShapeKind.Capsule,
                        Direction = d,
                        Radius = radius,
                        Height = math.max(capsule.height * scale[d], 2f * radius),
                        Center = ToScaled(space, t.TransformPoint(capsule.center)),
                        Rotation = rotation,
                    };
                    var length = shape.Height - 2f * radius;
                    shape.Volume = math.PI * radius * radius * length + 4f / 3f * math.PI * radius * radius * radius;
                    break;
                }
                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                {
                    MeshSource.ReadMesh(meshCollider.sharedMesh, out var vertices, out var triangles);
                    for (var i = 0; i < vertices.Length; i++) vertices[i] = ToScaled(space, t.TransformPoint(vertices[i]));
                    shape = meshCollider.convex
                        ? FittedShape.FromHull(new ConvexHull(vertices, triangles))
                        : FittedShape.FromMesh(new MeshGeometry(collider.name, vertices, triangles));
                    break;
                }
                default:
                    return null;
            }

            shape.Label = TryFind(collider, out _, out var entry) && !string.IsNullOrEmpty(entry.Label) ? entry.Label : collider.name;
            return shape;
        }

        /// <summary>Points describing the shape's surface (primitives are tessellated), in scaled space.</summary>
        public static float3[] SurfacePoints(FittedShape shape) => ShapeMesher.ToMesh(shape).Vertices;

        /// <summary>Point inside (or within <paramref name="tolerance"/> of) the shape.</summary>
        public static bool Contains(FittedShape shape, float3 p, float tolerance)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                    return math.all(math.abs(math.mul(math.inverse(shape.Rotation), p - shape.Center)) <= shape.Size * 0.5f + tolerance);
                case ShapeKind.Sphere:
                    return math.distance(p, shape.Center) <= shape.Radius + tolerance;
                case ShapeKind.Capsule:
                {
                    var axis = Axis(shape);
                    var half = math.max(0f, shape.Height * 0.5f - shape.Radius);
                    var t = math.clamp(math.dot(p - shape.Center, axis), -half, half);
                    return math.distance(p, shape.Center + axis * t) <= shape.Radius + tolerance;
                }
                case ShapeKind.ConvexMesh:
                    return shape.Hull.MaxPlaneDistance(p) <= tolerance;
                default:
                {
                    // Concave: treat its bounds as the volume (used only for coarse selection of points).
                    GeometryUtils.MinMax(shape.Mesh.Vertices, out var min, out var max);
                    return math.all(p >= min - tolerance) && math.all(p <= max + tolerance);
                }
            }
        }

        /// <summary>World-space capsule axis of a shape (unit).</summary>
        public static float3 Axis(FittedShape shape)
        {
            var axis = float3.zero;
            axis[shape.Direction] = 1f;
            return math.mul(shape.Rotation, axis);
        }

        /// <summary>
        /// Mirror a shape across the group's plane through its origin (normal = group axis 0/1/2).
        /// Rotations become S·R·S (a proper rotation); mesh triangles are re-wound.
        /// </summary>
        public static FittedShape Mirror(FittedShape shape, int axis)
        {
            var s = new float3(1f);
            s[axis] = -1f;
            var mirrored = new FittedShape
            {
                Kind = shape.Kind,
                Label = MirrorLabel(shape.Label),
                Center = shape.Center * s,
                Size = shape.Size,
                Radius = shape.Radius,
                Height = shape.Height,
                Direction = shape.Direction,
                Volume = shape.Volume,
            };

            var r = new float3x3(shape.Rotation);
            var sm = float3x3.Scale(s);
            mirrored.Rotation = math.normalize(new quaternion(math.mul(sm, math.mul(r, sm))));

            if (shape.IsMesh)
            {
                var vertices = shape.MeshVertices;
                var triangles = shape.MeshTriangles;
                var outVertices = new float3[vertices.Length];
                for (var i = 0; i < vertices.Length; i++) outVertices[i] = vertices[i] * s;
                var outTriangles = new int[triangles.Length];
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    outTriangles[i] = triangles[i];
                    outTriangles[i + 1] = triangles[i + 2];
                    outTriangles[i + 2] = triangles[i + 1];
                }
                if (shape.Kind == ShapeKind.ConvexMesh) mirrored.Hull = new ConvexHull(outVertices, outTriangles);
                else mirrored.Mesh = new MeshGeometry(mirrored.Label, outVertices, outTriangles);
                mirrored.Rotation = quaternion.identity;
            }
            return mirrored;
        }

        /// <summary>"Arm_L" ↔ "Arm_R", "Left Leg" ↔ "Right Leg"; otherwise appends " (Mirror)".</summary>
        public static string MirrorLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "Mirror";

            // Short side markers only count at the end ("Bracket_Large" must stay intact).
            foreach (var (a, b) in new[] { ("_L", "_R"), (".L", ".R"), (" L", " R"), ("_l", "_r") })
            {
                if (label.EndsWith(a)) return label.Substring(0, label.Length - a.Length) + b;
                if (label.EndsWith(b)) return label.Substring(0, label.Length - b.Length) + a;
            }
            // Whole words may appear anywhere.
            foreach (var (a, b) in new[] { ("Left", "Right"), ("left", "right"), ("LEFT", "RIGHT") })
            {
                if (label.Contains(a)) return label.Replace(a, b);
                if (label.Contains(b)) return label.Replace(b, a);
            }
            return label + " (Mirror)";
        }

        /// <summary>
        /// Rotated primitives can't be represented under non-uniform scale (they would shear): refit axis-aligned.
        /// </summary>
        public static FittedShape EnsureRepresentable(FittedShape shape, in TargetSpace space)
        {
            if (shape.IsMesh || space.IsUniform || !shape.IsRotated) return shape;
            if (shape.Kind == ShapeKind.Sphere)
            {
                shape.Rotation = quaternion.identity;
                return shape;
            }

            var points = SurfacePoints(shape);
            var label = shape.Label;
            var fixedShape = shape.Kind == ShapeKind.Box
                ? FittedShape.FromBox(BoxFitter.Aabb(points))
                : FittedShape.FromCapsule(CapsuleFitter.BestAxisAligned(points));
            fixedShape.Label = label;
            return fixedShape;
        }

        public static float3 ToScaled(in TargetSpace space, Vector3 world) => math.transform(space.WorldToScaled, world);

        public static Vector3 ToWorld(in TargetSpace space, float3 scaled) => math.transform(space.ScaledToWorld, scaled);

        /// <summary>All tracked, live colliders of the groups.</summary>
        public static IEnumerable<Collider> CollidersOf(IEnumerable<ColliderGroup> groups)
        {
            foreach (var group in groups)
            {
                if (group == null) continue;
                foreach (var entry in group.Entries)
                    if (entry.Collider != null) yield return entry.Collider;
            }
        }
    }
}
