using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Inspector for <see cref="SpriteColliderSwitcher"/>: settings, bake / clear buttons and a summary of what is baked.</summary>
    [CustomEditor(typeof(SpriteColliderSwitcher))]
    [CanEditMultipleObjects]
    internal sealed class SpriteColliderSwitcherEditor : UnityEditor.Editor
    {
        private HelpBox _missingCollider;
        private HelpBox _unbaked;
        private Label _summary;
        private Button _clear;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            AutoColliderStyles.Apply(root);
            root.AddToClassList("ac-inspector");

            root.Add(new PropertyField(serializedObject.FindProperty("_collider")));
            root.Add(new PropertyField(serializedObject.FindProperty("_extraSprites")));

            _missingCollider = new HelpBox(Strings.SwitcherMissingCollider, HelpBoxMessageType.Info);
            root.Add(_missingCollider);
            _unbaked = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            root.Add(_unbaked);

            var buttons = new VisualElement();
            buttons.AddToClassList("ac-row");
            var bake = new Button(Bake) { text = Strings.SwitcherBake, tooltip = Strings.SwitcherBakeTooltip };
            bake.AddToClassList("ac-button");
            bake.AddToClassList("ac-button--primary");
            buttons.Add(bake);
            _clear = new Button(Clear) { tooltip = Strings.SwitcherClearTooltip };
            _clear.AddToClassList("ac-button");
            buttons.Add(_clear);
            root.Add(buttons);

            _summary = new Label { tooltip = Strings.SwitcherHint };
            _summary.AddToClassList("ac-hint");
            root.Add(_summary);
            var hint = new Label(Strings.SwitcherHint) { tooltip = Strings.SwitcherHint };
            hint.AddToClassList("ac-hint");
            root.Add(hint);

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.schedule.Execute(Refresh).Every(1000);
            Refresh();
            return root;
        }

        private void Refresh()
        {
            if (target == null || _summary == null) return;
            var switcher = (SpriteColliderSwitcher)target;
            Show(_missingCollider, switcher.Collider == null);
            var unbaked = SpriteColliderBaker.CountUnbaked(switcher);
            _unbaked.text = string.Format(Strings.SwitcherUnbaked, unbaked);
            Show(_unbaked, unbaked > 0);
            _summary.text = string.Format(Strings.SwitcherSummary, switcher.ShapeCount, switcher.PointCount);
            _clear.text = string.Format(Strings.SwitcherClear, switcher.ShapeCount);
            _clear.SetEnabled(switcher.ShapeCount > 0);
        }

        private void Bake()
        {
            var settings = GenerationSettingsState.instance.Settings;
            foreach (var t in targets) SpriteColliderBaker.Bake((SpriteColliderSwitcher)t, settings);
            serializedObject.Update();
            Refresh();
        }

        private void Clear()
        {
            foreach (var t in targets)
            {
                var switcher = (SpriteColliderSwitcher)t;
                Undo.RecordObject(switcher, Strings.UndoClearSprites);
                switcher.ClearShapes();
                if (PrefabUtility.IsPartOfPrefabInstance(switcher)) PrefabUtility.RecordPrefabInstancePropertyModifications(switcher);
            }
            serializedObject.Update();
            Refresh();
        }

        private static void Show(VisualElement element, bool visible) => element.EnableInClassList("ac-hidden", !visible);
    }
}
