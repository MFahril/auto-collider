using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Apply stage: writes fitted shapes into the scene as collider components, fully Undo-able.
    /// Axis-aligned primitives and meshes go on the <c>[Colliders]</c> container; rotated primitives get their own child.
    /// Callers group several applies into one Undo step (see <see cref="BatchProcessor"/>).
    /// </summary>
    internal sealed class ColliderWriter
    {
        private readonly GeneratedMeshStore _meshStore;
        private readonly string _containerName;

        public ColliderWriter(GeneratedMeshStore meshStore, string containerName)
        {
            _meshStore = meshStore;
            _containerName = containerName;
        }

        /// <summary>Write <paramref name="result"/> to its target. Updates the result's created/removed counts.</summary>
        public void Apply(TargetResult result, GenerationSettings settings)
        {
            var target = result.Target;
            if (target == null || result.Shapes.Count == 0) return;

            var group = target.GetComponent<ColliderGroup>();
            if (group == null) group = Undo.AddComponent<ColliderGroup>(target);

            result.CollidersRemoved += settings.Replace switch
            {
                ReplaceMode.ReplaceGenerated => RemoveEntries(group, e => e.Generated && !e.Locked),
                ReplaceMode.ReplaceAll => RemoveAllExceptLocked(group),
                _ => 0,
            };
            RecordGroup(group, Strings.UndoGenerate);
            group.RemoveMissing();

            // A locked collider built from a painted region replaces that region's regenerated colliders.
            var lockedRegions = new HashSet<int>();
            foreach (var entry in group.Entries)
                if (entry.Locked && group.Recipe.IsRegionId(entry.PartId)) lockedRegions.Add(entry.PartId);

            var space = result.Space;
            var libraryName = target.name;

            for (var i = 0; i < result.Shapes.Count; i++)
            {
                var shape = result.Shapes[i];
                if (shape.RegionId >= 0 && lockedRegions.Contains(shape.RegionId)) continue;

                var container = shape.Parent != null ? null : EnsureContainer(group);
                var collider = CreateCollider(shape, container, space, libraryName);
                if (collider == null) continue;

                if (!(collider is MeshCollider { convex: false })) collider.isTrigger = settings.IsTrigger;
                collider.sharedMaterial = settings.Material;
                // Per-LOD colliders start as LOD 0 shows; the LOD follower switches them at runtime.
                if (shape.Lod > 0) collider.enabled = false;
                RecordGroup(group, Strings.UndoGenerate);
                var partId = shape.RegionId >= 0 ? shape.RegionId : i;
                group.AddEntry(new ColliderEntry(collider, shape.Label, partId, generated: true, OwnsHost(collider, group)) { Lod = shape.Lod });
                result.CollidersCreated++;
            }

            RecordGroup(group, Strings.UndoGenerate);
            group.Recipe.Settings = settings.Clone();
            EditorUtility.SetDirty(group);
            if (PrefabUtility.IsPartOfPrefabInstance(group))
                PrefabUtility.RecordPrefabInstancePropertyModifications(group);

            if (result.LodGroup != null) EnsureLodFollower(group, result.LodGroup);
        }

        private static void EnsureLodFollower(ColliderGroup group, LODGroup lodGroup)
        {
            if (!group.TryGetComponent<ColliderLodFollower>(out var follower))
                follower = Undo.AddComponent<ColliderLodFollower>(group.gameObject);
            Undo.RecordObject(follower, Strings.UndoGenerate);
            follower.LodGroup = lodGroup;
            if (PrefabUtility.IsPartOfPrefabInstance(follower))
                PrefabUtility.RecordPrefabInstancePropertyModifications(follower);
        }

        /// <summary>
        /// Create one collider for <paramref name="shape"/> in <paramref name="group"/> and register it (used by the edit tool).
        /// Settings such as trigger / material are copied from <paramref name="template"/> when given.
        /// </summary>
        public ColliderEntry AddShape(ColliderGroup group, FittedShape shape, in TargetSpace space, Collider template = null)
        {
            var container = shape.Parent != null ? null : EnsureContainer(group);
            var collider = CreateCollider(shape, container, space, group.name);
            if (collider == null) return null;

            if (template != null)
            {
                collider.enabled = template.enabled;
                if (!(collider is MeshCollider { convex: false })) collider.isTrigger = template.isTrigger;
                collider.sharedMaterial = template.sharedMaterial;
            }

            var entry = new ColliderEntry(collider, shape.Label, shape.RegionId, generated: true, OwnsHost(collider, group));
            RecordGroup(group, Strings.UndoEditCollider);
            group.AddEntry(entry);
            EditorUtility.SetDirty(group);
            return entry;
        }

        /// <summary>Remove generated, unlocked colliders. Removes the container and the group when nothing is left.</summary>
        public static int RemoveGenerated(ColliderGroup group)
        {
            if (group == null) return 0;
            var removed = RemoveEntries(group, e => e.Generated && !e.Locked);
            RecordGroup(group, Strings.UndoRemove);
            group.RemoveMissing();

            if (group.Entries.Count == 0)
            {
                var container = group.Container;
                if (container != null && IsDisposableObject(container.gameObject))
                    Undo.DestroyObjectImmediate(container.gameObject);
                Undo.DestroyObjectImmediate(group);
            }
            else
            {
                EditorUtility.SetDirty(group);
            }
            return removed;
        }

        /// <summary>True when the collider got a GameObject of its own (not the shared container).</summary>
        internal static bool OwnsHost(Collider collider, ColliderGroup group) =>
            collider != null && collider.transform != group.Container && collider.transform != group.transform;

        internal Collider CreateCollider(FittedShape shape, Transform container, in TargetSpace space, string libraryName)
        {
            if (shape.Parent != null) return CreateParentedCollider(shape, libraryName);

            // Rotation is only produced for uniformly scaled targets, so dividing by any scale component is exact.
            var host = container.gameObject;
            var center = space.ToLocalPoint(shape.Center);
            var uniformScale = math.abs(space.Scale.x);

            if (!shape.IsMesh && shape.IsRotated)
            {
                host = CreateChild(container, shape.Label);
                host.transform.localPosition = center;
                host.transform.localRotation = shape.Rotation;
                center = float3.zero;
            }

            switch (shape.Kind)
            {
                case ShapeKind.Box:
                {
                    var box = Undo.AddComponent<BoxCollider>(host);
                    box.center = center;
                    box.size = shape.IsRotated ? shape.Size / uniformScale : space.ToLocalSize(shape.Size);
                    return box;
                }
                case ShapeKind.Sphere:
                {
                    var sphere = Undo.AddComponent<SphereCollider>(host);
                    sphere.center = center;
                    sphere.radius = shape.Radius / math.cmax(math.abs(space.Scale));
                    return sphere;
                }
                case ShapeKind.Capsule:
                {
                    var capsule = Undo.AddComponent<CapsuleCollider>(host);
                    var d = shape.Direction;
                    var scale = math.abs(space.Scale);
                    var radialScale = shape.IsRotated ? uniformScale : math.max(scale[(d + 1) % 3], scale[(d + 2) % 3]);
                    var axialScale = shape.IsRotated ? uniformScale : scale[d];
                    capsule.center = center;
                    capsule.direction = d;
                    capsule.radius = shape.Radius / radialScale;
                    capsule.height = shape.Height / axialScale;
                    return capsule;
                }
                case ShapeKind.ConvexMesh:
                case ShapeKind.ConcaveMesh:
                {
                    var source = shape.MeshVertices;
                    var vertices = new Vector3[source.Length];
                    for (var v = 0; v < vertices.Length; v++) vertices[v] = space.ToLocalPoint(source[v]);
                    var mesh = _meshStore.GetOrCreate(libraryName, shape.Label, vertices, shape.MeshTriangles);

                    var meshCollider = Undo.AddComponent<MeshCollider>(host);
                    meshCollider.sharedMesh = mesh;
                    meshCollider.convex = shape.Kind == ShapeKind.ConvexMesh;
                    return meshCollider;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Bone colliders: a child of the bone at the shape's pose (bone-local space), sizes in bone-local units,
        /// so the collider follows the animation with no runtime code.
        /// </summary>
        private Collider CreateParentedCollider(FittedShape shape, string libraryName)
        {
            var host = CreateChild(shape.Parent, shape.Label);
            var isMesh = shape.IsMesh;
            host.transform.localPosition = isMesh ? Vector3.zero : (Vector3)(float3)shape.Center;
            host.transform.localRotation = isMesh ? Quaternion.identity : (Quaternion)shape.Rotation;

            switch (shape.Kind)
            {
                case ShapeKind.Box:
                {
                    var box = Undo.AddComponent<BoxCollider>(host);
                    box.size = shape.Size;
                    return box;
                }
                case ShapeKind.Sphere:
                {
                    var sphere = Undo.AddComponent<SphereCollider>(host);
                    sphere.radius = shape.Radius;
                    return sphere;
                }
                case ShapeKind.Capsule:
                {
                    var capsule = Undo.AddComponent<CapsuleCollider>(host);
                    capsule.direction = shape.Direction;
                    capsule.radius = shape.Radius;
                    capsule.height = shape.Height;
                    return capsule;
                }
                default:
                {
                    var source = shape.MeshVertices;
                    var vertices = new Vector3[source.Length];
                    for (var v = 0; v < vertices.Length; v++) vertices[v] = source[v];
                    var meshCollider = Undo.AddComponent<MeshCollider>(host);
                    meshCollider.sharedMesh = _meshStore.GetOrCreate(libraryName, shape.Label, vertices, shape.MeshTriangles);
                    meshCollider.convex = true;
                    return meshCollider;
                }
            }
        }

        internal Transform EnsureContainer(ColliderGroup group)
        {
            if (group.Container != null) return group.Container;

            var target = group.gameObject;
            var go = new GameObject(_containerName);
            go.transform.SetParent(target.transform, false);
            go.layer = target.layer;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(target));
            Undo.RegisterCreatedObjectUndo(go, Strings.UndoGenerate);
            RecordGroup(group, Strings.UndoGenerate);
            group.Container = go.transform;
            return go.transform;
        }

        /// <summary>
        /// Record the group right before changing it. Must come <b>after</b> any other Undo call (create / add / destroy):
        /// those flush pending records, so a record taken earlier would capture "no change" and the edit would be lost.
        /// </summary>
        internal static void RecordGroup(ColliderGroup group, string undoName)
        {
            Undo.RecordObject(group, undoName);
            EditorUtility.SetDirty(group);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Collider" : name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(parent.gameObject));
            Undo.RegisterCreatedObjectUndo(go, Strings.UndoGenerate);
            return go;
        }

        private static int RemoveEntries(ColliderGroup group, System.Predicate<ColliderEntry> match)
        {
            var toRemove = new List<ColliderEntry>();
            foreach (var entry in group.Entries)
                if (match(entry)) toRemove.Add(entry);

            var removed = 0;
            foreach (var entry in toRemove)
            {
                // Change the group first (recorded), then destroy — the destroy would flush an earlier record.
                RecordGroup(group, Strings.UndoRemove);
                group.RemoveEntry(entry);
                if (DestroyEntryCollider(entry, group.Container)) removed++;
            }
            return removed;
        }

        private static int RemoveAllExceptLocked(ColliderGroup group)
        {
            var removed = RemoveEntries(group, e => !e.Locked);

            var locked = new HashSet<Collider>();
            foreach (var entry in group.Entries)
                if (entry.Collider != null) locked.Add(entry.Collider);

            foreach (var collider in group.GetComponentsInChildren<Collider>(true))
                if (!locked.Contains(collider) && DestroyCollider(collider, group.Container))
                    removed++;
            return removed;
        }

        /// <summary>Destroy a collider; also its GameObject when that is a dedicated child of the container.</summary>
        internal static bool DestroyCollider(Collider collider, Transform container)
        {
            if (collider == null) return false;
            var go = collider.gameObject;
            Undo.DestroyObjectImmediate(collider);

            if (container != null && go != null && go.transform != container
                && go.transform.IsChildOf(container) && IsDisposableObject(go))
                Undo.DestroyObjectImmediate(go);
            return true;
        }

        /// <summary>Destroy an entry's collider, and its GameObject when Auto Collider created that object for it (e.g. under a bone).</summary>
        internal static bool DestroyEntryCollider(ColliderEntry entry, Transform container)
        {
            var collider = entry.Collider;
            if (collider == null) return false;
            var go = collider.gameObject;
            if (!entry.OwnsGameObject) return DestroyCollider(collider, container);

            Undo.DestroyObjectImmediate(collider);
            if (go != null && IsDisposableObject(go)) Undo.DestroyObjectImmediate(go);
            return true;
        }

        /// <summary>True when the object holds only a Transform and has no children (safe to delete).</summary>
        internal static bool IsDisposableObject(GameObject go)
        {
            return go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1;
        }
    }
}
