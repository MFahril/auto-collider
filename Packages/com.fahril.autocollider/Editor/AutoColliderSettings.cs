using System;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Project-wide Auto Collider settings, stored in <c>ProjectSettings/AutoColliderSettings.asset</c>
    /// so they are shared with the team through version control.
    /// </summary>
    [FilePath(PackagePaths.SettingsAsset, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class AutoColliderSettings : ScriptableSingleton<AutoColliderSettings>
    {
        /// <summary>Serialized property path of <see cref="Data"/>, for UI binding.</summary>
        public const string DataPropertyPath = nameof(_data);

        [SerializeField] private AutoColliderSettingsData _data = new();

        public AutoColliderSettingsData Data => _data ??= new AutoColliderSettingsData();

        private void OnEnable()
        {
            // ScriptableSingleton is created NotEditable; the settings page needs to edit it.
            hideFlags &= ~HideFlags.NotEditable;
        }

        public void SaveSettings() => Save(true);

        public void ResetToDefaults()
        {
            _data = new AutoColliderSettingsData();
            SaveSettings();
        }
    }

    /// <summary>Settings values. Field initializers are the single source of default values.</summary>
    [Serializable]
    internal sealed class AutoColliderSettingsData
    {
        public const string DefaultMeshFolder = "Assets/AutoCollider/Generated";
        public const string DefaultContainerName = "[Colliders]";

        [Tooltip("Folder (inside Assets/) where generated collider meshes are saved as assets.")]
        public string GeneratedMeshFolder = DefaultMeshFolder;

        [Tooltip("Name of the child GameObject that holds generated colliders.")]
        public string ContainerName = DefaultContainerName;

        [Tooltip("Live preview is skipped for meshes above this triangle count; use the Preview button instead.")]
        [Min(1000)]
        public int PreviewTriangleBudget = 200_000;

        [Tooltip("Scale of collider edit handles in the Scene view.")]
        [Range(0.25f, 3f)]
        public float HandleSize = 1f;

        [Tooltip("Draw colliders through geometry (faded) while the collider tool is active.")]
        public bool XrayByDefault = true;

        [Tooltip("Opacity of the filled collider surfaces drawn in the Scene view.")]
        [Range(0f, 1f)]
        public float FillOpacity = 0.15f;

        [Tooltip("Scene view color for box colliders.")]
        public Color BoxColor = new(0.27f, 0.85f, 0.45f, 1f);

        [Tooltip("Scene view color for sphere colliders.")]
        public Color SphereColor = new(0.30f, 0.70f, 1.00f, 1f);

        [Tooltip("Scene view color for capsule colliders.")]
        public Color CapsuleColor = new(0.95f, 0.75f, 0.25f, 1f);

        [Tooltip("Scene view color for convex and concave mesh colliders.")]
        public Color MeshColor = new(0.80f, 0.45f, 0.95f, 1f);

        [Tooltip("Scene view color for selected colliders.")]
        public Color SelectedColor = new(1.00f, 0.55f, 0.15f, 1f);

        public string ResolvedContainerName =>
            string.IsNullOrWhiteSpace(ContainerName) ? DefaultContainerName : ContainerName;

        public bool IsMeshFolderValid => IsValidMeshFolder(GeneratedMeshFolder);

        public static bool IsValidMeshFolder(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && (path == "Assets" || path.StartsWith("Assets/"))
                && !path.Contains("..");
        }
    }
}
