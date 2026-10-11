using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Edit operations on tracked colliders. Every public operation is exactly one Undo step, keeps the
    /// <see cref="ColliderGroup"/> entries in sync, and (by default) locks what it touched so Regenerate keeps it.
    /// </summary>
    internal static class ColliderOperations
    {
        public enum MergeKind
        {
            ConvexHull,
            Box,
        }

        // ---------- Shape → collider ----------

        /// <summary>
        /// Write an edited shape back to its collider. Updates in place when possible; otherwise (kind change, rotation on
        /// the shared container, mesh shapes) rebuilds the collider and re-points the same entry at it.
        /// Not wrapped in its own Undo group: handles call it every drag step and Unity groups the drag.
        /// </summary>
        public static Collider Apply(Collider collider, FittedShape shape, string undoName = null)
        {
            if (!ColliderShapes.TryFind(collider, out var group, out var entry)) return collider;
            undoName ??= Strings.UndoEditCollider;
            var space = ColliderShapes.SpaceOf(group);
            shape = ColliderShapes.EnsureRepresentable(shape, space);

            var result = TryUpdateInPlace(collider, shape, group, space, undoName) ? collider : Replace(collider, shape, group, entry, space);
            MarkEdited(group, entry);
            return result;
        }

        private static bool TryUpdateInPlace(Collider collider, FittedShape shape, ColliderGroup group, in TargetSpace space, string undoName)
        {
            var dedicated = ColliderShapes.IsDedicatedChild(collider, group);
            var onContainer = collider.transform == group.Container;
            if (!dedicated && !(onContainer && !shape.IsRotated)) return false;

            var scale = math.abs(space.Scale);
            var uniform = scale.x;
            var center = space.ToLocalPoint(shape.Center);
            var t = collider.transform;

            if (dedicated)
            {
                Undo.RecordObject(t, undoName);
                t.localPosition = center;
                t.localRotation = shape.Rotation;
                center = float3.zero;
            }

            switch (collider)
            {
                case BoxCollider box when shape.Kind == ShapeKind.Box:
                    Undo.RecordObject(box, undoName);
                    box.center = center;
                    box.size = dedicated && shape.IsRotated ? shape.Size / uniform : space.ToLocalSize(shape.Size);
                    return true;
                case SphereCollider sphere when shape.Kind == ShapeKind.Sphere:
                    Undo.RecordObject(sphere, undoName);
                    sphere.center = center;
                    sphere.radius = shape.Radius / math.cmax(scale);
                    return true;
                case CapsuleCollider capsule when shape.Kind == ShapeKind.Capsule:
                {
                    var d = shape.Direction;
                    var rotated = dedicated && shape.IsRotated;
                    Undo.RecordObject(capsule, undoName);
                    capsule.center = center;
                    capsule.direction = d;
                    capsule.radius = shape.Radius / (rotated ? uniform : math.max(scale[(d + 1) % 3], scale[(d + 2) % 3]));
                    capsule.height = shape.Height / (rotated ? uniform : scale[d]);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Build a new collider for the shape, re-point the entry at it, delete the old one.</summary>
        private static Collider Replace(Collider old, FittedShape shape, ColliderGroup group, ColliderEntry entry, in TargetSpace space)
        {
            var writer = Writer(out var store);
            var container = writer.EnsureContainer(group);
            shape.Label = entry.Label;
            var created = writer.CreateCollider(shape, container, space, group.name);
            store.Flush();
            if (created == null) return old;

            CopySettings(old, created);
            ColliderWriter.RecordGroup(group, Strings.UndoEditCollider); // after the create, right before the change
            entry.Collider = created;
            ColliderWriter.DestroyCollider(old, group.Container);
            return created;
        }

        /// <summary>
        /// Move a collider that shares the container onto its own child (same world pose, same mesh asset) so it can be
        /// moved / rotated as a transform. Returns the (possibly new) collider. Not its own Undo group (used mid-drag).
        /// </summary>
        public static Collider DetachToChild(Collider collider)
        {
            if (!ColliderShapes.TryFind(collider, out var group, out var entry)) return collider;
            if (ColliderShapes.IsDedicatedChild(collider, group) || collider.transform != group.Container) return collider;

            var go = new GameObject(string.IsNullOrEmpty(entry.Label) ? collider.GetType().Name : entry.Label);
            go.transform.SetParent(group.Container, false);
            go.layer = group.Container.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(go, Strings.UndoEditCollider);

            var copy = (Collider)Undo.AddComponent(go, collider.GetType());
            EditorUtility.CopySerialized(collider, copy);
            Undo.RecordObject(group, Strings.UndoEditCollider);
            entry.Collider = copy;
            Undo.DestroyObjectImmediate(collider);
            MarkEdited(group, entry);
            EditorUtility.SetDirty(group);
            return copy;
        }

        // ---------- Operations (one Undo step each) ----------

        public static List<Collider> Duplicate(IEnumerable<Collider> colliders) => Run(Strings.UndoDuplicate, () =>
        {
            var created = new List<Collider>();
            var writer = Writer(out var store);
            foreach (var c in Valid(colliders))
            {
                ColliderShapes.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var shape = ColliderShapes.ToShape(c, group, space);
                shape.Label += " (Copy)";
                var entry = writer.AddShape(group, shape, space, c);
                if (entry == null) continue;
                MarkEdited(group, entry);
                created.Add(entry.Collider);
            }
            store.Flush();
            return created;
        });

        public static int Delete(IEnumerable<Collider> colliders) => Run(Strings.UndoDelete, () =>
        {
            var count = 0;
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes.TryFind(c, out var group, out var entry);
                ColliderWriter.RecordGroup(group, Strings.UndoDelete);
                group.RemoveEntry(entry);
                ColliderWriter.DestroyCollider(c, group.Container);
                count++;
            }
            return count;
        });

        /// <summary>Mirrored copies across the group's plane through its origin (normal = group X / Y / Z).</summary>
        public static List<Collider> Mirror(IEnumerable<Collider> colliders, int axis) => Run(Strings.UndoMirror, () =>
        {
            var created = new List<Collider>();
            var writer = Writer(out var store);
            foreach (var c in Valid(colliders))
            {
                ColliderShapes.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var mirrored = ColliderShapes.EnsureRepresentable(ColliderShapes.Mirror(ColliderShapes.ToShape(c, group, space), axis), space);
                var entry = writer.AddShape(group, mirrored, space, c);
                if (entry == null) continue;
                MarkEdited(group, entry);
                created.Add(entry.Collider);
            }
            store.Flush();
            return created;
        });

        /// <summary>Replace the colliders (per group) with one convex hull or box around all of them.</summary>
        public static List<Collider> Merge(IEnumerable<Collider> colliders, MergeKind kind) => Run(Strings.UndoMerge, () =>
        {
            var created = new List<Collider>();
            var writer = Writer(out var store);
            foreach (var byGroup in Valid(colliders).GroupBy(c => c.GetComponentInParent<ColliderGroup>(true)).ToList())
            {
                var group = byGroup.Key;
                var members = byGroup.ToList();
                if (members.Count < 2) continue;
                var space = ColliderShapes.SpaceOf(group);

                var points = new List<float3>();
                foreach (var c in members) points.AddRange(ColliderShapes.SurfacePoints(ColliderShapes.ToShape(c, group, space)));
                var input = FitInput.Create(new MeshGeometry("merge", points.ToArray(), new int[0]));
                var context = new FitContext(space.IsUniform);
                var shape = kind == MergeKind.Box ? BoxStrategy.FitBox(input, context) : new ConvexHullStrategy().Fit(input, context)[0];
                shape.Label = Strings.MergedLabel;

                var entry = writer.AddShape(group, shape, space, members[0]);
                foreach (var c in members)
                {
                    ColliderShapes.TryFind(c, out _, out var old);
                    ColliderWriter.RecordGroup(group, Strings.UndoMerge);
                    group.RemoveEntry(old);
                    ColliderWriter.DestroyCollider(c, group.Container);
                }
                if (entry == null) continue;
                MarkEdited(group, entry);
                created.Add(entry.Collider);
            }
            store.Flush();
            return created;
        });

        /// <summary>Refit each collider as another kind around its current shape.</summary>
        public static List<Collider> Convert(IEnumerable<Collider> colliders, ShapeKind kind) => Run(Strings.UndoConvert, () =>
        {
            var result = new List<Collider>();
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                var current = ColliderShapes.ToShape(c, group, space);
                var shape = FitAround(ColliderShapes.SurfacePoints(current), kind, new FitContext(space.IsUniform));
                result.Add(Apply(c, shape, Strings.UndoConvert));
            }
            return result;
        });

        /// <summary>
        /// Shrink-wrap: refit each collider (same kind and orientation) to the source-mesh vertices inside it
        /// (plus a 10% margin). Colliders with fewer than 4 vertices inside are left alone.
        /// </summary>
        public static int Refit(IEnumerable<Collider> colliders) => Run(Strings.UndoRefit, () =>
        {
            var count = 0;
            var sourceCache = new Dictionary<ColliderGroup, float3[]>();
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes.TryFind(c, out var group, out _);
                var space = ColliderShapes.SpaceOf(group);
                if (!sourceCache.TryGetValue(group, out var source))
                    sourceCache[group] = source = SourceVertices(group, space);

                var shape = ColliderShapes.ToShape(c, group, space);
                GeometryUtils.MinMax(ColliderShapes.SurfacePoints(shape), out var min, out var max);
                var margin = math.length(max - min) * 0.1f;
                var inside = source.Where(p => ColliderShapes.Contains(shape, p, margin)).ToArray();
                if (inside.Length < PhysicsLimits.MinHullVertices) continue;

                var refit = RefitSameKind(shape, inside);
                if (refit == null) continue;
                Apply(c, refit, Strings.UndoRefit);
                count++;
            }
            return count;
        });

        /// <summary>Grow (+) or shrink (−) each collider along its surface.</summary>
        public static void Inflate(IEnumerable<Collider> colliders, float amount) => Run(Strings.UndoInflate, () =>
        {
            foreach (var c in Valid(colliders).ToList())
            {
                ColliderShapes.TryFind(c, out var group, out _);
                var shape = ColliderShapes.ToShape(c, group, ColliderShapes.SpaceOf(group));
                InflateDeflate.Apply(shape, amount);
                Apply(c, shape, Strings.UndoInflate);
            }
            return 0;
        });

        public static void SetLocked(IEnumerable<Collider> colliders, bool locked) => Run(Strings.UndoLock, () =>
        {
            foreach (var c in Valid(colliders))
            {
                ColliderShapes.TryFind(c, out var group, out var entry);
                Undo.RecordObject(group, Strings.UndoLock);
                entry.Locked = locked;
                EditorUtility.SetDirty(group);
            }
            return 0;
        });

        public static void Rename(Collider collider, string label) => Run(Strings.UndoRename, () =>
        {
            if (!ColliderShapes.TryFind(collider, out var group, out var entry) || string.IsNullOrWhiteSpace(label)) return 0;
            Undo.RecordObject(group, Strings.UndoRename);
            entry.Label = label.Trim();
            EditorUtility.SetDirty(group);
            if (ColliderShapes.IsDedicatedChild(collider, group))
            {
                Undo.RecordObject(collider.gameObject, Strings.UndoRename);
                collider.gameObject.name = entry.Label;
            }
            return 0;
        });

        // ---------- Helpers ----------

        public static FittedShape FitAround(float3[] points, ShapeKind kind, FitContext context)
        {
            var input = FitInput.Create(new MeshGeometry("fit", points, new int[0]));
            return kind switch
            {
                ShapeKind.Box => BoxStrategy.FitBox(input, context),
                ShapeKind.Sphere => SphereStrategy.FitSphere(input),
                ShapeKind.Capsule => CapsuleStrategy.FitCapsule(input, context),
                _ => new ConvexHullStrategy().Fit(input, context)[0],
            };
        }

        private static FittedShape RefitSameKind(FittedShape shape, float3[] points)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                {
                    var box = BoxFitter.InFrame(points, new float3x3(shape.Rotation));
                    return FittedShape.FromBox(box);
                }
                case ShapeKind.Sphere:
                    return FittedShape.FromSphere(SphereFitter.Welzl(points));
                case ShapeKind.Capsule:
                {
                    var fit = CapsuleFitter.AlongDirection(points, ColliderShapes.Axis(shape));
                    fit.Rotation = shape.Rotation;
                    fit.Direction = shape.Direction;
                    return FittedShape.FromCapsule(fit);
                }
                case ShapeKind.ConvexMesh:
                    return QuickHull3D.TryBuildCapped(points, PhysicsLimits.MaxConvexPolygons, out var hull) ? FittedShape.FromHull(hull) : null;
                default:
                    return null;
            }
        }

        /// <summary>Vertices of the group's source meshes (recipe filters), in its scaled space.</summary>
        public static float3[] SourceVertices(ColliderGroup group, in TargetSpace space)
        {
            var points = new List<float3>();
            foreach (var renderer in MeshSource.CollectRenderers(group.gameObject, group.Recipe.Settings, null))
                points.AddRange(MeshSource.Read(renderer, space).Vertices);
            return points.ToArray();
        }

        internal static void MarkEdited(ColliderGroup group, ColliderEntry entry)
        {
            if (entry == null || entry.Locked || !AutoColliderSettings.instance.Data.LockAfterEdit) return;
            Undo.RecordObject(group, Strings.UndoEditCollider);
            entry.Locked = true;
            EditorUtility.SetDirty(group);
        }

        private static void CopySettings(Collider from, Collider to)
        {
            to.enabled = from.enabled;
            if (!(to is MeshCollider { convex: false })) to.isTrigger = from.isTrigger;
            to.sharedMaterial = from.sharedMaterial;
        }

        private static IEnumerable<Collider> Valid(IEnumerable<Collider> colliders) =>
            colliders.Where(c => c != null && ColliderShapes.TryFind(c, out _, out _)).Distinct();

        /// <summary>Folder for meshes the operations create; null = Project Settings folder (tests point it at a temp folder).</summary>
        internal static string MeshFolderOverride;

        private static ColliderWriter Writer(out GeneratedMeshStore store)
        {
            var settings = AutoColliderSettings.instance.Data;
            store = new GeneratedMeshStore(MeshFolderOverride ?? settings.GeneratedMeshFolder);
            return new ColliderWriter(store, settings.ResolvedContainerName);
        }

        private static T Run<T>(string name, Func<T> body)
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
