using System;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Preset drop-down shared by the Generate and Optimize tabs (built-ins, project presets, save).</summary>
    internal static class PresetMenu
    {
        /// <summary>"Balanced", a project preset name, or "Custom".</summary>
        public static string CurrentName(GenerationSettings settings) => BuiltInPresets.Identify(settings) ?? Strings.PresetCustom;

        public static void Show(Action onChanged)
        {
            var state = GenerationSettingsState.instance;
            var menu = new GenericMenu();

            foreach (var preset in BuiltInPresets.All)
            {
                var p = preset;
                menu.AddItem(new GUIContent($"{p.Name}\t{p.Description}"), p.Matches(state.Settings), () =>
                {
                    p.ApplyTo(state.Settings);
                    state.SaveState();
                    onChanged?.Invoke();
                });
            }

            menu.AddSeparator(string.Empty);
            var guids = AssetDatabase.FindAssets("t:" + nameof(ColliderPreset));
            if (guids.Length == 0) menu.AddDisabledItem(new GUIContent(Strings.PresetNoAssets));
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ColliderPreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                menu.AddItem(new GUIContent(Strings.PresetProjectPrefix + asset.name), false, () =>
                {
                    state.Replace(asset.Settings);
                    onChanged?.Invoke();
                });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(Strings.PresetSaveAs), false, () => SaveAs(state.Settings));
            menu.ShowAsContext();
        }

        private static void SaveAs(GenerationSettings settings)
        {
            var path = EditorUtility.SaveFilePanelInProject(Strings.PresetSaveTitle, "Collider Preset", "asset", Strings.PresetSaveMessage);
            if (string.IsNullOrEmpty(path)) return;

            var preset = ScriptableObject.CreateInstance<ColliderPreset>();
            preset.Capture(settings);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(preset);
        }
    }
}
