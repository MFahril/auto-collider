using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AutoCollider.Editor.Tests
{
    /// <summary>M7 UI polish guard: every interactive control the plugin builds has a tooltip, and every root is styled.</summary>
    public class UiTooltipTests
    {
        private TestScene _scene;

        [SetUp]
        public void SetUp() => _scene = new TestScene();

        [TearDown]
        public void TearDown() => _scene.Cleanup();

        private static bool IsControl(VisualElement e)
        {
            if (e is Button) return true;
            for (var t = e.GetType(); t != null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BaseField<>)) return true;
            return false;
        }

        /// <summary>
        /// Controls we create. Parts generated inside other controls (a slider's input box, a foldout's toggle, fields inside
        /// a PropertyField — those get their tooltip from the [Tooltip] attribute when bound) are skipped.
        /// </summary>
        private static IEnumerable<VisualElement> OwnControls(VisualElement root)
        {
            var stack = new Stack<VisualElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var e = stack.Pop();
                if (e is PropertyField) continue;
                if (e != root && IsControl(e)) yield return e;
                if (IsControl(e) && e != root) continue;
                foreach (var child in e.Children())
                    if (!child.ClassListContains(Foldout.toggleUssClassName)) stack.Push(child);
            }
        }

        private static void AssertTooltips(VisualElement root, string where)
        {
            var missing = OwnControls(root)
                .Where(c => string.IsNullOrEmpty(c.tooltip))
                .Select(c => $"{c.GetType().Name} '{(c as Button)?.text ?? (c as BaseField<bool>)?.label ?? c.name}'")
                .ToList();
            Assert.That(missing, Is.Empty, $"{where}: controls without a tooltip");
        }

        [Test]
        public void WindowTabs_EveryControlHasATooltip()
        {
            var tabs = new (string name, Func<VisualElement> build)[]
            {
                ("Generate", () => new GenerateTab()),
                ("Optimize", () => new OptimizeTab()),
                ("Edit", () => new EditTab()),
                ("Doctor", () => new DoctorTab()),
            };
            foreach (var (name, build) in tabs) AssertTooltips(build(), name);
        }

        [Test]
        public void Inspectors_EveryControlHasATooltip_AndAreStyled()
        {
            var go = new GameObject("Inspected");
            var group = go.AddComponent<ColliderGroup>();
            group.AddEntry(new ColliderEntry(go.AddComponent<BoxCollider>(), "Box", 0, true));
            go.AddComponent<LODGroup>();
            var animated = go.AddComponent<AnimatedMeshCollider>();

            foreach (var component in new Object[] { group, animated })
            {
                var editor = UnityEditor.Editor.CreateEditor(component);
                try
                {
                    var root = editor.CreateInspectorGUI();
                    Assert.That(root, Is.Not.Null);
                    Assert.That(root.ClassListContains("ac-root"), component.GetType().Name + " applies the shared styles");
                    AssertTooltips(root, component.GetType().Name);
                }
                finally
                {
                    Object.DestroyImmediate(editor);
                }
            }
        }

        [Test]
        public void ProjectSettingsPage_EveryControlHasATooltip()
        {
            var root = new VisualElement();
            AutoColliderSettingsProvider.Create().activateHandler(string.Empty, root);
            AssertTooltips(root, "Project Settings");
        }

        [Test]
        public void SceneOverlay_EveryControlHasATooltip()
        {
            var overlay = new ColliderOverlay();
            AssertTooltips(overlay.CreatePanelContent(), "Collider overlay");
        }
    }
}
