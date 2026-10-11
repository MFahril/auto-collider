using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// The current user's generation settings (shared by the window and menu commands).
    /// Saved per user in <c>UserSettings/</c>, so it is not committed to version control.
    /// </summary>
    [FilePath(PackagePaths.UserGenerationSettings, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class GenerationSettingsState : ScriptableSingleton<GenerationSettingsState>
    {
        public const string SettingsPropertyPath = nameof(_settings);

        [SerializeField] private GenerationSettings _settings = new();

        public GenerationSettings Settings => _settings ??= new GenerationSettings();

        private void OnEnable()
        {
            hideFlags &= ~HideFlags.NotEditable;
        }

        public void SaveState() => Save(true);

        /// <summary>Back to the project's default preset (Project Settings), or the built-in defaults.</summary>
        public void ResetToDefaults()
        {
            var preset = AutoColliderSettings.instance.Data.DefaultPreset;
            _settings = preset != null ? preset.Settings.Clone() : new GenerationSettings();
            SaveState();
        }

        /// <summary>Replace every setting with a copy of <paramref name="settings"/> (e.g. from a preset asset).</summary>
        public void Replace(GenerationSettings settings)
        {
            _settings = settings.Clone();
            SaveState();
        }
    }
}
