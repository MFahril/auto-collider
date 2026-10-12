using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Inspector for <see cref="ColliderGroup"/>: summary, actions, collider list and bulk edits.</summary>
    [CustomEditor(typeof(ColliderGroup))]
    [CanEditMultipleObjects]
    internal sealed class ColliderGroupEditor : UnityEditor.Editor
    {
        private const float RowHeight = 22f;

        private readonly List<ColliderEntry> _rows = new();
        private Label _title;
        private Label _breakdown;
        private Label _cost;
        private Button _removeButton;
        private ListView _list;
        private HelpBox _missingBox;
        private Button _clearRegions;
        private Button _addLodFollower;
        private int _stateHash;

        private ColliderGroup Group => (ColliderGroup)target;

        private IEnumerable<ColliderGroup> Groups => targets.OfType<ColliderGroup>();

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            AutoColliderStyles.Apply(root);
            root.AddToClassList("ac-inspector");

            // Summary
            var summary = new VisualElement();
            summary.AddToClassList("ac-summary");
            _title = new Label();
            _title.AddToClassList("ac-summary__title");
            summary.Add(_title);
            _breakdown = new Label();
            _breakdown.AddToClassList("ac-summary__subtitle");
            summary.Add(_breakdown);
            _cost = new Label { tooltip = Strings.CostTooltip };
            _cost.AddToClassList("ac-summary__cost");
            summary.Add(_cost);
            root.Add(summary);

            // Actions
            var actions = new VisualElement();
            actions.AddToClassList("ac-row");
            actions.AddToClassList("ac-row--wrap");
            var regenerate = new Button(Regenerate) { text = Strings.Regenerate, tooltip = Strings.RegenerateTooltip };
            regenerate.AddToClassList("ac-button");
            regenerate.AddToClassList("ac-button--primary");
            actions.Add(regenerate);
            _removeButton = new Button(RemoveGenerated) { tooltip = Strings.RemoveTooltip };
            _removeButton.AddToClassList("ac-button");
            actions.Add(_removeButton);
            var editInScene = new Button(ColliderToolCommands.ActivateTool) { text = Strings.EditInScene, tooltip = Strings.EditInSceneTooltip };
            editInScene.AddToClassList("ac-button");
            actions.Add(editInScene);
            var openWindow = new Button(AutoColliderWindow.Open) { text = Strings.OpenWindow, tooltip = Strings.OpenWindowTooltip };
            openWindow.AddToClassList("ac-button");
            actions.Add(openWindow);
            root.Add(actions);

            if (targets.Length > 1)
            {
                root.Add(new HelpBox(string.Format(Strings.MultiGroupNote, targets.Length), HelpBoxMessageType.Info));
            }

            // Collider list
            var listSection = new VisualElement();
            listSection.AddToClassList("ac-section");
            var listHeader = new VisualElement();
            listHeader.AddToClassList("ac-entry-row");
            listHeader.AddToClassList("ac-entry-row--header");
            listHeader.Add(HeaderLabel(Strings.ColumnCollider, "ac-entry-row__label"));
            listHeader.Add(HeaderLabel(Strings.ColumnOn, "ac-entry-row__toggle"));
            listHeader.Add(HeaderLabel(Strings.ColumnTrigger, "ac-entry-row__toggle"));
            listHeader.Add(HeaderLabel(Strings.ColumnLock, "ac-entry-row__toggle"));
            listHeader.Add(HeaderLabel(string.Empty, "ac-entry-row__select"));
            listSection.Add(listHeader);

            _list = new ListView(_rows, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _list.AddToClassList("ac-entry-list");
            listSection.Add(_list);

            _missingBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            var cleanUp = new Button(CleanUpMissing) { text = Strings.CleanUp, tooltip = Strings.CleanUpTooltip };
            _missingBox.Add(cleanUp);
            listSection.Add(_missingBox);
            root.Add(listSection);

            // Bulk edit
            var bulk = new Foldout { text = Strings.SectionBulk, value = false, viewDataKey = "AutoCollider.Group.Bulk" };
            bulk.AddToClassList("ac-section");
            bulk.AddToClassList("ac-foldout");
            bulk.Add(BulkTrigger());
            bulk.Add(BulkLayer());
            bulk.Add(BulkMaterial());
            bulk.Add(BulkLayerOverrides());
            bulk.Add(BulkContacts());
            root.Add(bulk);

            // Runtime (events, LOD)
            var runtime = new Foldout { text = Strings.SectionRuntime, value = false, viewDataKey = "AutoCollider.Group.Runtime" };
            runtime.AddToClassList("ac-section");
            runtime.AddToClassList("ac-foldout");
            var eventsHint = new Label(Strings.RuntimeEventsHint);
            eventsHint.AddToClassList("ac-hint");
            runtime.Add(eventsHint);
            _addLodFollower = new Button(AddLodFollower) { text = Strings.AddLodFollower, tooltip = Strings.AddLodFollowerTooltip };
            _addLodFollower.AddToClassList("ac-button");
            runtime.Add(_addLodFollower);
            root.Add(runtime);

            // Recipe (read-only)
            var recipe = new Foldout { text = Strings.SectionRecipe, value = false, viewDataKey = "AutoCollider.Group.Recipe" };
            recipe.AddToClassList("ac-section");
            recipe.AddToClassList("ac-foldout");
            var recipeField = new PropertyField(serializedObject.FindProperty("_recipe._settings"), Strings.RecipeSettings);
            recipeField.SetEnabled(false);
            recipe.Add(recipeField);
            _clearRegions = new Button(ClearRegions) { tooltip = Strings.ClearRegionsTooltip };
            _clearRegions.AddToClassList("ac-button");
            recipe.Add(_clearRegions);
            root.Add(recipe);

            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += Refresh;
                ColliderEditState.Changed += RefreshSelection;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= Refresh;
                ColliderEditState.Changed -= RefreshSelection;
            });
            root.schedule.Execute(RefreshIfChanged).Every(500);
            Refresh();
            return root;
        }

        /// <summary>Scene-tool selection changed: re-bind rows (highlight) and pick up rebuilt colliders.</summary>
        private void RefreshSelection()
        {
            if (target == null || _list == null) return;
            Refresh();
        }

        private void RefreshIfChanged()
        {
            if (target == null) return;
            if (ComputeStateHash() != _stateHash) Refresh();
        }

        private void Refresh()
        {
            if (target == null || _list == null) return;
            _stateHash = ComputeStateHash();

            _rows.Clear();
            _rows.AddRange(Group.Entries.Where(e => e.Collider != null));
            _list.style.height = Mathf.Clamp(_rows.Count, 1, 12) * RowHeight + 4;
            _list.RefreshItems();

            var live = Groups.Sum(g => g.LiveCount);
            _title.text = string.Format(live == 1 ? Strings.SummaryOne : Strings.SummaryMany, live);
            _breakdown.text = Breakdown(Groups.SelectMany(g => g.Entries));
            var cost = ColliderCost.Estimate(Groups.SelectMany(g => g.Entries));
            _cost.text = string.Format(Strings.CostLabel, ColliderCost.Meter(cost), ColliderCost.Rating(cost));

            var removable = BatchProcessor.CountRemovable(Groups.Select(g => g.gameObject).ToList());
            _removeButton.text = string.Format(Strings.RemoveButton, removable);
            _removeButton.SetEnabled(removable > 0);

            var missing = Group.Entries.Count(e => e.Collider == null);
            _missingBox.text = string.Format(Strings.MissingEntries, missing);
            _missingBox.EnableInClassList("ac-hidden", missing == 0);

            var regions = Group.Recipe.Regions.Count;
            _clearRegions.text = string.Format(Strings.ClearRegions, regions);
            _clearRegions.EnableInClassList("ac-hidden", regions == 0);

            var canFollow = Groups.Any(g => !g.TryGetComponent<ColliderLodFollower>(out _) && g.GetComponentInChildren<LODGroup>(true) != null);
            _addLodFollower.EnableInClassList("ac-hidden", !canFollow);
        }

        private int ComputeStateHash()
        {
            var hash = 17;
            foreach (var group in Groups)
            foreach (var entry in group.Entries)
            {
                var c = entry.Collider;
                hash = hash * 31 + (c != null ? c.GetEntityId().GetHashCode() : 0);
                hash = hash * 31 + (c != null && c.enabled ? 1 : 0) + (c != null && c.isTrigger ? 2 : 0) + (entry.Locked ? 4 : 0);
            }
            return hash;
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-entry-row");

            var icon = new Image { name = "icon" };
            icon.AddToClassList("ac-entry-row__icon");
            row.Add(icon);
            var label = new Label { name = "label" };
            label.AddToClassList("ac-entry-row__label");
            label.RegisterCallback<ClickEvent>(e =>
            {
                if (!(row.userData is ColliderEntry entry) || entry.Collider == null) return;
                var op = e.shiftKey ? SelectOp.Add : e.actionKey ? SelectOp.Toggle : SelectOp.Replace;
                ColliderEditState.Select(entry.Collider, op);
                ColliderToolCommands.ActivateTool();
            });
            row.Add(label);
            var kind = new Label { name = "kind" };
            kind.AddToClassList("ac-chip");
            row.Add(kind);

            row.Add(EntryToggle("enabled", Strings.ToggleEnabledTooltip));
            row.Add(EntryToggle("trigger", Strings.ToggleTriggerTooltip));
            row.Add(EntryToggle("locked", Strings.ToggleLockTooltip));

            var select = new Button { name = "select", text = Strings.Select, tooltip = Strings.SelectTooltip };
            select.AddToClassList("ac-button");
            select.AddToClassList("ac-button--small");
            select.AddToClassList("ac-entry-row__select");
            select.clicked += () =>
            {
                if (!(row.userData is ColliderEntry entry) || entry.Collider == null) return;
                // Keep the group selected (the tool needs it) and select the collider inside the tool instead.
                ColliderEditState.Select(entry.Collider);
                ColliderToolCommands.ActivateTool();
                ColliderToolCommands.FrameSelection(SceneView.lastActiveSceneView);
            };
            row.Add(select);
            return row;
        }

        private static Toggle EntryToggle(string name, string tooltip)
        {
            var toggle = new Toggle { name = name, tooltip = tooltip };
            toggle.AddToClassList("ac-entry-row__toggle");
            return toggle;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _rows.Count) return;
            var entry = _rows[index];
            var collider = entry.Collider;
            row.userData = entry;
            row.EnableInClassList("ac-entry-row--selected", ColliderEditState.IsSelected(collider));

            row.Q<Image>("icon").image = AssetPreview.GetMiniTypeThumbnail(collider.GetType());
            var label = row.Q<Label>("label");
            label.text = string.IsNullOrEmpty(entry.Label) ? collider.gameObject.name : entry.Label;
            label.tooltip = entry.Generated ? Strings.EntryGeneratedTooltip : Strings.EntryManualTooltip;
            var kindText = ColliderCost.KindName(collider);
            row.Q<Label>("kind").text = entry.Lod >= 0 ? string.Format(Strings.EntryKindLod, kindText, entry.Lod) : kindText;

            Rebind(row.Q<Toggle>("enabled"), collider.enabled, value =>
            {
                Undo.RecordObject(collider, Strings.UndoEditCollider);
                collider.enabled = value;
            });

            var trigger = row.Q<Toggle>("trigger");
            var canTrigger = !(collider is MeshCollider { convex: false });
            trigger.SetEnabled(canTrigger);
            Rebind(trigger, collider.isTrigger, value =>
            {
                Undo.RecordObject(collider, Strings.UndoEditCollider);
                collider.isTrigger = value;
            });

            Rebind(row.Q<Toggle>("locked"), entry.Locked, value =>
            {
                Undo.RecordObject(Group, Strings.UndoEditCollider);
                entry.Locked = value;
                EditorUtility.SetDirty(Group);
                Refresh();
            });
        }

        private static void Rebind(Toggle toggle, bool value, System.Action<bool> onChange)
        {
            if (toggle.userData is EventCallback<ChangeEvent<bool>> previous)
                toggle.UnregisterValueChangedCallback(previous);
            toggle.SetValueWithoutNotify(value);
            EventCallback<ChangeEvent<bool>> callback = e => onChange(e.newValue);
            toggle.userData = callback;
            toggle.RegisterValueChangedCallback(callback);
        }

        private void Regenerate()
        {
            foreach (var group in Groups.ToList())
            {
                var settings = group.Recipe.Settings.Clone();
                if (settings.Replace == ReplaceMode.Append) settings.Replace = ReplaceMode.ReplaceGenerated;
                GenerateCommands.Generate(new[] { group.gameObject }, settings);
            }
            Refresh();
        }

        private void RemoveGenerated()
        {
            GenerateCommands.Remove(Groups.Select(g => g.gameObject).ToList());
        }

        private void ClearRegions()
        {
            foreach (var group in Groups.ToList())
                if (group.Recipe.HasRegions) ColliderOperations.ClearRegions(group);
            ColliderEditState.Prune();
            Refresh();
        }

        private void CleanUpMissing()
        {
            foreach (var group in Groups)
            {
                Undo.RecordObject(group, Strings.UndoEditCollider);
                group.RemoveMissing();
                EditorUtility.SetDirty(group);
            }
            Refresh();
        }

        private VisualElement BulkTrigger()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            // Concave mesh colliders can't be triggers; leave them unchanged.
            var on = new Button(() => ApplyToColliders(c =>
                {
                    if (!(c is MeshCollider { convex: false })) c.isTrigger = true;
                }))
                { text = Strings.BulkTriggerOn, tooltip = Strings.BulkTriggerTooltip };
            var off = new Button(() => ApplyToColliders(c => c.isTrigger = false)) { text = Strings.BulkTriggerOff, tooltip = Strings.BulkTriggerTooltip };
            foreach (var b in new[] { on, off })
            {
                b.AddToClassList("ac-button");
                row.Add(b);
            }
            return row;
        }

        private VisualElement BulkLayer()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            var layer = new LayerField(Strings.BulkLayer) { tooltip = Strings.BulkLayerTooltip };
            layer.AddToClassList("ac-row__grow");
            row.Add(layer);
            var apply = new Button(() => ApplyLayer(layer.value)) { text = Strings.Apply, tooltip = Strings.BulkLayerTooltip };
            apply.AddToClassList("ac-button");
            row.Add(apply);
            return row;
        }

        private VisualElement BulkMaterial()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            var material = new ObjectField(Strings.BulkMaterial) { objectType = typeof(PhysicsMaterial), tooltip = Strings.BulkMaterialTooltip };
            material.AddToClassList("ac-row__grow");
            row.Add(material);
            var apply = new Button(() => ApplyToColliders(c => c.sharedMaterial = material.value as PhysicsMaterial))
                { text = Strings.Apply, tooltip = Strings.BulkMaterialTooltip };
            apply.AddToClassList("ac-button");
            row.Add(apply);
            return row;
        }

        private VisualElement BulkLayerOverrides()
        {
            var box = new VisualElement();
            var include = new LayerMaskField(Strings.BulkIncludeLayers) { tooltip = Strings.BulkIncludeLayersTooltip };
            var exclude = new LayerMaskField(Strings.BulkExcludeLayers) { tooltip = Strings.BulkExcludeLayersTooltip };
            var priority = new IntegerField(Strings.BulkLayerPriority) { tooltip = Strings.BulkLayerPriorityTooltip };
            var first = Group.Entries.Select(e => e.Collider).FirstOrDefault(c => c != null);
            if (first != null)
            {
                include.SetValueWithoutNotify(first.includeLayers);
                exclude.SetValueWithoutNotify(first.excludeLayers);
                priority.SetValueWithoutNotify(first.layerOverridePriority);
            }
            box.Add(include);
            box.Add(exclude);
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            priority.AddToClassList("ac-row__grow");
            row.Add(priority);
            var apply = new Button(() => ApplyToColliders(c =>
                {
                    c.includeLayers = include.value;
                    c.excludeLayers = exclude.value;
                    c.layerOverridePriority = priority.value;
                }))
                { text = Strings.Apply, tooltip = Strings.BulkLayerOverridesTooltip };
            apply.AddToClassList("ac-button");
            row.Add(apply);
            box.Add(row);
            return box;
        }

        private VisualElement BulkContacts()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-row");
            var on = new Button(() => ApplyToColliders(c => c.providesContacts = true)) { text = Strings.BulkContactsOn, tooltip = Strings.BulkContactsTooltip };
            var off = new Button(() => ApplyToColliders(c => c.providesContacts = false)) { text = Strings.BulkContactsOff, tooltip = Strings.BulkContactsTooltip };
            foreach (var b in new[] { on, off })
            {
                b.AddToClassList("ac-button");
                row.Add(b);
            }
            return row;
        }

        private void AddLodFollower()
        {
            foreach (var group in Groups)
            {
                if (group.TryGetComponent<ColliderLodFollower>(out _)) continue;
                var lodGroup = group.GetComponentInChildren<LODGroup>(true);
                if (lodGroup == null) continue;
                var follower = Undo.AddComponent<ColliderLodFollower>(group.gameObject);
                Undo.RecordObject(follower, Strings.UndoAddLodFollower);
                follower.LodGroup = lodGroup;
            }
            Refresh();
        }

        private void ApplyToColliders(System.Action<Collider> change)
        {
            var colliders = Groups.SelectMany(g => g.Entries).Select(e => e.Collider).Where(c => c != null).ToArray();
            Undo.RecordObjects(colliders, Strings.UndoEditCollider);
            foreach (var c in colliders) change(c);
            Refresh();
        }

        private void ApplyLayer(int layer)
        {
            var objects = Groups.SelectMany(g => g.Entries).Select(e => e.Collider).Where(c => c != null)
                .Select(c => c.gameObject).Where(go => go.GetComponent<Renderer>() == null).Distinct().ToList();
            foreach (var group in Groups)
                if (group.Container != null) objects.Add(group.Container.gameObject);
            objects = objects.Distinct().ToList();

            Undo.RecordObjects(objects.ToArray(), Strings.UndoEditCollider);
            foreach (var go in objects) go.layer = layer;
        }

        private static string Breakdown(IEnumerable<ColliderEntry> entries)
        {
            var counts = entries.Where(e => e.Collider != null)
                .GroupBy(e => ColliderCost.KindName(e.Collider))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");
            var text = string.Join(" · ", counts);
            return string.IsNullOrEmpty(text) ? Strings.SummaryEmpty : text;
        }

        private static Label HeaderLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}
