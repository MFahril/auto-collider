using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Small builders for the recurring UI pieces (sections, rows, foldouts, bound fields).</summary>
    internal static class UiFactory
    {
        public static VisualElement Section(VisualElement parent, string title)
        {
            var section = new VisualElement();
            section.AddToClassList("ac-section");
            if (title != null)
            {
                var label = new Label(title);
                label.AddToClassList("ac-section__header");
                section.Add(label);
            }
            parent.Add(section);
            return section;
        }

        public static Foldout Foldout(VisualElement parent, string title, string viewDataKey)
        {
            var foldout = new Foldout { text = title, value = false, viewDataKey = viewDataKey };
            foldout.AddToClassList("ac-section");
            foldout.AddToClassList("ac-foldout");
            parent.Add(foldout);
            return foldout;
        }

        public static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            parent.Add(row);
            return row;
        }

        /// <summary>PropertyField for a member of a serialized settings object (tooltips/ranges come from attributes).</summary>
        public static PropertyField Field(SerializedProperty parent, string name) => new(parent.FindPropertyRelative(name));
    }
}
