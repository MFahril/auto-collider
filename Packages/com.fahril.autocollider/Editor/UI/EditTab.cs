using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using static AutoCollider.Editor.UiFactory;

namespace AutoCollider.Editor
{
    /// <summary>Edit tab: entry point to the Scene-view collider tool, with the selected groups and a shortcut cheat sheet.</summary>
    internal sealed class EditTab : VisualElement
    {
        private readonly Label _status;
        private readonly Button _enter;

        public EditTab()
        {
            AddToClassList("ac-tab");

            var intro = Section(this, null);
            var text = new Label(Strings.EditTabIntro);
            text.AddToClassList("ac-subtitle");
            intro.Add(text);
            _status = new Label();
            _status.AddToClassList("ac-result__summary");
            intro.Add(_status);
            _enter = new Button(ColliderToolCommands.ActivateTool) { text = Strings.EditTabEnter, tooltip = Strings.EditTabEnterTooltip };
            _enter.AddToClassList("ac-button");
            _enter.AddToClassList("ac-button--primary");
            intro.Add(_enter);

            var shortcuts = Section(this, Strings.EditTabShortcuts);
            var list = new Label(Strings.EditTabShortcutList);
            list.AddToClassList("ac-shortcuts");
            shortcuts.Add(list);

            RegisterCallback<AttachToPanelEvent>(_ => Selection.selectionChanged += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => Selection.selectionChanged -= Refresh);
            Refresh();
        }

        private void Refresh()
        {
            var groups = Selection.gameObjects.Select(g => g.GetComponent<ColliderGroup>()).Where(g => g != null).ToList();
            _status.text = groups.Count == 0 ? Strings.EditTabNoGroup : string.Format(Strings.EditTabGroups, groups.Count, groups.Sum(g => g.LiveCount));
            _enter.SetEnabled(groups.Count > 0);
        }
    }
}
