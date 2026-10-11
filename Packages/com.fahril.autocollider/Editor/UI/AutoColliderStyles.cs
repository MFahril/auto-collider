using UnityEditor;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Applies the shared Auto Collider stylesheet and skin class to a UI Toolkit root.</summary>
    internal static class AutoColliderStyles
    {
        public const string RootClass = "ac-root";
        public const string DarkSkinClass = "ac-root--dark";
        public const string LightSkinClass = "ac-root--light";

        private static StyleSheet _mainStyleSheet;

        public static StyleSheet MainStyleSheet
        {
            get
            {
                if (_mainStyleSheet == null)
                    _mainStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackagePaths.MainStyleSheet);
                return _mainStyleSheet;
            }
        }

        /// <summary>Adds the stylesheet plus root/skin classes so USS variables resolve for both editor skins.</summary>
        public static void Apply(VisualElement root)
        {
            if (MainStyleSheet != null && !root.styleSheets.Contains(MainStyleSheet))
                root.styleSheets.Add(MainStyleSheet);

            root.AddToClassList(RootClass);
            root.EnableInClassList(DarkSkinClass, EditorGUIUtility.isProSkin);
            root.EnableInClassList(LightSkinClass, !EditorGUIUtility.isProSkin);
        }
    }
}
