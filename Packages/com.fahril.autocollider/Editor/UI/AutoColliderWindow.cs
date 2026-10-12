using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Main Auto Collider window: Generate / Optimize / Edit / Doctor tabs.</summary>
    internal sealed class AutoColliderWindow : EditorWindow
    {
        private TabView _tabs;
        private Tab _doctorTab;
        private DoctorTab _doctor;

        [MenuItem(Strings.MenuOpenWindow, priority = 0)]
        public static void Open() => OpenWindow(false);

        [MenuItem(Strings.MenuRunDoctor, priority = 1)]
        public static void RunDoctor() => OpenWindow(true);

        private static void OpenWindow(bool doctor)
        {
            var window = GetWindow<AutoColliderWindow>();
            window.titleContent = new GUIContent(Strings.ProductName, AssetPreview.GetMiniTypeThumbnail(typeof(BoxCollider)));
            window.minSize = new Vector2(320, 420);
            window.Show();
            if (!doctor || window._tabs == null) return;
            window._tabs.activeTab = window._doctorTab;
            window._doctor.ScanScene();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            AutoColliderStyles.Apply(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ac-window");
            root.Add(scroll);

            _tabs = new TabView { viewDataKey = "AutoCollider.Window.Tabs" };
            _tabs.AddToClassList("ac-tabs");
            scroll.Add(_tabs);

            _tabs.Add(MakeTab(Strings.TabGenerate, new GenerateTab()));
            _tabs.Add(MakeTab(Strings.TabOptimize, new OptimizeTab()));
            _tabs.Add(MakeTab(Strings.TabEdit, new EditTab()));
            _doctor = new DoctorTab();
            _doctorTab = MakeTab(Strings.TabDoctor, _doctor);
            _tabs.Add(_doctorTab);
        }

        private static Tab MakeTab(string title, VisualElement content)
        {
            var tab = new Tab(title);
            tab.Add(content);
            return tab;
        }
    }
}
