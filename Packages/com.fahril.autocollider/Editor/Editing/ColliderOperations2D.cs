using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Edit operations on tracked 2D colliders. Called by <see cref="ColliderOperations"/> (which wraps each public operation
    /// in one Undo step and routes 3D colliders to the 3D code); point edits are called by the edit tool's handles.
    /// </summary>
    internal static class ColliderOperations2D
    {
        // ---------- Shape → collider ----------

        /// <summary>
        /// Write an edited shape back to its 2D collider: in place when possible, otherwise rebuild it and re-point the same
        /// entry. Not wrapped in its own Undo group (handles call it every drag step).
        /// </summary>
        public static Collider2D Apply(Collider2D collider, FittedShape2D shape, string undoName = null)
        {
            if (!ColliderShapes2D.TryFind(collider, out var group, out var entry)) return collider;
            undoName ??= Strings.UndoEditCollider;
            var space = ColliderShapes.SpaceOf(group);
            shape = ColliderShapes2D.EnsureRepresentable(shape, space);

            var result = TryUpdateInPlace(collider, shape, group, space, undoName) ? collider : Replace(collider, shape, group, entry, space);
            ColliderOperations.MarkEdited(group, entry);
            return result;
        }

        private static bool TryUpdateInPlace(Collider2D collider, FittedShape2D shape, ColliderGroup group, in TargetSpace space, string undoName)
        {
            if (!Matches(collider, shape.Kind)) return false;
            var dedicated = ColliderShapes2D.IsDedicatedChild(collider, group);
            // A collider on someone else's object keeps that object's rotation: fine as long as the shape keeps it too.
            var sameAngle = shape.Kind == ShapeKind2D.Circle
                            || math.abs(Mathf.DeltaAngle(shape.Angle, ColliderShapes2D.AngleOf(collider, group))) < 0.01f;
            if (!dedicated && !sameAngle && !shape.IsPolygonal) return false;

            var t = collider.transform;
            var z = ColliderShapes2D.PlaneZ(collider, space);
            Vector2 offset;
            if (dedicated && !shape.IsPolygonal)
            {
                Undo.RecordObject(t, undoName);
                t.SetPositionAndRotation(ColliderShapes2D.ToWorld(space, shape.Center, z), group.transform.rotation * Quaternion.Euler(0f, 0f, shape.Angle));
                offset = Vector2.zero;
            }
            else
            {
                offset = t.InverseTransformPoint(ColliderShapes2D.ToWorld(space, shape.Center, z));
            }

            var scale = math.abs(((float3)t.lossyScale).xy);
            Undo.RecordObject(collider, undoName);
            switch (collider)
            {
                case BoxCollider2D box:
                    box.offset = offset;
                    box.size = (Vector2)(shape.Size / scale);
                    return true;
                case CircleCollider2D circle:
                    circle.offset = offset;
                    circle.radius = shape.Radius / math.cmax(scale);
                    return true;
                case CapsuleCollider2D capsule:
                {
                    capsule.offset = offset;
                    capsule.direction = shape.Vertical ? CapsuleDirection2D.Vertical : CapsuleDirection2D.Horizontal;
                    var size = shape.Vertical ? new float2(2f * shape.Radius, shape.Height) : new float2(shape.Height, 2f * shape.Radius);
                    capsule.size = (Vector2)(size / scale);
                    return true;
                }
                case PolygonCollider2D polygon:
                    polygon.offset = Vector2.zero;
                {
                    var paths = new Vector2[shape.Paths.Length][];
                    for (var p = 0; p < paths.Length; p++) paths[p] = ToLocal(shape.Paths[p], t, space, z, false);
                    ColliderWriter2D.SetPaths(polygon, paths);
                    return true;
                }
                case EdgeCollider2D edge:
                    edge.offset = Vector2.zero;
                    edge.points = ToLocal(shape.Paths[0], t, space, z, true);
                    return true;
                default:
                    return false;
            }
        }

        private static Vector2[] ToLocal(float2[] path, Transform t, in TargetSpace space, float z, bool close)
        {
            var points = new Vector2[path.Length + (close ? 1 : 0)];
            for (var i = 0; i < path.Length; i++) points[i] = t.InverseTransformPoint(ColliderShapes2D.ToWorld(space, path[i], z));
            if (close) points[path.Length] = points[0];
            return points;
        }

        private static bool Matches(Collider2D collider, ShapeKind2D kind) => kind switch
        {
            ShapeKind2D.Box => collider is BoxCollider2D,
            ShapeKind2D.Circle => collider is CircleCollider2D,
            ShapeKind2D.Capsule => collider is CapsuleCollider2D,
            ShapeKind2D.Polygon => collider is PolygonCollider2D,
            _ => collider is EdgeCollider2D,
        };

        private static Collider2D Replace(Collider2D old, FittedShape2D shape, ColliderGroup group, ColliderEntry entry, in TargetSpace space)
        {
            shape.Label = entry.Label;
            // A bone collider stays on its bone (its dedicated object's parent is neither the group nor a container).
            var parent = old.transform.parent;
            if (ColliderShapes2D.IsDedicatedChild(old, group) && parent != null && parent != group.transform && parent != group.Container2D)
                shape = ColliderShapes2D.ToLocalOf(shape, parent, space, ColliderShapes2D.PlaneZ(old, space));
            var created = ColliderWriter2D.Create(shape, shape.Parent != null ? null : Writer().EnsureContainer2D(group), space);
            if (created == null) return old;
            created.enabled = old.enabled;
            created.isTrigger = old.isTrigger;
            created.sharedMaterial = old.sharedMaterial;

            var oldEntryState = new ColliderEntry(old, entry.Label, entry.PartId, entry.Generated, entry.OwnsGameObject);
            ColliderWriter.RecordGroup(group, Strings.UndoEditCollider); // after the create, right before the change
            entry.Collider2D = created;
            entry.OwnsGameObject = ColliderWriter.OwnsHost(created, group);
            ColliderWriter.DestroyEntryCollider(oldEntryState, group.Container2D);
            return created;
        }

        // ---------- Operations (the caller wraps them in one Undo group) ----------

        public static List<Component> Duplicate(IEnumerable<Collider2D> colliders)
        {
            var created = new List<Component>();
            var writer = Writer();
            foreach (var c in Valid(colliders))
            {
                ColliderShapes2D.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var shape = ColliderShapes2D.ToShape(c, group, space);
                shape.Label += " (Copy)";
                var entry = writer.AddShape2D(group, shape, space, c);
                if (entry == null) continue;
                ColliderOperations.MarkEdited(group, entry);
                created.Add(entry.Collider2D);
            }
            return created;
        }

        /// <summary>Mirrored copies across the group's X (0) or Y (1) axis; Z mirrors nothing in 2D.</summary>
        public static List<Component> Mirror(IEnumerable<Collider2D> colliders, int axis)
        {
            var created = new List<Component>();
            if (axis > 1) return created;
            var writer = Writer();
            foreach (var c in Valid(colliders))
            {
                ColliderShapes2D.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var mirrored = ColliderShapes2D.EnsureRepresentable(ColliderShapes2D.Mirror(ColliderShapes2D.ToShape(c, group, space), axis), space);
                var entry = writer.AddShape2D(group, mirrored, space, c);
                if (entry == null) continue;
                ColliderOperations.MarkEdited(group, entry);
                created.Add(entry.Collider2D);
            }
            return created;
        }

        /// <summary>Replace the colliders (per group) with one convex polygon or box around all of them.</summary>
        public static List<Component> Merge(IEnumerable<Collider2D> colliders, ColliderOperations.MergeKind kind)
        {
            var created = new List<Component>();
            var writer = Writer();
            foreach (var byGroup in Valid(colliders).GroupBy(c => c.GetComponentInParent<ColliderGroup>(true)).ToList())
            {
                var group = byGroup.Key;
                var members = byGroup.ToList();
                if (members.Count < 2) continue;
                var space = ColliderShapes.SpaceOf(group);

                var points = new List<float2>();
                foreach (var c in members) points.AddRange(ColliderShapes2D.SurfacePoints(ColliderShapes2D.ToShape(c, group, space)));
                var shape = FitAround(points.ToArray(), kind == ColliderOperations.MergeKind.Box ? ShapeKind2D.Box : ShapeKind2D.Polygon, space);
                shape.Label = Strings.MergedLabel;

                var entry = writer.AddShape2D(group, shape, space, members[0]);
                foreach (var c in members)
                {
                    ColliderShapes2D.TryFind(c, out _, out var old);
                    ColliderWriter.RecordGroup(group, Strings.UndoMerge);
                    group.RemoveEntry(old);
                    ColliderWriter.DestroyEntryCollider(old, group.Container2D);
                }
                if (entry == null) continue;
                ColliderOperations.MarkEdited(group, entry);
                created.Add(entry.Collider2D);
            }
            return created;
        }

        /// <summary>Refit each collider as another kind around its current outline (Polygon = convex polygon around it).</summary>
        public static List<Component> Convert(IEnumerable<Collider2D> colliders, ShapeKind2D kind)
        {
            var result = new List<Component>();
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes2D.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var shape = FitAround(ColliderShapes2D.SurfacePoints(ColliderShapes2D.ToShape(c, group, space)), kind, space);
                result.Add(Apply(c, shape, Strings.UndoConvert));
            }
            return result;
        }

        /// <summary>Shrink-wrap each collider (same kind) to the source outline points inside it (+10 % margin).</summary>
        public static int Refit(IEnumerable<Collider2D> colliders)
        {
            var count = 0;
            var sourceCache = new Dictionary<ColliderGroup, float2[]>();
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes2D.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                if (!sourceCache.TryGetValue(group, out var source)) sourceCache[group] = source = SourcePoints(group, space);

                var shape = ColliderShapes2D.ToShape(c, group, space);
                var outline = ColliderShapes2D.SurfacePoints(shape);
                var min = outline.Aggregate(new float2(float.MaxValue), math.min);
                var max = outline.Aggregate(new float2(float.MinValue), math.max);
                var margin = math.distance(min, max) * 0.1f;
                var inside = source.Where(p => ColliderShapes2D.Contains(shape, p, margin)).ToArray();
                if (inside.Length < 3) continue;

                var refit = FitAround(inside, shape.Kind, space, shape);
                Apply(c, refit, Strings.UndoRefit);
                count++;
            }
            return count;
        }

        public static void Inflate(IEnumerable<Collider2D> colliders, float amount)
        {
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes2D.TryFind(c, out var group, out _);
                var shape = ColliderShapes2D.ToShape(c, group, ColliderShapes.SpaceOf(group));
                GenerationPipeline2D.Inflate(shape, amount);
                Apply(c, shape, Strings.UndoInflate);
            }
        }

        // ---------- Polygon / edge points (handles) ----------

        /// <summary>Move one point of a polygon path / edge to a world position (not its own Undo group: called while dragging).</summary>
        public static void MovePoint(Collider2D collider, int path, int index, Vector3 world)
        {
            var points = GetPath(collider, path);
            if (points == null || index < 0 || index >= points.Length) return;
            Undo.RecordObject(collider, Strings.UndoMovePoint);
            var local = (Vector2)collider.transform.InverseTransformPoint(world) - collider.offset;
            points[index] = local;
            if (collider is EdgeCollider2D && IsClosed(collider, path) && index == 0) points[points.Length - 1] = local;
            SetPath(collider, path, points);
            MarkEdited(collider);
        }

        /// <summary>Insert a point after <paramref name="index"/> at a world position. One Undo step.</summary>
        public static void InsertPoint(Collider2D collider, int path, int index, Vector3 world) => Run(Strings.UndoAddPoint, () =>
        {
            var points = GetPath(collider, path)?.ToList();
            if (points == null) return;
            Undo.RecordObject(collider, Strings.UndoAddPoint);
            points.Insert(index + 1, (Vector2)collider.transform.InverseTransformPoint(world) - collider.offset);
            SetPath(collider, path, points.ToArray());
            MarkEdited(collider);
        });

        /// <summary>Remove a point (polygons keep at least 3, edges at least 2). One Undo step.</summary>
        public static void DeletePoint(Collider2D collider, int path, int index) => Run(Strings.UndoDeletePoint, () =>
        {
            var points = GetPath(collider, path)?.ToList();
            var minimum = collider is PolygonCollider2D ? 3 : 2;
            if (points == null || points.Count <= minimum || index < 0 || index >= points.Count) return;
            Undo.RecordObject(collider, Strings.UndoDeletePoint);
            var closed = IsClosed(collider, path);
            points.RemoveAt(index);
            if (closed && index == 0) points[points.Count - 1] = points[0];
            SetPath(collider, path, points.ToArray());
            MarkEdited(collider);
        });

        /// <summary>Simplify every path (Douglas–Peucker, tolerance in world units). One Undo step. Returns points removed.</summary>
        public static int Simplify(Collider2D collider, float tolerance) => Run(Strings.UndoSimplify, () =>
        {
            if (!ColliderShapes2D.TryFind(collider, out var group, out _)) return 0;
            var space = ColliderShapes.SpaceOf(group);
            var shape = ColliderShapes2D.ToShape(collider, group, space);
            if (shape == null || !shape.IsPolygonal) return 0;
            var before = shape.PointCount;
            var simplified = PolygonSimplifier.Simplify(shape.Paths, tolerance);
            if (simplified.Count == 0) return 0;
            shape.Paths = shape.Kind == ShapeKind2D.Edge ? new[] { simplified[0] } : simplified.ToArray();
            Apply(collider, shape, Strings.UndoSimplify);
            return before - shape.PointCount;
        });

        public static Vector2[] GetPath(Collider2D collider, int path) => collider switch
        {
            PolygonCollider2D polygon when path < polygon.pathCount => polygon.GetPath(path),
            EdgeCollider2D edge when path == 0 => edge.points,
            _ => null,
        };

        public static int PathCount(Collider2D collider) => collider switch
        {
            PolygonCollider2D polygon => polygon.pathCount,
            EdgeCollider2D => 1,
            _ => 0,
        };

        private static void SetPath(Collider2D collider, int path, Vector2[] points)
        {
            if (collider is PolygonCollider2D polygon) polygon.SetPath(path, points);
            else if (collider is EdgeCollider2D edge) edge.points = points;
        }

        /// <summary>An edge loop repeats its first point at the end.</summary>
        public static bool IsClosed(Collider2D collider, int path)
        {
            if (!(collider is EdgeCollider2D edge)) return false;
            var points = edge.points;
            return points.Length > 2 && points[0] == points[points.Length - 1];
        }

        private static void MarkEdited(Collider2D collider)
        {
            if (ColliderShapes2D.TryFind(collider, out var group, out var entry)) ColliderOperations.MarkEdited(group, entry);
        }

        // ---------- Helpers ----------

        /// <summary>A shape of <paramref name="kind"/> around the points (Polygon / Edge → convex polygon). Rotation only under equal X/Y scale.</summary>
        public static FittedShape2D FitAround(float2[] points, ShapeKind2D kind, in TargetSpace space, FittedShape2D keepOrientationOf = null)
        {
            var loop = Polygon2D.OrientByNesting(new[] { HullLoop(points) });
            var outline = new Outline2D("fit", loop);
            var context = new FitContext(GenerationPipeline2D.IsUniform2D(space));
            switch (kind)
            {
                case ShapeKind2D.Box:
                    if (keepOrientationOf != null) return FittedShape2D.FromBox(Box2DFitter.InFrame(points, keepOrientationOf.Angle));
                    return Strategies2D.Box(outline, context);
                case ShapeKind2D.Circle:
                    return FittedShape2D.FromCircle(CircleFitter.Welzl(points));
                case ShapeKind2D.Capsule:
                    if (keepOrientationOf != null)
                    {
                        var axis = ColliderShapes2D.CapsuleAxis(keepOrientationOf);
                        var refit = FittedShape2D.FromCapsule(Capsule2DFitter.Along(points, axis));
                        return refit;
                    }
                    return Strategies2D.Capsule(outline, context);
                default:
                    return FittedShape2D.FromPaths(loop.ToArray(), kind == ShapeKind2D.Edge);
            }
        }

        private static float2[] HullLoop(float2[] points)
        {
            var list = new List<double2>(points.Length);
            foreach (var p in points) list.Add(p);
            var hull = GeometryUtils.ConvexHull2D(list);
            var loop = new float2[hull.Count];
            for (var i = 0; i < loop.Length; i++) loop[i] = (float2)hull[i];
            return loop;
        }

        /// <summary>Outline points of the group's 2D sources (sprites, flat meshes), in its 2D space — for refit and vertex snapping.</summary>
        public static float2[] SourcePoints(ColliderGroup group, in TargetSpace space)
        {
            var settings = group.Recipe.Settings;
            var points = new List<float2>();
            foreach (var renderer in MeshSource.CollectRenderers(group.gameObject, settings, null, includeSprites: true))
            {
                if (!GenerationPipeline2D.Is2D(renderer, group.gameObject, settings)) continue;
                foreach (var loop in SpriteSource.Read(renderer, space, settings, null).Loops) points.AddRange(loop);
            }
            return points.ToArray();
        }

        private static ColliderWriter Writer()
        {
            var settings = AutoColliderSettings.instance.Data;
            return new ColliderWriter(new GeneratedMeshStore(ColliderOperations.MeshFolderOverride ?? settings.GeneratedMeshFolder),
                settings.ResolvedContainerName);
        }

        /// <summary>Shapes the tool can edit (tilemap and composite colliders are managed by Unity).</summary>
        public static bool IsEditable(Collider2D c) => c is BoxCollider2D || c is CircleCollider2D || c is CapsuleCollider2D || c is PolygonCollider2D || c is EdgeCollider2D;

        private static IEnumerable<Collider2D> Valid(IEnumerable<Collider2D> colliders) =>
            colliders.Where(c => c != null && IsEditable(c) && ColliderShapes2D.TryFind(c, out _, out _)).Distinct();

        private static void Run(string name, System.Action body)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                body();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static T Run<T>(string name, System.Func<T> body)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            try
            {
                return body();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
