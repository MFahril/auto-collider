using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Main Auto Collider window: Generate / Optimize / Edit / Doctor tabs.</summary>
    internal sealed class AutoColliderWindow : EditorWindow
    {
        [MenuItem(Strings.MenuOpenWindow, priority = 0)]
        public static void Open()
        {
            var window = GetWindow<AutoColliderWindow>();
            window.titleContent = new GUIContent(Strings.ProductName, AssetPreview.GetMiniTypeThumbnail(typeof(BoxCollider)));
            window.minSize = new Vector2(320, 420);
            window.Show();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            AutoColliderStyles.Apply(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ac-window");
            root.Add(scroll);

            var tabs = new TabView { viewDataKey = "AutoCollider.Window.Tabs" };
            tabs.AddToClassList("ac-tabs");
            scroll.Add(tabs);

            tabs.Add(MakeTab(Strings.TabGenerate, new GenerateTab()));
            tabs.Add(MakeTab(Strings.TabOptimize, Placeholder(Strings.ComingOptimize)));
            tabs.Add(MakeTab(Strings.TabEdit, Placeholder(Strings.ComingEdit)));
            tabs.Add(MakeTab(Strings.TabDoctor, Placeholder(Strings.ComingDoctor)));
        }

        private static Tab MakeTab(string title, VisualElement content)
        {
            var tab = new Tab(title);
            tab.Add(content);
            return tab;
        }

        private static VisualElement Placeholder(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ac-empty");
            label.AddToClassList("ac-placeholder");
            return label;
        }
    }
}
