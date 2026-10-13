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
        private static readonly List<Component> SelectedList = new();
        private static readonly HashSet<Component> Hidden = new();
        private static HashSet<Component> _soloSet;
        private static bool? _xray;
        private static bool? _labels;

        public static event Action Changed;

        public static ColliderToolMode Mode { get; private set; } = ColliderToolMode.Edit;
        public static ColliderHandleMode HandleMode { get; private set; } = ColliderHandleMode.Resize;

        public static IReadOnlyList<Component> Selected => SelectedList;

        /// <summary>The most recently selected collider — a Collider or a Collider2D (gets the handles).</summary>
        public static Component Active => SelectedList.Count > 0 ? SelectedList[SelectedList.Count - 1] : null;

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
            Mode = mode;
            NotifyChanged();
        }

        // ---------- Paint ----------

        private static readonly Dictionary<Renderer, HashSet<int>> PaintedTriangles = new();

        private static float _brushRadius = 24f;

        /// <summary>Brush radius in screen points.</summary>
        public static float BrushRadius
        {
            get => _brushRadius;
            set
            {
                _brushRadius = Mathf.Clamp(value, 4f, 256f);
                NotifyChanged();
            }
        }

        /// <summary>Painted (not yet turned into a region) triangles per renderer.</summary>
        public static IReadOnlyDictionary<Renderer, HashSet<int>> Painted => PaintedTriangles;

        public static int PaintedCount => PaintedTriangles.Values.Sum(s => s.Count);

        public static void Paint(Renderer renderer, IEnumerable<int> triangles, bool erase)
        {
            if (renderer == null) return;
            if (!PaintedTriangles.TryGetValue(renderer, out var set)) PaintedTriangles[renderer] = set = new HashSet<int>();
            var changed = false;
            foreach (var t in triangles) changed |= erase ? set.Remove(t) : set.Add(t);
            if (set.Count == 0) PaintedTriangles.Remove(renderer);
            if (changed) NotifyChanged();
        }

        public static void SetPainted(Renderer renderer, IEnumerable<int> triangles)
        {
            if (renderer == null) return;
            PaintedTriangles[renderer] = new HashSet<int>(triangles);
            if (PaintedTriangles[renderer].Count == 0) PaintedTriangles.Remove(renderer);
            NotifyChanged();
        }

        public static void ClearPaint()
        {
            if (PaintedTriangles.Count == 0) return;
            PaintedTriangles.Clear();
            NotifyChanged();
        }

        public static void SetHandleMode(ColliderHandleMode mode)
        {
            HandleMode = mode;
            Mode = ColliderToolMode.Edit;
            NotifyChanged();
        }

        public static bool IsSelected(Component c) => c != null && SelectedList.Contains(c);

        public static void Select(Component collider, SelectOp op = SelectOp.Replace) => Select(new[] { collider }, op);

        public static void Select(IEnumerable<Component> colliders, SelectOp op = SelectOp.Replace)
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
        public static void Replace(Component oldCollider, Component newCollider)
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
            foreach (var renderer in PaintedTriangles.Keys.Where(r => r == null).ToList()) PaintedTriangles.Remove(renderer);
            var removed = SelectedList.RemoveAll(c => c == null);
            Hidden.RemoveWhere(c => c == null);
            _soloSet?.RemoveWhere(c => c == null);
            if (removed > 0) NotifyChanged();
        }

        public static bool IsVisible(Component c) => c != null && !Hidden.Contains(c) && (_soloSet == null || _soloSet.Contains(c));

        public static void Hide(IEnumerable<Component> colliders)
        {
            foreach (var c in colliders) if (c != null) Hidden.Add(c);
            SelectedList.RemoveAll(Hidden.Contains);
            NotifyChanged();
        }

        /// <summary>Show only the selected colliders; toggles back when already soloing.</summary>
        public static void ToggleSolo()
        {
            _soloSet = _soloSet == null && SelectedList.Count > 0 ? new HashSet<Component>(SelectedList) : null;
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
