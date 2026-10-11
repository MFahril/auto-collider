using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static AutoCollider.Editor.UiFactory;

namespace AutoCollider.Editor
{
    /// <summary>Generate tab: follows the Hierarchy selection, shows per-object status, settings and actions.</summary>
    internal sealed class GenerateTab : VisualElement
    {
        private const int PreviewDebounceMs = 150;

        private readonly GenerationSettingsState _state = GenerationSettingsState.instance;
        private readonly SerializedObject _serialized;
        private readonly List<GameObject> _targets = new();
        private readonly Dictionary<GameObject, TargetResult> _lastResults = new();

        private readonly Label _targetsHeader;
        private readonly ListView _targetList;
        private readonly Label _emptyState;
        private readonly Label _strategyHint;
        private readonly Toggle _livePreview;
        private readonly Label _previewHint;
        private readonly Button _generateButton;
        private readonly Button _removeButton;
        private readonly VisualElement _resultBox;
        private readonly Label _resultLabel;
        private readonly Foldout _resultDetails;
        private readonly Label _resultStats;
        private readonly PresetRow _presetRow;
        private readonly Foldout _meshOptimization;
        private IVisualElementScheduledItem _pendingPreview;

        public GenerateTab()
        {
            AddToClassList("ac-tab");
            _serialized = new SerializedObject(_state);
            var settings = _serialized.FindProperty(GenerationSettingsState.SettingsPropertyPath);

            // Targets
            var targetsSection = Section(this, null);
            var header = Row(targetsSection);
            _targetsHeader = new Label { tooltip = Strings.TargetsTooltip };
            _targetsHeader.AddToClassList("ac-section__header");
            _targetsHeader.AddToClassList("ac-row__grow");
            header.Add(_targetsHeader);

            _emptyState = new Label(Strings.TargetsEmpty);
            _emptyState.AddToClassList("ac-empty");
            targetsSection.Add(_emptyState);

            _targetList = new ListView(_targets, 22, MakeTargetRow, BindTargetRow)
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _targetList.AddToClassList("ac-target-list");
            targetsSection.Add(_targetList);

            // Settings
            _presetRow = new PresetRow(OnPresetApplied);
            Add(_presetRow);

            var shapeSection = Section(this, Strings.SectionShape);
            shapeSection.Add(Field(settings, nameof(GenerationSettings.Strategy)));
            _strategyHint = new Label();
            _strategyHint.AddToClassList("ac-hint");
            shapeSection.Add(_strategyHint);
            shapeSection.Add(Field(settings, nameof(GenerationSettings.Granularity)));
            shapeSection.Add(Field(settings, nameof(GenerationSettings.AllowRotation)));
            shapeSection.Add(Field(settings, nameof(GenerationSettings.Inflate)));

            _meshOptimization = Foldout(this, Strings.SectionMeshOptimization, "AutoCollider.Generate.MeshOptimization");
            var meshHint = new Label(Strings.MeshOptimizationHint);
            meshHint.AddToClassList("ac-hint");
            _meshOptimization.Add(meshHint);
            _meshOptimization.Add(Field(settings, nameof(GenerationSettings.Quality)));
            _meshOptimization.Add(Field(settings, nameof(GenerationSettings.ShapeFidelity)));
            _meshOptimization.Add(Field(settings, nameof(GenerationSettings.MaxTriangles)));
            _meshOptimization.Add(Field(settings, nameof(GenerationSettings.MinIslandSize)));

            var decomposition = Foldout(this, Strings.SectionDecomposition, "AutoCollider.Generate.Decomposition");
            var decompositionHint = new Label(Strings.DecompositionHint);
            decompositionHint.AddToClassList("ac-hint");
            decomposition.Add(decompositionHint);
            decomposition.Add(Field(settings, nameof(GenerationSettings.DecompositionResolution)));
            decomposition.Add(Field(settings, nameof(GenerationSettings.MaxParts)));
            decomposition.Add(Field(settings, nameof(GenerationSettings.ConcavityTolerance)));
            decomposition.Add(Field(settings, nameof(GenerationSettings.PrimitiveFitThreshold)));
            decomposition.Add(Field(settings, nameof(GenerationSettings.ConvexityThreshold)));

            var filters = Foldout(this, Strings.SectionFilters, "AutoCollider.Generate.Filters");
            filters.Add(Field(settings, nameof(GenerationSettings.IncludeChildren)));
            filters.Add(Field(settings, nameof(GenerationSettings.IncludeInactive)));
            filters.Add(Field(settings, nameof(GenerationSettings.Lod0Only)));
            filters.Add(Field(settings, nameof(GenerationSettings.MinSize)));
            filters.Add(Field(settings, nameof(GenerationSettings.ExcludeNamePattern)));

            var output = Foldout(this, Strings.SectionOutput, "AutoCollider.Generate.Output");
            output.Add(Field(settings, nameof(GenerationSettings.Replace)));
            output.Add(Field(settings, nameof(GenerationSettings.IsTrigger)));
            output.Add(Field(settings, nameof(GenerationSettings.Material)));

            var resetRow = Row(this);
            resetRow.AddToClassList("ac-footer");
            var reset = new Button(ResetSettings) { text = Strings.ResetToDefaults, tooltip = Strings.ResetGenerationTooltip };
            reset.AddToClassList("ac-button");
            reset.AddToClassList("ac-button--small");
            resetRow.Add(reset);

            // Actions
            var actions = Section(this, null);
            actions.AddToClassList("ac-actions");
            var previewRow = Row(actions);
            _livePreview = new Toggle(Strings.LivePreview) { tooltip = Strings.LivePreviewTooltip };
            _livePreview.RegisterValueChangedCallback(_ => SchedulePreview());
            previewRow.Add(_livePreview);
            _previewHint = new Label();
            _previewHint.AddToClassList("ac-hint");
            _previewHint.AddToClassList("ac-row__grow");
            previewRow.Add(_previewHint);

            var buttons = Row(actions);
            _generateButton = new Button(Generate) { tooltip = Strings.GenerateTooltip };
            _generateButton.AddToClassList("ac-button");
            _generateButton.AddToClassList("ac-button--primary");
            _generateButton.AddToClassList("ac-row__grow");
            buttons.Add(_generateButton);
            _removeButton = new Button(Remove) { tooltip = Strings.RemoveTooltip };
            _removeButton.AddToClassList("ac-button");
            buttons.Add(_removeButton);

            _resultBox = new VisualElement();
            _resultBox.AddToClassList("ac-result");
            _resultLabel = new Label();
            _resultLabel.AddToClassList("ac-result__summary");
            _resultBox.Add(_resultLabel);
            _resultStats = new Label();
            _resultStats.AddToClassList("ac-result__line");
            _resultBox.Add(_resultStats);
            _resultDetails = new Foldout { text = Strings.ResultDetails, value = false };
            _resultBox.Add(_resultDetails);
            actions.Add(_resultBox);
            _resultBox.AddToClassList("ac-hidden");

            this.Bind(_serialized);
            this.TrackSerializedObjectValue(_serialized, _ =>
            {
                _state.SaveState();
                RefreshTargets();
            });

            RegisterCallback<AttachToPanelEvent>(_ => Subscribe(true));
            RegisterCallback<DetachFromPanelEvent>(_ => Subscribe(false));
            RefreshTargets();
        }

