using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Small collider-count badge at the right edge of Hierarchy rows that have a <see cref="ColliderGroup"/>
    /// (Project Settings ▸ Auto Collider ▸ Show Hierarchy Badge). IMGUI because the Hierarchy is an IMGUI view.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyBadge
    {
        private const float Width = 34f;

        private static GUIStyle _style;
        private static Texture _icon;

        static HierarchyBadge()
        {
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnItemGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnItemGUI;
        }

        private static void OnItemGUI(EntityId id, Rect row)
        {
            if (!AutoColliderSettings.instance.Data.ShowHierarchyBadge) return;
            if (!(EditorUtility.EntityIdToObject(id) is GameObject go) || !go.TryGetComponent<ColliderGroup>(out var group)) return;

            _style ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 2, 0, 0) };
            if (_icon == null) _icon = AssetPreview.GetMiniTypeThumbnail(typeof(BoxCollider));

            var count = group.LiveCount;
            var rect = new Rect(row.xMax - Width, row.y, Width, row.height);
            var content = new GUIContent(count.ToString(), _icon, string.Format(Strings.HierarchyBadgeTooltip, count));
            var color = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(rect, content, _style);
            GUI.color = color;
        }
    }
}
