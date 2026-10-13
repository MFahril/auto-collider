using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Scene-view panel for the collider tool: modes, handles, selection info and every edit operation.</summary>
    [Overlay(typeof(SceneView), OverlayId, Strings.ProductName, defaultDisplay = true)]
    internal sealed class ColliderOverlay : Overlay, ITransientOverlay
    {
        public const string OverlayId = "auto-collider-edit-tool";

        private Button _select, _edit, _paint, _resize, _move, _rotate;
        private Label _summary, _hint;
        private TextField _rename;
        private FloatField _step;
        private Toggle _xray, _labels;
        private VisualElement _selectionActions;
        private Button _mergeHull, _mergeBox, _lock, _solo;

        public bool visible => ColliderToolCommands.IsToolActive;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            AutoColliderStyles.Apply(root);
            root.AddToClassList("ac-overlay");

            var modes = Row(root);
            _select = Toggle(modes, Strings.ModeSelect, Strings.ModeSelectTooltip, () => ColliderEditState.SetMode(ColliderToolMode.Select));
            _edit = Toggle(modes, Strings.ModeEdit, Strings.ModeEditTooltip, () => ColliderEditState.SetMode(ColliderToolMode.Edit));
            _paint = Toggle(modes, Strings.ModePaint, Strings.ModePaintTooltip, () => ColliderEditState.SetMode(ColliderToolMode.Paint));

            var handles = Row(root);
            _handlesRow = handles;
            _resize = Toggle(handles, Strings.HandleResize, Strings.HandleResizeTooltip, () => ColliderEditState.SetHandleMode(ColliderHandleMode.Resize));
            _move = Toggle(handles, Strings.HandleMove, Strings.HandleMoveTooltip, () => ColliderEditState.SetHandleMode(ColliderHandleMode.Move));
            _rotate = Toggle(handles, Strings.HandleRotate, Strings.HandleRotateTooltip, () => ColliderEditState.SetHandleMode(ColliderHandleMode.Rotate));

            _summary = new Label();
            _summary.AddToClassList("ac-overlay__summary");
            root.Add(_summary);
            _hint = new Label();
            _hint.AddToClassList("ac-hint");
            root.Add(_hint);

            _selectionActions = new VisualElement();
            root.Add(_selectionActions);

            _rename = new TextField { isDelayed = true, tooltip = Strings.RenameTooltip };
            _rename.AddToClassList("ac-overlay__rename");
            _rename.RegisterValueChangedCallback(e => ColliderToolCommands.Rename(e.newValue));
            _selectionActions.Add(_rename);

            var edit = Row(_selectionActions);
            Action(edit, Strings.ActionDuplicate, Strings.ActionDuplicateTooltip, ColliderToolCommands.Duplicate);
            Action(edit, Strings.ActionDelete, Strings.ActionDeleteTooltip, ColliderToolCommands.Delete);
            _lock = Action(edit, Strings.ActionLock, Strings.ActionLockTooltip, ColliderToolCommands.ToggleLock);

            var mirror = Row(_selectionActions);
            Caption(mirror, Strings.CaptionMirror);
            Action(mirror, "X", Strings.ActionMirrorTooltip, () => ColliderToolCommands.Mirror(0));
            Action(mirror, "Y", Strings.ActionMirrorTooltip, () => ColliderToolCommands.Mirror(1));
            Action(mirror, "Z", Strings.ActionMirrorTooltip, () => ColliderToolCommands.Mirror(2));

            var convert = Row(_selectionActions);
            Caption(convert, Strings.CaptionConvert);
            Action(convert, Strings.KindBox, Strings.ActionConvertTooltip, () => ColliderToolCommands.Convert(ShapeKind.Box));
            Action(convert, Strings.KindSphere, Strings.ActionConvertTooltip, () => ColliderToolCommands.Convert(ShapeKind.Sphere));
            Action(convert, Strings.KindCapsule, Strings.ActionConvertTooltip, () => ColliderToolCommands.Convert(ShapeKind.Capsule));
            Action(convert, Strings.KindConvex, Strings.ActionConvertTooltip, () => ColliderToolCommands.Convert(ShapeKind.ConvexMesh));

            var merge = Row(_selectionActions);
            Caption(merge, Strings.CaptionMerge);
            _mergeHull = Action(merge, Strings.KindConvex, Strings.ActionMergeTooltip, () => ColliderToolCommands.Merge(ColliderOperations.MergeKind.ConvexHull));
            _mergeBox = Action(merge, Strings.KindBox, Strings.ActionMergeTooltip, () => ColliderToolCommands.Merge(ColliderOperations.MergeKind.Box));

            var fit = Row(_selectionActions);
            Action(fit, Strings.ActionRefit, Strings.ActionRefitTooltip, ColliderToolCommands.Refit);
            _step = new FloatField { value = 0.01f, tooltip = Strings.InflateStepTooltip };
            _step.AddToClassList("ac-overlay__step");
            fit.Add(_step);
            Action(fit, "+", Strings.ActionInflateTooltip, () => ColliderToolCommands.Inflate(Mathf.Abs(_step.value)));
            Action(fit, "−", Strings.ActionDeflateTooltip, () => ColliderToolCommands.Inflate(-Mathf.Abs(_step.value)));

            _polygonRow = Row(_selectionActions);
            Caption(_polygonRow, Strings.CaptionPolygon);
            _simplifyTolerance = new FloatField { value = 0.02f, tooltip = Strings.SimplifyToleranceTooltip };
            _simplifyTolerance.AddToClassList("ac-overlay__step");
            _polygonRow.Add(_simplifyTolerance);
            Action(_polygonRow, Strings.ActionSimplify, Strings.ActionSimplifyTooltip, () => ColliderToolCommands.SimplifyActive(Mathf.Abs(_simplifyTolerance.value)));

            BuildPaintSection(root);

            var view = Row(root);
            _solo = Action(view, Strings.ActionSolo, Strings.ActionSoloTooltip, ColliderEditState.ToggleSolo);
            Action(view, Strings.ActionShowAll, Strings.ActionShowAllTooltip, ColliderEditState.ShowAll);
            _xray = new Toggle(Strings.ToggleXray) { tooltip = Strings.ToggleXrayTooltip };
            _xray.RegisterValueChangedCallback(e => ColliderEditState.Xray = e.newValue);
            view.Add(_xray);
            _labels = new Toggle(Strings.ToggleLabels) { tooltip = Strings.ToggleLabelsTooltip };
            _labels.RegisterValueChangedCallback(e => ColliderEditState.ShowLabels = e.newValue);
            view.Add(_labels);

            var footer = Row(root);
            footer.AddToClassList("ac-footer");
            Action(footer, Strings.ToolExit, Strings.ToolExitTooltip, ColliderToolCommands.ExitTool);

            root.RegisterCallback<AttachToPanelEvent>(_ => ColliderEditState.Changed += Refresh);
            root.RegisterCallback<DetachFromPanelEvent>(_ => ColliderEditState.Changed -= Refresh);
            Refresh();
            return root;
        }

        private VisualElement _handlesRow, _paintSection, _regionList, _polygonRow;
        private FloatField _simplifyTolerance;
        private Label _paintSummary;
        private SliderInt _brush;
        private VisualElement _createRow;

        private void BuildPaintSection(VisualElement root)
        {
            _paintSection = new VisualElement();
            root.Add(_paintSection);

            _paintSummary = new Label();
            _paintSummary.AddToClassList("ac-overlay__summary");
            _paintSection.Add(_paintSummary);
            var hint = new Label(Strings.PaintHint);
            hint.AddToClassList("ac-hint");
            _paintSection.Add(hint);

            _brush = new SliderInt(Strings.PaintBrush, 4, 128) { tooltip = Strings.PaintBrushTooltip, showInputField = true };
            _brush.AddToClassList("ac-overlay__brush");
            _brush.RegisterValueChangedCallback(e => ColliderEditState.BrushRadius = e.newValue);
            _paintSection.Add(_brush);

            var select = Row(_paintSection);
            Action(select, Strings.PaintAll, Strings.PaintAllTooltip, ColliderToolCommands.PaintAll);
            Action(select, Strings.PaintConnected, Strings.PaintConnectedTooltip, ColliderToolCommands.PaintConnected);
            Action(select, Strings.PaintGrow, Strings.PaintGrowTooltip, ColliderToolCommands.PaintGrow);
            Action(select, Strings.PaintClear, Strings.PaintClearTooltip, ColliderToolCommands.ClearPaint);

            _createRow = Row(_paintSection);
            Caption(_createRow, Strings.CaptionCreate);
            foreach (var shape in new[] { PartShape.Auto, PartShape.Box, PartShape.Sphere, PartShape.Capsule, PartShape.ConvexHull, PartShape.ConvexParts })
            {
                var s = shape;
                Action(_createRow, Strings.PartShapeName(s), Strings.CreateRegionTooltip, () => ColliderToolCommands.CreateRegion(s));
            }

            var regionsHeader = new Label(Strings.RegionsHeader);
            regionsHeader.AddToClassList("ac-overlay__caption");
            _paintSection.Add(regionsHeader);
            _regionList = new VisualElement();
            _regionList.AddToClassList("ac-overlay__regions");
            _paintSection.Add(_regionList);
            var regenerateNote = new Label(Strings.RegionsRegenerateNote);
            regenerateNote.AddToClassList("ac-hint");
            _paintSection.Add(regenerateNote);
        }

        private void RefreshPaint()
        {
            _paintSummary.text = string.Format(Strings.PaintSummary, ColliderEditState.PaintedCount);
            _brush.SetValueWithoutNotify(Mathf.RoundToInt(ColliderEditState.BrushRadius));
            _createRow.SetEnabled(ColliderEditState.PaintedCount > 0);

            _regionList.Clear();
            foreach (var group in ColliderToolCommands.SelectedGroups())
            {
                var regions = group.Recipe.Regions;
                for (var i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    var row = Row(_regionList);
                    var swatch = new VisualElement();
                    swatch.AddToClassList("ac-overlay__swatch");
                    swatch.style.backgroundColor = PaintDrawing.RegionColor(i);
                    row.Add(swatch);
                    var label = new Label(string.Format(Strings.RegionRow, region.Label, Strings.PartShapeName(region.Shape), region.Triangles.Length));
                    label.AddToClassList("ac-overlay__region-label");
                    row.Add(label);
                    Action(row, Strings.RegionPaint, Strings.RegionPaintTooltip, () => ColliderToolCommands.LoadRegion(region));
                    Action(row, "✕", Strings.RegionDeleteTooltip, () => ColliderToolCommands.DeleteRegion(group, region));
                }
            }
            if (_regionList.childCount == 0)
            {
                var none = new Label(Strings.RegionsNone);
                none.AddToClassList("ac-hint");
                _regionList.Add(none);
            }
        }

        private void Refresh()
        {
            if (_summary == null) return;
            var mode = ColliderEditState.Mode;
            var handle = ColliderEditState.HandleMode;
            var painting = mode == ColliderToolMode.Paint;
            Mark(_select, mode == ColliderToolMode.Select);
            Mark(_edit, mode == ColliderToolMode.Edit);
            Mark(_paint, painting);
            _handlesRow.EnableInClassList("ac-hidden", painting);
            _paintSection.EnableInClassList("ac-hidden", !painting);
            if (painting) RefreshPaint();
            Mark(_resize, mode == ColliderToolMode.Edit && handle == ColliderHandleMode.Resize);
            Mark(_move, mode == ColliderToolMode.Edit && handle == ColliderHandleMode.Move);
            Mark(_rotate, mode == ColliderToolMode.Edit && handle == ColliderHandleMode.Rotate);

            var selected = ColliderEditState.Selected.Where(c => c != null).ToList();
            var active = ColliderEditState.Active;
            _summary.text = ColliderEditState.Summary();
            var polygon2D = active is PolygonCollider2D || active is EdgeCollider2D;
            _hint.text = selected.Count == 0 ? Strings.ToolPickHint
                : active is MeshCollider && handle == ColliderHandleMode.Resize && mode == ColliderToolMode.Edit ? Strings.ToolMeshResizeHint
                : polygon2D && handle == ColliderHandleMode.Resize && mode == ColliderToolMode.Edit ? Strings.ToolPolygonHint
                : Strings.ToolEditHint;
            _polygonRow.EnableInClassList("ac-hidden", !polygon2D);

            _selectionActions.EnableInClassList("ac-hidden", selected.Count == 0 || painting);
            _summary.EnableInClassList("ac-hidden", painting);
            _hint.EnableInClassList("ac-hidden", painting);
            _mergeHull.SetEnabled(selected.Count > 1);
            _mergeBox.SetEnabled(selected.Count > 1);

            var entryLocked = selected.Count > 0 && selected.All(c => ColliderOperations.TryFindAny(c, out _, out var e) && e.Locked);
            _lock.text = entryLocked ? Strings.ActionUnlock : Strings.ActionLock;
            _solo.text = ColliderEditState.IsSoloing ? Strings.ActionUnsolo : Strings.ActionSolo;

            _rename.SetEnabled(selected.Count == 1);
            _rename.SetValueWithoutNotify(selected.Count == 1 && ColliderOperations.TryFindAny(active, out _, out var activeEntry) ? activeEntry.Label : string.Empty);
            _xray.SetValueWithoutNotify(ColliderEditState.Xray);
            _labels.SetValueWithoutNotify(ColliderEditState.ShowLabels);
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            row.AddToClassList("ac-overlay__row");
            parent.Add(row);
            return row;
        }

        private static void Caption(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("ac-overlay__caption");
            parent.Add(label);
        }

        private static Button Toggle(VisualElement parent, string text, string tooltip, Action onClick)
        {
            var button = Action(parent, text, tooltip, onClick);
            button.AddToClassList("ac-overlay__toggle");
            return button;
        }

        private static Button Action(VisualElement parent, string text, string tooltip, Action onClick)
        {
            var button = new Button(() => onClick?.Invoke()) { text = text, tooltip = tooltip };
            button.AddToClassList("ac-button");
            button.AddToClassList("ac-button--small");
            parent.Add(button);
            return button;
        }

        private static void Mark(VisualElement element, bool on) => element.EnableInClassList("ac-overlay__toggle--on", on);
    }
}