        private GenerationSettings Settings => _state.Settings;

        private void Subscribe(bool on)
        {
            Selection.selectionChanged -= RefreshTargets;
            EditorApplication.hierarchyChanged -= RefreshTargets;
            Undo.undoRedoPerformed -= RefreshTargets;
            if (!on)
            {
                PreviewDrawer.Clear();
                return;
            }
            Selection.selectionChanged += RefreshTargets;
            EditorApplication.hierarchyChanged += RefreshTargets;
            Undo.undoRedoPerformed += RefreshTargets;
            RefreshTargets();
        }

        private void RefreshTargets()
        {
            _targets.Clear();
            _targets.AddRange(TargetResolver.FromSelection(Settings.IncludeChildren));
            _targetList.RefreshItems();

            var hasTargets = _targets.Count > 0;
            _targetsHeader.text = string.Format(Strings.TargetsHeader, _targets.Count);
            _emptyState.EnableInClassList("ac-hidden", hasTargets);
            _targetList.EnableInClassList("ac-hidden", !hasTargets);
            _targetList.style.height = Mathf.Clamp(_targets.Count, 1, 8) * 22 + 4;

            _generateButton.text = string.Format(Strings.GenerateButton, _targets.Count);
            _generateButton.SetEnabled(hasTargets);
            var removable = BatchProcessor.CountRemovable(_targets);
            _removeButton.text = string.Format(Strings.RemoveButton, removable);
            _removeButton.SetEnabled(removable > 0);

            _strategyHint.text = Strings.StrategyHint(Settings.Strategy);
            _presetRow.Refresh();
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            _pendingPreview?.Pause();
            if (!_livePreview.value)
            {
                PreviewDrawer.Clear();
                _previewHint.text = string.Empty;
                return;
            }
            _pendingPreview = schedule.Execute(UpdatePreview).StartingIn(PreviewDebounceMs);
        }

        private void UpdatePreview()
        {
            var budget = AutoColliderSettings.instance.Data.PreviewTriangleBudget;
            var triangles = 0;
            foreach (var target in _targets)
            foreach (var renderer in MeshSource.CollectRenderers(target, Settings, null))
                triangles += TriangleCount(MeshSource.SharedMesh(renderer));

            if (triangles > budget)
            {
                PreviewDrawer.Clear();
                _previewHint.text = string.Format(Strings.PreviewOverBudget, triangles, budget);
                return;
            }

            var results = new List<TargetResult>();
            var shapes = 0;
            var stats = new ColliderStats();
            foreach (var target in _targets)
            {
                var result = GenerationPipeline.Compute(target, Settings);
                results.Add(result);
                shapes += result.Shapes.Count;
                if (result.HasMeshStats) stats.Add(result.Stats);
            }
            PreviewDrawer.Show(results);
            var statsText = GenerateCommands.DescribeStats(stats);
            _previewHint.text = string.Format(Strings.PreviewSummary, shapes)
                                + (statsText.Length > 0 ? "\n" + statsText : string.Empty);
        }

