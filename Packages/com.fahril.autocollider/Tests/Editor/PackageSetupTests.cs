using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M0 guards: package wiring, assembly boundaries, settings and styles.</summary>
    public class PackageSetupTests
    {
        [Test]
        public void RuntimeAssembly_DoesNotReferenceUnityEditor()
        {
            var references = typeof(PhysicsLimits).Assembly.GetReferencedAssemblies();

            Assert.That(references.Select(r => r.Name), Has.None.StartsWith("UnityEditor"),
                "AutoCollider.Runtime must stay free of UnityEditor so player builds compile.");
        }

        [Test]
        public void PackageInfo_MatchesPackagePaths()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePaths).Assembly);

            Assert.That(info, Is.Not.Null);
            Assert.That(info.name, Is.EqualTo(PackagePaths.PackageName));
            Assert.That(info.assetPath, Is.EqualTo(PackagePaths.Root));
        }

        [Test]
        public void MainStyleSheet_LoadsFromPackage()
        {
            Assert.That(AutoColliderStyles.MainStyleSheet, Is.Not.Null, PackagePaths.MainStyleSheet);
        }

        [Test]
        public void Styles_Apply_AddsSheetAndExactlyOneSkinClass()
        {
            var root = new VisualElement();

            AutoColliderStyles.Apply(root);
            AutoColliderStyles.Apply(root);

            Assert.That(root.styleSheets.count, Is.EqualTo(1));
            Assert.That(root.ClassListContains(AutoColliderStyles.RootClass));
            Assert.That(root.ClassListContains(AutoColliderStyles.DarkSkinClass)
                        ^ root.ClassListContains(AutoColliderStyles.LightSkinClass));
        }

        [Test]
        public void Settings_InstanceIsEditableAndHasData()
        {
            var settings = AutoColliderSettings.instance;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.Data, Is.Not.Null);
            Assert.That(settings.hideFlags & UnityEngine.HideFlags.NotEditable, Is.EqualTo(UnityEngine.HideFlags.None));
        }

        [Test]
        public void SettingsData_DefaultsAreValid()
        {
            var defaults = new AutoColliderSettingsData();

            Assert.That(defaults.IsMeshFolderValid);
            Assert.That(defaults.ResolvedContainerName, Is.EqualTo(AutoColliderSettingsData.DefaultContainerName));
            Assert.That(defaults.PreviewTriangleBudget, Is.GreaterThan(0));
            Assert.That(defaults.FillOpacity, Is.InRange(0f, 1f));
        }

        [TestCase("Assets", true)]
        [TestCase("Assets/AutoCollider/Generated", true)]
        [TestCase("Packages/com.fahril.autocollider", false)]
        [TestCase("Assets/../Library", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void SettingsData_MeshFolderValidation(string path, bool expected)
        {
            Assert.That(AutoColliderSettingsData.IsValidMeshFolder(path), Is.EqualTo(expected));
        }

        [Test]
        public void SettingsData_BlankContainerName_FallsBackToDefault()
        {
            var data = new AutoColliderSettingsData { ContainerName = "  " };

            Assert.That(data.ResolvedContainerName, Is.EqualTo(AutoColliderSettingsData.DefaultContainerName));
        }

        [Test]
        public void SettingsSerializedPropertyPaths_Resolve()
        {
            var serialized = new SerializedObject(AutoColliderSettings.instance);
            var data = serialized.FindProperty(AutoColliderSettings.DataPropertyPath);

            Assert.That(data, Is.Not.Null);
            foreach (var field in typeof(AutoColliderSettingsData).GetFields()
                         .Where(f => !f.IsLiteral && !f.IsStatic))
                Assert.That(data.FindPropertyRelative(field.Name), Is.Not.Null, field.Name);
        }

        [Test]
        public void SettingsProvider_IsRegisteredAtExpectedPath()
        {
            var provider = AutoColliderSettingsProvider.Create();

            Assert.That(provider.settingsPath, Is.EqualTo(Strings.SettingsPath));
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
        }

        [Test]
        public void PhysicsLimits_MatchPhysX()
        {
            Assert.That(PhysicsLimits.MaxConvexPolygons, Is.EqualTo(255));
            Assert.That(PhysicsLimits.MinHullVertices, Is.EqualTo(4));
        }
    }
}
