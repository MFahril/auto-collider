using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Summary of a batch run.</summary>
    internal sealed class BatchReport
    {
        public List<TargetResult> Results { get; } = new();
        public int CollidersCreated { get; set; }
        public int CollidersRemoved { get; set; }
        public int TargetsChanged { get; set; }
        public double Milliseconds { get; set; }
        public bool Canceled { get; set; }

        /// <summary>Accumulated mesh-collider statistics (a field so it can be added to in place).</summary>
        public ColliderStats Stats;

        public int Count(TargetStatus status) => Results.FindAll(r => r.Status == status).Count;
    }

    /// <summary>
    /// Runs generation / removal over many targets as <b>one Undo step</b>, with a cancelable progress bar.
    /// </summary>
    internal static class BatchProcessor
    {
        /// <summary>Generate colliders for every target.</summary>
        /// <param name="meshFolder">Folder for generated meshes; defaults to the project setting.</param>
        public static BatchReport Generate(IReadOnlyList<GameObject> targets, GenerationSettings settings,
            string meshFolder = null, bool showProgress = true)
        {
            var report = new BatchReport();
            var timer = Stopwatch.StartNew();
            var projectSettings = AutoColliderSettings.instance.Data;
            var store = new GeneratedMeshStore(meshFolder ?? projectSettings.GeneratedMeshFolder);
            var writer = new ColliderWriter(store, projectSettings.ResolvedContainerName);

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(string.Format(Strings.UndoGenerateCount, targets.Count));

            using var fitCache = FitCache.Scope();
            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar(Strings.ProgressGenerateTitle,
                            target != null ? target.name : string.Empty, (float)i / targets.Count))
                    {
                        report.Canceled = true;
                        break;
                    }

                    var result = GenerationPipeline.Compute(target, settings);
                    if (result.Status != TargetStatus.Skipped)
                    {
                        writer.Apply(result, settings);
                        report.TargetsChanged++;
                    }
                    if (result.HasMeshStats) report.Stats.Add(result.Stats);
                    report.CollidersCreated += result.CollidersCreated;
                    report.CollidersRemoved += result.CollidersRemoved;
                    report.Results.Add(result);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
                store.Flush();
                Undo.CollapseUndoOperations(undoGroup);
                report.Milliseconds = timer.Elapsed.TotalMilliseconds;
            }
            return report;
        }

        /// <summary>Remove generated (unlocked) colliders from every target.</summary>
        public static BatchReport RemoveGenerated(IReadOnlyList<GameObject> targets)
        {
            var report = new BatchReport();
            var timer = Stopwatch.StartNew();

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(string.Format(Strings.UndoRemoveCount, targets.Count));

            foreach (var target in targets)
            {
                var result = new TargetResult(target);
                var group = target != null ? target.GetComponent<ColliderGroup>() : null;
                if (group == null)
                {
                    result.Skip(Strings.SkipNoGroup);
                }
                else
                {
                    result.CollidersRemoved = ColliderWriter.RemoveGenerated(group);
                    report.CollidersRemoved += result.CollidersRemoved;
                    report.TargetsChanged++;
                }
                report.Results.Add(result);
            }

            Undo.CollapseUndoOperations(undoGroup);
            report.Milliseconds = timer.Elapsed.TotalMilliseconds;
            return report;
        }

        /// <summary>Number of generated, unlocked colliders that <see cref="RemoveGenerated"/> would remove.</summary>
        public static int CountRemovable(IReadOnlyList<GameObject> targets)
        {
            var count = 0;
            foreach (var target in targets)
            {
                if (target == null || !target.TryGetComponent<ColliderGroup>(out var group)) continue;
                foreach (var entry in group.Entries)
                    if (entry.Generated && !entry.Locked && entry.IsAlive) count++;
            }
            return count;
        }
    }
}
