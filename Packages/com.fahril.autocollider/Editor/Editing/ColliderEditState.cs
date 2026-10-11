using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    internal enum ColliderToolMode
    {
        Select,
        Edit,
        Paint,
    }

    internal enum ColliderHandleMode
    {
        Resize,
        Move,
        Rotate,
    }

    internal enum SelectOp
    {
        Replace,
        Add,
        Toggle,
    }

    /// <summary>
    /// Editor-only state shared by the collider tool, its overlay and the Collider Group inspector:
    /// collider selection, tool / handle mode, visibility (hide / solo) and display toggles. Never saved in scenes.
    /// </summary>
    internal static class ColliderEditState
    {
        private static readonly List<Collider> SelectedList = new();
        private static readonly HashSet<Collider> Hidden = new();
        private static HashSet<Collider> _soloSet;
        private static bool? _xray;
        private static bool? _labels;

        public static event Action Changed;

        public static ColliderToolMode Mode { get; private set; } = ColliderToolMode.Edit;
        public static ColliderHandleMode HandleMode { get; private set; } = ColliderHandleMode.Resize;

        public static IReadOnlyList<Collider> Selected => SelectedList;

        /// <summary>The most recently selected collider (gets the handles).</summary>
        public static Collider Active => SelectedList.Count > 0 ? SelectedList[SelectedList.Count - 1] : null;

        public static bool Xray
        {
            get => _xray ??= AutoColliderSettings.instance.Data.XrayByDefault;
            set { _xray = value; NotifyChanged(); }
        }

        public static bool ShowLabels
        {
            get => _labels ??= AutoColliderSettings.instance.Data.ShowLabels;
            set { _labels = value; NotifyChanged(); }
        }

        public static bool IsSoloing => _soloSet != null;

        public static void SetMode(ColliderToolMode mode)
        {
            if (mode == ColliderToolMode.Paint) return; // arrives in M5
            Mode = mode;
            NotifyChanged();
        }

        public static void SetHandleMode(ColliderHandleMode mode)
        {
            HandleMode = mode;
            Mode = ColliderToolMode.Edit;
            NotifyChanged();
        }

        public static bool IsSelected(Collider c) => c != null && SelectedList.Contains(c);

        public static void Select(Collider collider, SelectOp op = SelectOp.Replace) => Select(new[] { collider }, op);

        public static void Select(IEnumerable<Collider> colliders, SelectOp op = SelectOp.Replace)
        {
            if (op == SelectOp.Replace) SelectedList.Clear();
            foreach (var c in colliders)
            {
                if (c == null) continue;
                var present = SelectedList.Remove(c);
                if (op == SelectOp.Toggle && present) continue;
                SelectedList.Add(c); // re-adding moves it to the end → becomes Active
            }
            NotifyChanged();
        }

        public static void ClearSelection()
        {
            if (SelectedList.Count == 0) return;
            SelectedList.Clear();
            NotifyChanged();
        }

        /// <summary>Swap a collider that an operation rebuilt (keeps selection and visibility).</summary>
        public static void Replace(Collider oldCollider, Collider newCollider)
        {
            if (oldCollider == newCollider || newCollider == null) return;
            var index = SelectedList.IndexOf(oldCollider);
            if (index >= 0) SelectedList[index] = newCollider;
            if (Hidden.Remove(oldCollider)) Hidden.Add(newCollider);
            if (_soloSet != null && _soloSet.Remove(oldCollider)) _soloSet.Add(newCollider);
        }

        /// <summary>Drop destroyed colliders (after undo / delete).</summary>
        public static void Prune()
        {
            var removed = SelectedList.RemoveAll(c => c == null);
            Hidden.RemoveWhere(c => c == null);
            _soloSet?.RemoveWhere(c => c == null);
            if (removed > 0) NotifyChanged();
        }

        public static bool IsVisible(Collider c) => c != null && !Hidden.Contains(c) && (_soloSet == null || _soloSet.Contains(c));

        public static void Hide(IEnumerable<Collider> colliders)
        {
            foreach (var c in colliders) if (c != null) Hidden.Add(c);
            SelectedList.RemoveAll(Hidden.Contains);
            NotifyChanged();
        }

        /// <summary>Show only the selected colliders; toggles back when already soloing.</summary>
        public static void ToggleSolo()
        {
            _soloSet = _soloSet == null && SelectedList.Count > 0 ? new HashSet<Collider>(SelectedList) : null;
            NotifyChanged();
        }

        public static void ShowAll()
        {
            Hidden.Clear();
            _soloSet = null;
            NotifyChanged();
        }

        public static string Summary()
        {
            if (SelectedList.Count == 0) return Strings.ToolNothingSelected;
            var kinds = SelectedList.Where(c => c != null).GroupBy(ColliderCost.KindName).Select(g => g.Count() > 1 ? $"{g.Count()} {g.Key}" : g.Key);
            return string.Format(Strings.ToolSelectionSummary, SelectedList.Count, string.Join(", ", kinds));
        }

        public static void NotifyChanged()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
