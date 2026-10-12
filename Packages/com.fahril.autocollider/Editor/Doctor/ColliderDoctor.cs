using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Scene validator: finds collider setups that break or slow down physics and offers one-click fixes
    /// (each fix is one Undo step; <see cref="FixAll"/> is a single step for everything).
    /// </summary>
    internal static class ColliderDoctor
    {
        /// <summary>Folder for meshes written by fixes (tests); null = project setting.</summary>
        internal static string MeshFolderOverride;

        /// <summary>Scan options.</summary>
        internal readonly struct Options
        {
            public Options(bool checkMissingColliders, int triangleBudget)
            {
                CheckMissingColliders = checkMissingColliders;
                TriangleBudget = triangleBudget;
            }

            /// <summary>Also report visible meshes that have no collider at all.</summary>
            public bool CheckMissingColliders { get; }

            /// <summary>Concave mesh colliders above this triangle count are reported.</summary>
            public int TriangleBudget { get; }
        }

        /// <summary>Scan <paramref name="roots"/> (and their children), or every loaded scene when null. Sorted: errors first.</summary>
        public static List<DoctorIssue> Scan(IEnumerable<GameObject> roots, Options options)
        {
            var objects = Collect(roots);
            var issues = new List<DoctorIssue>();
            foreach (var go in objects)
            {
                foreach (var collider in go.GetComponents<Collider>()) CheckCollider(collider, options, issues);
                CheckDuplicates(go, issues);
                if (go.TryGetComponent<ColliderGroup>(out var group)) CheckGroup(group, issues);
                if (go.TryGetComponent<AnimatedMeshCollider>(out var animated)) CheckAnimated(animated, issues);
                if (options.CheckMissingColliders) CheckMissingCollider(go, issues);
            }

            var order = new Dictionary<GameObject, int>();
            for (var i = 0; i < objects.Count; i++) order[objects[i]] = i;
            issues.Sort((a, b) =>
            {
                var bySeverity = a.Severity.CompareTo(b.Severity);
                return bySeverity != 0 ? bySeverity : order[ObjectOf(a)].CompareTo(order[ObjectOf(b)]);
            });
            return issues;
        }

        /// <summary>Apply every available fix as one Undo step. Returns the number of fixes applied.</summary>
        public static int FixAll(IReadOnlyList<DoctorIssue> issues)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Strings.UndoDoctorFixAll);
            var fixedCount = 0;
            try
            {
                foreach (var issue in issues)
                {
                    if (!issue.CanFix || issue.Target == null) continue;
                    issue.Fix();
                    fixedCount++;
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
            return fixedCount;
        }

        /// <summary>Apply one issue's fix as its own Undo step.</summary>
        public static void FixOne(DoctorIssue issue)
        {
            if (!issue.CanFix || issue.Target == null) return;
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Strings.UndoDoctorFix);
            try
            {
                issue.Fix();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // ---------- Checks ----------

        private static void CheckCollider(Collider collider, Options options, List<DoctorIssue> issues)
        {
            switch (collider)
            {
                case MeshCollider mesh:
                    CheckMeshCollider(mesh, options, issues);
                    break;
                case SphereCollider or CapsuleCollider:
                    CheckScaledPrimitive(collider, issues);
                    break;
            }
        }

        private static void CheckMeshCollider(MeshCollider collider, Options options, List<DoctorIssue> issues)
        {
            var mesh = collider.sharedMesh;
            if (mesh == null)
            {
                var issue = new DoctorIssue(DoctorCheck.MissingMesh, DoctorSeverity.Warning, collider, Strings.DoctorMissingMesh);
                var filter = collider.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    issue.WithFix(Strings.DoctorFixAssignMesh, Strings.DoctorFixAssignMeshTooltip, () => AssignMesh(collider, filter.sharedMesh));
                else
                    issue.WithFix(Strings.DoctorFixRemove, Strings.DoctorFixRemoveTooltip, () => RemoveCollider(collider));
                issues.Add(issue);
                return;
            }

            // Unity never attaches a moving Rigidbody to a concave collider, so attachedRigidbody is null — look up the hierarchy.
            var body = collider.GetComponentInParent<Rigidbody>();
            if (!collider.convex && body != null && !body.isKinematic)
                issues.Add(new DoctorIssue(DoctorCheck.ConcaveOnMovingBody, DoctorSeverity.Error, collider, Strings.DoctorConcaveMoving)
                    .WithFix(Strings.DoctorFixConvex, Strings.DoctorFixConvexTooltip, () => MakeConvex(collider)));
            else if (!collider.convex && collider.isTrigger)
                issues.Add(new DoctorIssue(DoctorCheck.ConcaveTrigger, DoctorSeverity.Error, collider, Strings.DoctorConcaveTrigger)
                    .WithFix(Strings.DoctorFixConvex, Strings.DoctorFixConvexTooltip, () => MakeConvex(collider)));

            var triangles = TriangleCount(mesh);
            var canOptimize = collider.convex || body == null || body.isKinematic;
            if (!collider.convex && triangles > options.TriangleBudget && canOptimize)
                issues.Add(new DoctorIssue(DoctorCheck.DetailedMesh, DoctorSeverity.Warning, collider,
                        string.Format(CultureInfo.CurrentCulture, Strings.DoctorDetailedMesh, triangles, options.TriangleBudget))
                    .WithFix(Strings.DoctorFixOptimize, Strings.DoctorFixOptimizeTooltip, () => Optimize(collider)));
            else if (collider.convex && triangles > PhysicsLimits.MaxConvexPolygons)
                issues.Add(new DoctorIssue(DoctorCheck.ComplexConvex, DoctorSeverity.Info, collider,
                        string.Format(CultureInfo.CurrentCulture, Strings.DoctorComplexConvex, triangles))
                    .WithFix(Strings.DoctorFixOptimize, Strings.DoctorFixHullTooltip, () => Optimize(collider)));

            var scale = collider.transform.lossyScale;
            if (scale.x * scale.y * scale.z < 0f)
                issues.Add(new DoctorIssue(DoctorCheck.NegativeScale, DoctorSeverity.Info, collider, Strings.DoctorNegativeScale));
        }

        private static void CheckScaledPrimitive(Collider collider, List<DoctorIssue> issues)
        {
            // Our own colliders are fitted with the scale compensated; only user colliders can be surprising.
            var group = collider.GetComponentInParent<ColliderGroup>(true);
            if (group != null && group.TryGetEntry(collider, out var entry) && entry.Generated) return;

            var s = collider.transform.lossyScale;
            var abs = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            float largest, smallest;
            if (collider is CapsuleCollider capsule)
            {
                // Only the two radial axes matter for a capsule's radius.
                var d = capsule.direction;
                largest = Mathf.Max(abs[(d + 1) % 3], abs[(d + 2) % 3]);
                smallest = Mathf.Min(abs[(d + 1) % 3], abs[(d + 2) % 3]);
            }
            else
            {
                largest = Mathf.Max(abs.x, Mathf.Max(abs.y, abs.z));
                smallest = Mathf.Min(abs.x, Mathf.Min(abs.y, abs.z));
            }
            if (smallest <= 0f || largest / smallest < ScaledPrimitiveRatio) return;
            issues.Add(new DoctorIssue(DoctorCheck.ScaledPrimitive, DoctorSeverity.Info, collider,
                string.Format(CultureInfo.CurrentCulture, Strings.DoctorScaledPrimitive, largest / smallest)));
        }

        /// <summary>Scale ratio from which a sphere / capsule visibly outgrows the mesh.</summary>
        private const float ScaledPrimitiveRatio = 1.25f;

        private static void CheckDuplicates(GameObject go, List<DoctorIssue> issues)
        {
            var colliders = go.GetComponents<Collider>();
            if (colliders.Length < 2) return;
            var seen = new HashSet<string>();
            foreach (var collider in colliders)
            {
                var signature = Signature(collider);
                if (signature == null || seen.Add(signature)) continue;
                issues.Add(new DoctorIssue(DoctorCheck.DuplicateCollider, DoctorSeverity.Warning, collider, Strings.DoctorDuplicate)
                    .WithFix(Strings.DoctorFixRemoveDuplicate, Strings.DoctorFixRemoveDuplicateTooltip, () => RemoveCollider(collider)));
            }
        }

        private static void CheckGroup(ColliderGroup group, List<DoctorIssue> issues)
        {
            var missing = 0;
            foreach (var entry in group.Entries)
                if (entry.Collider == null) missing++;
            if (missing == 0) return;
            issues.Add(new DoctorIssue(DoctorCheck.MissingEntries, DoctorSeverity.Warning, group,
                    string.Format(CultureInfo.CurrentCulture, Strings.DoctorMissingEntries, missing))
                .WithFix(Strings.CleanUp, Strings.CleanUpTooltip, () =>
                {
                    ColliderWriter.RecordGroup(group, Strings.UndoDoctorFix);
                    group.RemoveMissing();
                }));
        }

        private static void CheckAnimated(AnimatedMeshCollider animated, List<DoctorIssue> issues)
        {
            if (animated.Source == null || animated.Target == null)
            {
                issues.Add(new DoctorIssue(DoctorCheck.AnimatedSetup, DoctorSeverity.Warning, animated, Strings.AnimatedMissing));
                return;
            }
            var mesh = animated.Source.sharedMesh;
            if (animated.Mode != AnimatedMeshSource.SharedMesh || mesh == null || mesh.isReadable) return;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) as ModelImporter;
            var issue = new DoctorIssue(DoctorCheck.AnimatedSetup, DoctorSeverity.Warning, animated, string.Format(Strings.AnimatedNotReadable, mesh.name));
            if (importer != null)
                issue.WithFix(Strings.AnimatedEnableReadWrite, Strings.AnimatedEnableReadWriteTooltip, () =>
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                });
            issues.Add(issue);
        }

        private static void CheckMissingCollider(GameObject go, List<DoctorIssue> issues)
        {
            if (!go.activeInHierarchy || !go.TryGetComponent<Renderer>(out var renderer) || !renderer.enabled) return;
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || MeshSource.SharedMesh(renderer) == null) return;
            for (var t = go.transform; t != null; t = t.parent)
                if (t.TryGetComponent<Collider>(out _) || t.TryGetComponent<ColliderGroup>(out _)) return;
            if (go.GetComponentInChildren<Collider>(true) != null) return;

            issues.Add(new DoctorIssue(DoctorCheck.NoCollider, DoctorSeverity.Info, go, Strings.DoctorNoCollider)
                .WithFix(Strings.DoctorFixGenerate, Strings.DoctorFixGenerateTooltip,
                    () => BatchProcessor.Generate(new[] { go }, GenerationSettingsState.instance.Settings, MeshFolderOverride, showProgress: false)));
        }

        // ---------- Fixes ----------

        private static void MakeConvex(MeshCollider collider)
        {
            Undo.RecordObject(collider, Strings.UndoDoctorFix);
            collider.convex = true;
            RecordPrefab(collider);
        }

        private static void AssignMesh(MeshCollider collider, Mesh mesh)
        {
            Undo.RecordObject(collider, Strings.UndoDoctorFix);
            collider.sharedMesh = mesh;
            RecordPrefab(collider);
        }

        private static void Optimize(MeshCollider collider)
        {
            var analysis = ColliderOptimizer.Analyze(collider, GenerationSettingsState.instance.Settings);
            if (analysis.CanApply) ColliderOptimizer.Apply(new[] { analysis }, MeshFolderOverride);
        }

        /// <summary>Destroy a collider (explicitly requested by the user), unregistering it from its group first.</summary>
        private static void RemoveCollider(Collider collider)
        {
            var group = collider.GetComponentInParent<ColliderGroup>(true);
            if (group != null && group.TryGetEntry(collider, out var entry))
            {
                ColliderWriter.RecordGroup(group, Strings.UndoDoctorFix);
                group.RemoveEntry(entry);
            }
            Undo.DestroyObjectImmediate(collider);
        }

        private static void RecordPrefab(Object o)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        // ---------- Helpers ----------

        private static List<GameObject> Collect(IEnumerable<GameObject> roots)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<GameObject>();
            void AddTree(GameObject root)
            {
                if (root == null || EditorUtility.IsPersistent(root)) return;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (seen.Add(t.gameObject)) result.Add(t.gameObject);
            }

            if (roots != null)
            {
                foreach (var root in roots) AddTree(root);
                return result;
            }
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) AddTree(root);
            }
            return result;
        }

        private static GameObject ObjectOf(DoctorIssue issue) => issue.Target switch
        {
            Component c => c.gameObject,
            GameObject g => g,
            _ => null,
        };

        internal static long TriangleCount(Mesh mesh)
        {
            long count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++) count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        private static string Signature(Collider c)
        {
            static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", v.x, v.y, v.z);
            var common = $"{c.isTrigger}|{c.enabled}|";
            return c switch
            {
                BoxCollider b => $"Box|{common}{V(b.center)}|{V(b.size)}",
                SphereCollider s => $"Sphere|{common}{V(s.center)}|{s.radius:R}",
                CapsuleCollider k => $"Capsule|{common}{V(k.center)}|{k.radius:R}|{k.height:R}|{k.direction}",
                MeshCollider m => m.sharedMesh == null ? null : $"Mesh|{common}{m.sharedMesh.GetEntityId()}|{m.convex}",
                _ => null,
            };
        }
    }
}
