using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Project Settings ▸ Auto Collider page (UI Toolkit).</summary>
    internal static class AutoColliderSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Strings.SettingsPath, SettingsScope.Project)
            {
                label = Strings.ProductName,
                activateHandler = (_, root) => Build(root),
                keywords = new HashSet<string>
                {
                    "collider", "physics", "convex", "mesh", "handle", "generated", "auto collider"
                }
            };
        }

        [MenuItem(Strings.MenuProjectSettings, priority = 1000)]
        private static void OpenProjectSettings() => SettingsService.OpenProjectSettings(Strings.SettingsPath);

        private static void Build(VisualElement root)
        {
            var settings = AutoColliderSettings.instance;
            var serialized = new SerializedObject(settings);
            var data = serialized.FindProperty(AutoColliderSettings.DataPropertyPath);

            AutoColliderStyles.Apply(root);
            var page = new VisualElement();
            page.AddToClassList("ac-page");
            root.Add(page);

            page.Add(Label(Strings.ProductName, "ac-title"));
            page.Add(Label(Strings.SettingsSubtitle, "ac-subtitle"));

            var generation = Section(page, Strings.SectionGeneration);
            var folderRow = new VisualElement();
            folderRow.AddToClassList("ac-row");
            var folderField = Field(data, nameof(AutoColliderSettingsData.GeneratedMeshFolder));
            folderField.AddToClassList("ac-row__grow");
            folderRow.Add(folderField);
            var browse = new Button(() => BrowseMeshFolder(settings, serialized)) { text = Strings.Browse, tooltip = Strings.BrowseTooltip };
            browse.AddToClassList("ac-button");
            folderRow.Add(browse);
            generation.Add(folderRow);

            var folderWarning = new HelpBox(Strings.InvalidMeshFolder, HelpBoxMessageType.Warning);
            generation.Add(folderWarning);
            generation.Add(Field(data, nameof(AutoColliderSettingsData.ContainerName)));
            generation.Add(Field(data, nameof(AutoColliderSettingsData.DefaultPreset)));
            generation.Add(Field(data, nameof(AutoColliderSettingsData.PreviewTriangleBudget)));

            var sceneView = Section(page, Strings.SectionSceneView);
            sceneView.Add(Field(data, nameof(AutoColliderSettingsData.HandleSize)));
            sceneView.Add(Field(data, nameof(AutoColliderSettingsData.XrayByDefault)));
            sceneView.Add(Field(data, nameof(AutoColliderSettingsData.LockAfterEdit)));
            sceneView.Add(Field(data, nameof(AutoColliderSettingsData.ShowLabels)));
            sceneView.Add(Field(data, nameof(AutoColliderSettingsData.FillOpacity)));

            var colors = Section(page, Strings.SectionColors);
            colors.Add(Field(data, nameof(AutoColliderSettingsData.BoxColor)));
            colors.Add(Field(data, nameof(AutoColliderSettingsData.SphereColor)));
            colors.Add(Field(data, nameof(AutoColliderSettingsData.CapsuleColor)));
            colors.Add(Field(data, nameof(AutoColliderSettingsData.MeshColor)));
            colors.Add(Field(data, nameof(AutoColliderSettingsData.SelectedColor)));

            var footer = new VisualElement();
            footer.AddToClassList("ac-footer");
            var reset = new Button(() => ConfirmReset(settings, serialized)) { text = Strings.ResetToDefaults, tooltip = Strings.ResetToDefaultsTooltip };
            reset.AddToClassList("ac-button");
            footer.Add(reset);
            page.Add(footer);

            void Refresh() => folderWarning.EnableInClassList("ac-hidden", settings.Data.IsMeshFolderValid);

            page.Bind(serialized);
            page.TrackSerializedObjectValue(serialized, _ =>
            {
                settings.SaveSettings();
                Refresh();
            });
            Refresh();
        }

        private static void BrowseMeshFolder(AutoColliderSettings settings, SerializedObject serialized)
        {
            var absolute = EditorUtility.OpenFolderPanel(Strings.BrowseDialogTitle, Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolute))
                return;

            var projectRoot = Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
            var relative = absolute.Replace('\\', '/');
            if (relative.StartsWith(projectRoot + "/"))
                relative = relative.Substring(projectRoot.Length + 1);

            serialized.Update();
            serialized.FindProperty(AutoColliderSettings.DataPropertyPath)
                .FindPropertyRelative(nameof(AutoColliderSettingsData.GeneratedMeshFolder))
                .stringValue = relative;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.SaveSettings();
        }

        private static void ConfirmReset(AutoColliderSettings settings, SerializedObject serialized)
        {
            if (!EditorUtility.DisplayDialog(Strings.ResetConfirmTitle, Strings.ResetConfirmMessage, Strings.Reset, Strings.Cancel))
                return;

            settings.ResetToDefaults();
            serialized.Update();
        }

        private static VisualElement Section(VisualElement parent, string title)
        {
            var section = new VisualElement();
            section.AddToClassList("ac-section");
            section.Add(Label(title, "ac-section__header"));
            parent.Add(section);
            return section;
        }

        private static PropertyField Field(SerializedProperty data, string relativePath)
        {
            return new PropertyField(data.FindPropertyRelative(relativePath));
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}
