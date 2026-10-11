using System;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>"Preset: Balanced  [Presets ▾]" row; opens <see cref="PresetMenu"/>.</summary>
    internal sealed class PresetRow : VisualElement
    {
        private readonly Label _current;

        public PresetRow(Action onChanged)
        {
            AddToClassList("ac-row");
            AddToClassList("ac-preset-row");

            var label = new Label(Strings.PresetLabel);
            label.AddToClassList("ac-preset-row__label");
            Add(label);

            _current = new Label { tooltip = Strings.PresetCurrentTooltip };
            _current.AddToClassList("ac-preset-row__current");
            _current.AddToClassList("ac-row__grow");
            Add(_current);

            var button = new Button(() => PresetMenu.Show(() =>
            {
                onChanged?.Invoke();
                Refresh();
            }))
            {
                text = Strings.PresetButton,
                tooltip = Strings.PresetButtonTooltip,
            };
            button.AddToClassList("ac-button");
            button.AddToClassList("ac-button--small");
            Add(button);

            Refresh();
        }

        public void Refresh() => _current.text = PresetMenu.CurrentName(GenerationSettingsState.instance.Settings);
    }
}
