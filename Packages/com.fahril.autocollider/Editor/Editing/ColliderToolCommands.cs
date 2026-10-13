using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Commands shared by the collider tool, its overlay, shortcuts and the inspector.</summary>
    internal static class ColliderToolCommands
    {
        private static List<Component> Selection => ColliderEditState.Selected.Where(c => c != null).ToList();

        public static bool IsToolActive => ToolManager.activeToolType == typeof(ColliderEditTool);

        public static void ActivateTool()
        {
            if (IsToolActive) return;
            try
            {
                ToolManager.SetActiveTool<ColliderEditTool>();
            }
            catch (System.InvalidOperationException)
            {
                // Component tools are found through the inspector's editors, which can lag a selection change by a frame.
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                try
                {
                    ToolManager.SetActiveTool<ColliderEditTool>();
                }
                catch (System.InvalidOperationException)
                {
                    Notify(Strings.EditTabNoGroup);
                }
            }
        }

        /// <summary>Make sure the tool's overlay panel is shown in every Scene view.</summary>
        public static void ShowOverlay()
        {
            foreach (SceneView view in SceneView.sceneViews)
                if (view.TryGetOverlay(ColliderOverlay.OverlayId, out var overlay))
                    overlay.displayed = true;
        }

        public static void ExitTool() => ToolManager.RestorePreviousTool();

        public static void Duplicate() => SelectResult(ColliderOperations.Duplicate(Selection), Strings.NotifyDuplicated);

        public static void Delete()
        {
            var count = ColliderOperations.Delete(Selection);
            ColliderEditState.ClearSelection();
            Notify(string.Format(Strings.NotifyDeleted, count));
        }

        public static void Mirror(int axis) => SelectResult(ColliderOperations.Mirror(Selection, axis), Strings.NotifyMirrored);

        public static void Merge(ColliderOperations.MergeKind kind) => SelectResult(ColliderOperations.Merge(Selection, kind), Strings.NotifyMerged);

        public static void Convert(ShapeKind kind)
        {
            var entries = SelectedEntries();
            ColliderOperations.Convert(Selection, kind);
            Reselect(entries);
            Notify(string.Format(Strings.NotifyConverted, entries.Count));
        }

        public static void Refit()
        {
            var entries = SelectedEntries();
            var count = ColliderOperations.Refit(Selection);
            Reselect(entries);
            Notify(string.Format(Strings.NotifyRefit, count, entries.Count));
        }

        public static void Inflate(float amount)
        {
            var entries = SelectedEntries();
            ColliderOperations.Inflate(Selection, amount);
            Reselect(entries);
        }

        public static void ToggleLock()
        {
            var selection = Selection;
            if (selection.Count == 0) return;
            var anyUnlocked = selection.Any(c => ColliderOperations.TryFindAny(c, out _, out var e) && !e.Locked);
            ColliderOperations.SetLocked(selection, anyUnlocked);
            Notify(anyUnlocked ? Strings.NotifyLocked : Strings.NotifyUnlocked);
            ColliderEditState.NotifyChanged();
        }

        /// <summary>Simplify the active 2D polygon / edge collider (tolerance in world units).</summary>
        public static void SimplifyActive(float tolerance)
        {
            if (!(ColliderEditState.Active is Collider2D active)) return;
            var removed = ColliderOperations2D.Simplify(active, tolerance);
            ColliderEditState.NotifyChanged();
            Notify(string.Format(Strings.NotifySimplified, removed));
        }

        public static void Rename(string label)
        {
            var active = ColliderEditState.Active;
            if (active != null) ColliderOperations.Rename(active, label);
            ColliderEditState.NotifyChanged();
        }

        // ---------- Paint ----------

        /// <summary>Collider Groups on the selected objects (the tool's targets).</summary>
        public static List<ColliderGroup> SelectedGroups() =>
            UnityEditor.Selection.gameObjects.Select(g => g.GetComponent<ColliderGroup>()).Where(g => g != null).ToList();

        /// <summary>
        /// Start painting: adds an empty Collider Group to selected objects that have none (one Undo step), activates the
        /// tool and switches to Paint mode.
        /// </summary>
        public static void StartPainting()
        {
            var objects = UnityEditor.Selection.gameObjects.Where(g => !EditorUtility.IsPersistent(g)).ToList();
            if (objects.Count == 0)
            {
                Notify(Strings.NotifyNothingSelected);
                return;
            }

            var missing = objects.Where(g => g.GetComponent<ColliderGroup>() == null).ToList();
            if (missing.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                var undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(Strings.UndoAddGroup);
                foreach (var go in missing)
                {
                    var group = Undo.AddComponent<ColliderGroup>(go);
                    ColliderWriter.RecordGroup(group, Strings.UndoAddGroup);
                    group.Recipe.Settings = GenerationSettingsState.instance.Settings.Clone();
                }
                Undo.CollapseUndoOperations(undoGroup);
            }

            ColliderEditState.SetMode(ColliderToolMode.Paint);
            ActivateTool();
        }

        public static void PaintAll()
        {
            foreach (var mesh in PaintMeshCache.For(SelectedGroups()))
                ColliderEditState.SetPainted(mesh.Renderer, Enumerable.Range(0, mesh.TriangleCount));
        }

        public static void PaintConnected() => ExpandPaint((mesh, set) => mesh.Connected(set));

        public static void PaintGrow() => ExpandPaint((mesh, set) => mesh.Grow(set));

        private static void ExpandPaint(System.Func<PaintMesh, HashSet<int>, HashSet<int>> expand)
        {
            foreach (var pair in ColliderEditState.Painted.ToList())
            {
                var mesh = PaintMeshCache.Get(pair.Key, pair.Key.GetComponentInParent<ColliderGroup>(true));
                if (mesh != null) ColliderEditState.SetPainted(pair.Key, expand(mesh, pair.Value));
            }
        }

        public static void ClearPaint() => ColliderEditState.ClearPaint();

        /// <summary>Turn the current paint into region(s) of the given shape, then select the new colliders.</summary>
        public static void CreateRegion(PartShape shape)
        {
            if (ColliderEditState.PaintedCount == 0)
            {
                Notify(Strings.NotifyNothingPainted);
                return;
            }
            var created = ColliderOperations.CreateRegions(ColliderEditState.Painted, shape);
            ColliderEditState.ClearPaint();
            if (created.Count > 0) ColliderEditState.Select(created);
            Notify(string.Format(Strings.NotifyRegionCreated, created.Count));
        }

        /// <summary>Load a region's triangles back into the paint selection (to see or re-make it).</summary>
        public static void LoadRegion(PaintedRegion region)
        {
            ColliderEditState.SetPainted(region.Renderer, region.Triangles);
            ColliderEditState.SetMode(ColliderToolMode.Paint);
        }

        public static void DeleteRegion(ColliderGroup group, PaintedRegion region)
        {
            ColliderOperations.DeleteRegion(group, region.Id);
            ColliderEditState.Prune();
            ColliderEditState.NotifyChanged();
        }

        public static void ResizeBrush(float factor) =>
            ColliderEditState.BrushRadius = Mathf.Clamp(ColliderEditState.BrushRadius * factor, 4f, 256f);

        public static void SelectAll(IEnumerable<ColliderGroup> groups) =>
            ColliderEditState.Select(ColliderShapes.ComponentsOf(groups).Where(ColliderEditState.IsVisible));

        public static void FrameSelection(SceneView view)
        {
            var selection = Selection;
            if (view == null || selection.Count == 0) return;
            var bounds = ColliderPicker.BoundsOf(selection[0]);
            foreach (var c in selection) bounds.Encapsulate(ColliderPicker.BoundsOf(c));
            view.Frame(bounds, false);
        }

        private static void SelectResult(List<Component> created, string message)
        {
            ColliderEditState.Prune();
            if (created.Count > 0) ColliderEditState.Select(created);
            Notify(string.Format(message, created.Count));
        }

        private static List<ColliderEntry> SelectedEntries()
        {
            var entries = new List<ColliderEntry>();
            foreach (var c in Selection)
                if (ColliderOperations.TryFindAny(c, out _, out var entry)) entries.Add(entry);
            return entries;
        }

        /// <summary>Operations may rebuild a collider but keep its entry: select the entries' current colliders again.</summary>
        private static void Reselect(List<ColliderEntry> entries)
        {
            ColliderEditState.Select(entries.Select(e => e.Component).Where(c => c != null));
        }

        public static void Notify(string message)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.ShowNotification(new GUIContent(message), 1.5);
        }
    }

    /// <summary>Shortcut context: active while the collider tool is (takes priority over global shortcuts).</summary>
    internal sealed class ColliderToolShortcutContext : IShortcutContext
    {
        public bool active => ColliderToolCommands.IsToolActive;
    }

    /// <summary>Rebindable shortcuts (Edit ▸ Shortcuts ▸ Auto Collider) that only apply while the collider tool is active.</summary>
    internal static class ColliderToolShortcuts
    {
        private const string Prefix = Strings.ShortcutCategory + "/Edit Tool/";

        public static bool VertexSnapHeld { get; private set; }

        [Shortcut(Prefix + "Select Mode", typeof(ColliderToolShortcutContext), KeyCode.Alpha1)]
        private static void SelectMode() => ColliderEditState.SetMode(ColliderToolMode.Select);

        [Shortcut(Prefix + "Edit Mode", typeof(ColliderToolShortcutContext), KeyCode.Alpha2)]
        private static void EditMode() => ColliderEditState.SetMode(ColliderToolMode.Edit);

        [Shortcut(Prefix + "Paint Mode", typeof(ColliderToolShortcutContext), KeyCode.Alpha3)]
        private static void PaintMode() => ColliderEditState.SetMode(ColliderToolMode.Paint);

        [Shortcut(Prefix + "Smaller Brush", typeof(ColliderToolShortcutContext), KeyCode.LeftBracket)]
        private static void SmallerBrush() => ColliderToolCommands.ResizeBrush(1f / 1.25f);

        [Shortcut(Prefix + "Bigger Brush", typeof(ColliderToolShortcutContext), KeyCode.RightBracket)]
        private static void BiggerBrush() => ColliderToolCommands.ResizeBrush(1.25f);

        [Shortcut(Prefix + "Resize", typeof(ColliderToolShortcutContext), KeyCode.R)]
        private static void Resize() => ColliderEditState.SetHandleMode(ColliderHandleMode.Resize);

        [Shortcut(Prefix + "Move", typeof(ColliderToolShortcutContext), KeyCode.W)]
        private static void Move() => ColliderEditState.SetHandleMode(ColliderHandleMode.Move);

        [Shortcut(Prefix + "Rotate", typeof(ColliderToolShortcutContext), KeyCode.E)]
        private static void Rotate() => ColliderEditState.SetHandleMode(ColliderHandleMode.Rotate);

        [Shortcut(Prefix + "Duplicate", typeof(ColliderToolShortcutContext), KeyCode.D, ShortcutModifiers.Action)]
        private static void Duplicate() => ColliderToolCommands.Duplicate();

        [Shortcut(Prefix + "Delete", typeof(ColliderToolShortcutContext), KeyCode.Delete)]
        private static void Delete() => ColliderToolCommands.Delete();

        [Shortcut(Prefix + "Mirror X", typeof(ColliderToolShortcutContext), KeyCode.M)]
        private static void MirrorX() => ColliderToolCommands.Mirror(0);

        [Shortcut(Prefix + "Merge to Hull", typeof(ColliderToolShortcutContext), KeyCode.G)]
        private static void Merge() => ColliderToolCommands.Merge(ColliderOperations.MergeKind.ConvexHull);

        [Shortcut(Prefix + "Toggle Lock", typeof(ColliderToolShortcutContext), KeyCode.L)]
        private static void Lock() => ColliderToolCommands.ToggleLock();

        [Shortcut(Prefix + "Solo", typeof(ColliderToolShortcutContext), KeyCode.H)]
        private static void Solo() => ColliderEditState.ToggleSolo();

        [Shortcut(Prefix + "Hide Selected", typeof(ColliderToolShortcutContext), KeyCode.H, ShortcutModifiers.Shift)]
        private static void Hide() => ColliderEditState.Hide(ColliderEditState.Selected.ToList());

        [Shortcut(Prefix + "Show All", typeof(ColliderToolShortcutContext), KeyCode.H, ShortcutModifiers.Alt)]
        private static void ShowAll() => ColliderEditState.ShowAll();

        [Shortcut(Prefix + "Toggle X-Ray", typeof(ColliderToolShortcutContext), KeyCode.X)]
        private static void Xray() => ColliderEditState.Xray = !ColliderEditState.Xray;

        [ClutchShortcut(Prefix + "Vertex Snap (hold)", typeof(ColliderToolShortcutContext), KeyCode.V)]
        private static void VertexSnap(ShortcutArguments args) => VertexSnapHeld = args.stage == ShortcutStage.Begin;
    }
}