        private void OnPresetApplied()
        {
            _serialized.Update();
            RefreshTargets();
        }

        private void Generate()
        {
            var report = GenerateCommands.Generate(_targets, Settings.Clone());
            if (report != null) ShowReport(report);
            RefreshTargets();
        }

        private void Remove()
        {
            var report = GenerateCommands.Remove(_targets);
            ShowReport(report, string.Format(Strings.NotifyRemoved, report.CollidersRemoved, report.TargetsChanged));
            RefreshTargets();
        }

        private void ResetSettings()
        {
            _state.ResetToDefaults();
            _serialized.Update();
            RefreshTargets();
        }

        private void ShowReport(BatchReport report, string summary = null)
        {
            _lastResults.Clear();
            foreach (var result in report.Results)
                if (result.Target != null) _lastResults[result.Target] = result;

            _resultBox.RemoveFromClassList("ac-hidden");
            _resultLabel.text = summary ?? GenerateCommands.Describe(report);
            _resultStats.text = GenerateCommands.DescribeStats(report.Stats);
            _resultStats.EnableInClassList("ac-hidden", _resultStats.text.Length == 0);
            _resultDetails.Clear();
            foreach (var result in report.Results)
            {
                if (result.Status == TargetStatus.Ok && result.Notes.Count == 0) continue;
                var name = result.Target != null ? result.Target.name : "?";
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(result.Message)) lines.Add(result.Message);
                lines.AddRange(result.Warnings);
                lines.AddRange(result.Notes);
                var line = new Label($"{name}: {string.Join(" ", lines)}");
                line.AddToClassList(StatusClass(result.Status));
                line.AddToClassList("ac-result__line");
                _resultDetails.Add(line);
            }
            _resultDetails.EnableInClassList("ac-hidden", _resultDetails.childCount == 0);
            _targetList.RefreshItems();
        }

        private VisualElement MakeTargetRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-target-row");
            var icon = new Image { name = "icon" };
            icon.AddToClassList("ac-target-row__icon");
            row.Add(icon);
            var title = new Label { name = "name" };
            title.AddToClassList("ac-target-row__name");
            row.Add(title);
            var info = new Label { name = "info" };
            info.AddToClassList("ac-target-row__info");
            row.Add(info);
            var status = new Label { name = "status" };
            status.AddToClassList("ac-chip");
            row.Add(status);
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2 && row.userData is GameObject go) EditorGUIUtility.PingObject(go);
            });
            return row;
        }

        private void BindTargetRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _targets.Count) return;
            var go = _targets[index];
            row.userData = go;
            row.tooltip = Strings.TargetRowTooltip;
            row.Q<Image>("icon").image = AssetPreview.GetMiniTypeThumbnail(typeof(GameObject));
            row.Q<Label>("name").text = go.name;

            var renderers = MeshSource.CollectRenderers(go, Settings, null).Count;
            var existing = go.TryGetComponent<ColliderGroup>(out var group) ? group.LiveCount : 0;
            row.Q<Label>("info").text = existing > 0
                ? string.Format(Strings.TargetInfoWithColliders, renderers, existing)
                : string.Format(Strings.TargetInfo, renderers);

            var status = row.Q<Label>("status");
            foreach (var s in new[] { TargetStatus.Ok, TargetStatus.Warning, TargetStatus.Skipped, TargetStatus.Error })
                status.RemoveFromClassList(StatusClass(s));

            if (_lastResults.TryGetValue(go, out var result))
            {
                status.text = result.Status switch
                {
                    TargetStatus.Ok => string.Format(Strings.StatusCreated, result.CollidersCreated),
                    TargetStatus.Warning => Strings.StatusWarning,
                    TargetStatus.Skipped => Strings.StatusSkipped,
                    _ => Strings.StatusError,
                };
                var tips = new List<string>(result.Warnings);
                tips.AddRange(result.Notes);
                if (!string.IsNullOrEmpty(result.Message)) tips.Add(result.Message);
                status.tooltip = string.Join("\n", tips);
                status.AddToClassList(StatusClass(result.Status));
                status.RemoveFromClassList("ac-hidden");
            }
            else if (renderers == 0)
            {
                status.text = Strings.StatusNoMesh;
                status.tooltip = Strings.SkipNoMeshes;
                status.AddToClassList(StatusClass(TargetStatus.Skipped));
                status.RemoveFromClassList("ac-hidden");
            }
            else
            {
                status.AddToClassList("ac-hidden");
            }
        }

        private static string StatusClass(TargetStatus status) => status switch
        {
            TargetStatus.Ok => "ac-status--ok",
            TargetStatus.Warning => "ac-status--warning",
            TargetStatus.Skipped => "ac-status--muted",
            _ => "ac-status--error",
        };

        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (var i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
            return (int)(indices / 3);
        }
    }
}
