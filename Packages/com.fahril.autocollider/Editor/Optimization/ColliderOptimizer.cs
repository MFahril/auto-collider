using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Optimizes <b>existing</b> MeshColliders in place (Optimize tab): analyze → apply (one Undo step) → restore original.
    /// Re-optimizing always starts from the original mesh, never from an already-optimized one.
    /// </summary>
    internal static class ColliderOptimizer
    {
        public sealed class Analysis
        {
            public MeshCollider Collider;
            /// <summary>The collider's mesh when it was analyzed; the analysis is stale once this changes.</summary>
            public Mesh AnalyzedMesh;
            /// <summary>Original mesh (the stored source if the collider already uses an optimized mesh).</summary>
            public Mesh SourceMesh;
            public bool IsOptimized;
            public TargetSpace Space;
            public MeshOptimizer.Result Result;
            public string Problem;

            public bool CanApply => Result != null && Problem == null && Result.Stats.ResultTriangles < Result.Stats.SourceTriangles;
        }

        /// <summary>MeshColliders with a mesh under the roots (or every loaded scene when <paramref name="roots"/> is null).</summary>
        public static List<MeshCollider> Collect(IEnumerable<GameObject> roots)
        {
            var result = new List<MeshCollider>();
            var seen = new HashSet<MeshCollider>();
            IEnumerable<MeshCollider> candidates;
            if (roots == null)
            {
                candidates = Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include);
            }
            else
            {
                var list = new List<MeshCollider>();
                foreach (var root in roots)
                    if (root != null) list.AddRange(root.GetComponentsInChildren<MeshCollider>(true));
                candidates = list;
            }

            foreach (var collider in candidates)
                if (collider != null && collider.sharedMesh != null && !EditorUtility.IsPersistent(collider) && seen.Add(collider))
                    result.Add(collider);

            result.Sort((a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
            return result;
        }

        /// <summary>Compute the optimized mesh and its statistics without changing anything.</summary>
        public static Analysis Analyze(MeshCollider collider, GenerationSettings settings)
        {
            var current = collider.sharedMesh;
            var analysis = new Analysis { Collider = collider, AnalyzedMesh = current };
            analysis.IsOptimized = GeneratedMeshStore.TryGetSource(current, out var source);
            analysis.SourceMesh = analysis.IsOptimized ? source : current;

            if (!TargetSpace.TryCreate(collider.transform, out var space, out var error))
            {
                analysis.Problem = error;
                return analysis;
            }
            analysis.Space = space;

            MeshSource.ReadMesh(analysis.SourceMesh, out var vertices, out var triangles);
            var scaled = new Unity.Mathematics.float3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) scaled[i] = vertices[i] * space.Scale;
            var geometry = new MeshGeometry(collider.name, scaled, triangles);

            if (collider.convex)
                analysis.Result = MeshOptimizer.OptimizeConvex(geometry, settings);
            else if (GenerationPipeline.IsDynamic(collider.gameObject))
                analysis.Problem = Strings.WarningDynamicConcave;
            else
                analysis.Result = MeshOptimizer.Optimize(geometry, settings);

            if (analysis.Result == null && analysis.Problem == null) analysis.Problem = Strings.ProblemHullFailed;
            return analysis;
        }

        /// <summary>Replace each collider's mesh with its optimized version (saved as an asset). One Undo step.</summary>
        public static int Apply(IReadOnlyList<Analysis> analyses, string meshFolder = null)
        {
            var store = new GeneratedMeshStore(meshFolder ?? AutoColliderSettings.instance.Data.GeneratedMeshFolder);
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(string.Format(Strings.UndoOptimizeCount, analyses.Count));

            var applied = 0;
            try
            {
                foreach (var analysis in analyses)
                {
                    if (analysis.Collider == null || !analysis.CanApply) continue;

                    var mesh = analysis.Result.Mesh;
                    var local = new Vector3[mesh.Vertices.Length];
                    for (var i = 0; i < local.Length; i++) local[i] = analysis.Space.ToLocalPoint(mesh.Vertices[i]);
                    // Only assets can be referenced from the mesh library; scene-only meshes rely on Undo instead.
                    var source = EditorUtility.IsPersistent(analysis.SourceMesh) ? analysis.SourceMesh : null;
                    var stored = store.GetOrCreate(LibraryName(analysis.Collider), analysis.SourceMesh.name, local,
                        mesh.Triangles, source);

                    Undo.RecordObject(analysis.Collider, Strings.UndoOptimize);
                    analysis.Collider.sharedMesh = stored;
                    RecordPrefab(analysis.Collider);
                    applied++;
                }
            }
            finally
            {
                store.Flush();
                Undo.CollapseUndoOperations(undoGroup);
            }
            return applied;
        }

        /// <summary>Put the original mesh back on colliders that use an optimized mesh. One Undo step.</summary>
        public static int RestoreOriginals(IReadOnlyList<MeshCollider> colliders)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Strings.UndoRestore);

            var restored = 0;
            foreach (var collider in colliders)
            {
                if (collider == null || !GeneratedMeshStore.TryGetSource(collider.sharedMesh, out var source)) continue;
                Undo.RecordObject(collider, Strings.UndoRestore);
                collider.sharedMesh = source;
                RecordPrefab(collider);
                restored++;
            }
            Undo.CollapseUndoOperations(undoGroup);
            return restored;
        }

        public static int CountOptimized(IEnumerable<MeshCollider> colliders)
        {
            var count = 0;
            foreach (var c in colliders)
                if (c != null && GeneratedMeshStore.TryGetSource(c.sharedMesh, out _)) count++;
            return count;
        }

        private static string LibraryName(Component collider)
        {
            var group = collider.GetComponentInParent<ColliderGroup>();
            return group != null ? group.name : collider.gameObject.name;
        }

        private static void RecordPrefab(Object obj)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
