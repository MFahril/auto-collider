using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Menu items, shortcuts and the shared generate/remove commands used by the window and inspector.</summary>
    internal static class GenerateCommands
    {
        private const string GameObjectMenu = "GameObject/" + Strings.ProductName + "/";

        [MenuItem(GameObjectMenu + "Generate Colliders", false, 40)]
        private static void GenerateMenu(MenuCommand command)
        {
            if (IsRepeatedContextInvocation(command)) return;
            GenerateForSelection();
        }

        [MenuItem(GameObjectMenu + "Generate Colliders", true)]
        private static bool ValidateGenerateMenu() => HasSceneSelection();

        [MenuItem(GameObjectMenu + "Remove Generated Colliders", false, 41)]
        private static void RemoveMenu(MenuCommand command)
        {
            if (IsRepeatedContextInvocation(command)) return;
            RemoveForSelection();
        }

        [MenuItem(GameObjectMenu + "Remove Generated Colliders", true)]
        private static bool ValidateRemoveMenu() => HasSceneSelection();

        [Shortcut(Strings.ShortcutCategory + "/Generate Colliders for Selection", KeyCode.C, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
        public static void GenerateForSelection()
        {
            var settings = GenerationSettingsState.instance.Settings;
            Generate(TargetResolver.FromSelection(settings.IncludeChildren), settings);
        }

        public static void RemoveForSelection()
        {
            Remove(TargetResolver.FromSelection(includeChildren: false));
        }

        public static BatchReport Generate(IReadOnlyList<GameObject> targets, GenerationSettings settings)
        {
            if (targets.Count == 0)
            {
                Notify(Strings.NotifyNothingSelected);
                return null;
            }

            PreviewDrawer.Clear();
            var report = BatchProcessor.Generate(targets, settings);
            var stats = DescribeStats(report.Stats);
            Notify(stats.Length > 0 ? Describe(report) + "\n" + stats : Describe(report));
            return report;
        }

        public static BatchReport Remove(IReadOnlyList<GameObject> targets)
        {
            var report = BatchProcessor.RemoveGenerated(targets);
            Notify(string.Format(Strings.NotifyRemoved, report.CollidersRemoved, report.TargetsChanged));
            return report;
        }

        /// <summary>One-line summary, e.g. "14 colliders on 5 objects · 1 skipped · 82 ms".</summary>
        public static string Describe(BatchReport report)
        {
            var text = string.Format(Strings.ReportCreated, report.CollidersCreated, report.TargetsChanged);
            var skipped = report.Count(TargetStatus.Skipped);
            if (skipped > 0) text += string.Format(Strings.ReportSkipped, skipped);
            var warnings = report.Count(TargetStatus.Warning);
            if (warnings > 0) text += string.Format(Strings.ReportWarnings, warnings);
            if (report.Canceled) text += Strings.ReportCanceled;
            return text + string.Format(Strings.ReportTime, report.Milliseconds);
        }

        /// <summary>"Triangles 65,536 → 4,812 (−93%) · max error 0.8 cm", or empty when no mesh colliders were made.</summary>
        public static string DescribeStats(ColliderStats stats)
        {
            if (stats.SourceTriangles <= 0) return string.Empty;
            var line = string.Format(Strings.StatsLine, stats.SourceTriangles, stats.ResultTriangles, stats.Savings * 100f,
                Format.Distance(stats.MaxError));
            return stats.ResultParts > 1 ? line + string.Format(Strings.StatsParts, stats.ResultParts) : line;
        }

        private static void Notify(string message)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.ShowNotification(new GUIContent(message), 2.5);
        }

        private static bool HasSceneSelection()
        {
            foreach (var go in Selection.gameObjects)
                if (!EditorUtility.IsPersistent(go)) return true;
            return false;
        }

        /// <summary>Hierarchy context menus invoke once per selected object; only act on the first call.</summary>
        private static bool IsRepeatedContextInvocation(MenuCommand command)
        {
            return command.context != null
                   && Selection.gameObjects.Length > 1
                   && command.context != Selection.activeGameObject;
        }
    }
}
