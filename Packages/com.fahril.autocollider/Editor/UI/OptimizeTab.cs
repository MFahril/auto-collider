using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static AutoCollider.Editor.UiFactory;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Optimize tab: lists existing MeshColliders (selection or whole scene), analyzes cheaper versions with before/after
    /// statistics and an error heatmap, applies them in one Undo step, and restores originals.
    /// </summary>
    internal sealed class OptimizeTab : VisualElement
    {
        private const float RowHeight = 22f;

        private readonly GenerationSettingsState _state = GenerationSettingsState.instance;
        private readonly SerializedObject _serialized;
        private readonly List<MeshCollider> _colliders = new();
        private readonly Dictionary<MeshCollider, ColliderOptimizer.Analysis> _analyses = new();

        private readonly DropdownField _scope;
        private readonly Label _header;
        private readonly Label _emptyState;
        private readonly ListView _list;
        private readonly PresetRow _presetRow;
        private readonly Button _analyzeButton;
        private readonly Button _optimizeButton;
        private readonly Button _restoreButton;
        private readonly Label _summary;
        private readonly Toggle _heatmap;
        private readonly FloatField _threshold;
        private readonly Toggle _errorLines;

        public OptimizeTab()
        {
            AddToClassList("ac-tab");
            _serialized = new SerializedObject(_state);
            var settings = _serialized.FindProperty(GenerationSettingsState.SettingsPropertyPath);

            // Collider list
            var listSection = Section(this, null);
            var header = Row(listSection);
            _header = new Label();
            _header.AddToClassList("ac-section__header");
            _header.AddToClassList("ac-row__grow");
            header.Add(_header);
            _scope = new DropdownField(new List<string> { Strings.ScopeSelection, Strings.ScopeScene }, 0) { tooltip = Strings.ScopeTooltip };
            _scope.AddToClassList("ac-scope");
            _scope.RegisterValueChangedCallback(_ => RefreshColliders());
            header.Add(_scope);

            _emptyState = new Label(Strings.OptimizeEmpty);
            _emptyState.AddToClassList("ac-empty");
            listSection.Add(_emptyState);

            _list = new ListView(_colliders, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _list.AddToClassList("ac-target-list");
            listSection.Add(_list);

            // Settings (shared with the Generate tab)
            _presetRow = new PresetRow(OnSettingsChanged);
            Add(_presetRow);
            var optimization = Section(this, Strings.SectionMeshOptimization);
            optimization.Add(Field(settings, nameof(GenerationSettings.Quality)));
            optimization.Add(Field(settings, nameof(GenerationSettings.ShapeFidelity)));
            optimization.Add(Field(settings, nameof(GenerationSettings.MaxTriangles)));
            optimization.Add(Field(settings, nameof(GenerationSettings.Inflate)));
            optimization.Add(Field(settings, nameof(GenerationSettings.MinIslandSize)));

            // Actions
            var actions = Section(this, null);
            actions.AddToClassList("ac-actions");
            var buttons = Row(actions);
            _analyzeButton = new Button(Analyze) { text = Strings.Analyze, tooltip = Strings.AnalyzeTooltip };
            _analyzeButton.AddToClassList("ac-button");
            buttons.Add(_analyzeButton);
            _optimizeButton = new Button(Optimize) { tooltip = Strings.OptimizeTooltip };
            _optimizeButton.AddToClassList("ac-button");
            _optimizeButton.AddToClassList("ac-button--primary");
            _optimizeButton.AddToClassList("ac-row__grow");
            buttons.Add(_optimizeButton);
            _restoreButton = new Button(Restore) { tooltip = Strings.RestoreTooltip };
            _restoreButton.AddToClassList("ac-button");
            buttons.Add(_restoreButton);

            _summary = new Label();
            _summary.AddToClassList("ac-result__summary");
            _summary.AddToClassList("ac-optimize-summary");
            actions.Add(_summary);

            var heatmapRow = Row(actions);
            heatmapRow.AddToClassList("ac-heatmap-row");
            _heatmap = new Toggle(Strings.Heatmap) { tooltip = Strings.HeatmapTooltip };
            _heatmap.RegisterValueChangedCallback(_ => UpdateHeatmap());
            heatmapRow.Add(_heatmap);
            _threshold = new FloatField(Strings.HeatmapThreshold) { value = 0.02f, tooltip = Strings.HeatmapThresholdTooltip };
            _threshold.AddToClassList("ac-heatmap-threshold");
            _threshold.RegisterValueChangedCallback(_ => UpdateHeatmap());
            heatmapRow.Add(_threshold);
            _errorLines = new Toggle(Strings.HeatmapLines) { value = true, tooltip = Strings.HeatmapLinesTooltip };
            _errorLines.RegisterValueChangedCallback(_ => UpdateHeatmap());
            heatmapRow.Add(_errorLines);

            this.Bind(_serialized);
            this.TrackSerializedObjectValue(_serialized, _ =>
            {
                _state.SaveState();
                OnSettingsChanged();
            });

            RegisterCallback<AttachToPanelEvent>(_ => Subscribe(true));
            RegisterCallback<DetachFromPanelEvent>(_ => Subscribe(false));
            RefreshColliders();
        }

        private GenerationSettings Settings => _state.Settings;

        private bool WholeScene => _scope.index == 1;

        private void Subscribe(bool on)
        {
            Selection.selectionChanged -= RefreshColliders;
            EditorApplication.hierarchyChanged -= RefreshColliders;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (!on)
            {
                HeatmapDrawer.Clear();
                return;
            }
            Selection.selectionChanged += RefreshColliders;
            EditorApplication.hierarchyChanged += RefreshColliders;
            Undo.undoRedoPerformed += OnUndoRedo;
            RefreshColliders();
        }

        // Analyses whose collider mesh changed are dropped in RefreshColliders; unrelated undos keep them.
        private void OnUndoRedo() => RefreshColliders();

        private void OnSettingsChanged()
        {
            _serialized.Update();
            _analyses.Clear();
            _presetRow.Refresh();
            RefreshColliders();
        }

        private void RefreshColliders()
        {
            _colliders.Clear();
            _colliders.AddRange(ColliderOptimizer.Collect(WholeScene ? null : Selection.gameObjects));

            // Forget analyses for colliders that are gone or whose mesh changed.
            var stale = new List<MeshCollider>();
            foreach (var pair in _analyses)
                if (pair.Key == null || !_colliders.Contains(pair.Key) || pair.Key.sharedMesh != pair.Value.AnalyzedMesh)
                    stale.Add(pair.Key);
            foreach (var key in stale) _analyses.Remove(key);

            var any = _colliders.Count > 0;
            _header.text = string.Format(Strings.OptimizeHeader, _colliders.Count);
            _emptyState.text = WholeScene ? Strings.OptimizeEmptyScene : Strings.OptimizeEmpty;
            _emptyState.EnableInClassList("ac-hidden", any);
            _list.EnableInClassList("ac-hidden", !any);
            _list.style.height = Mathf.Clamp(_colliders.Count, 1, 10) * RowHeight + 4;
            _list.RefreshItems();

            _analyzeButton.SetEnabled(any);
            var applicable = CountApplicable();
            _optimizeButton.text = string.Format(Strings.OptimizeButton, _analyses.Count > 0 ? applicable : _colliders.Count);
            _optimizeButton.SetEnabled(any);
            var optimized = ColliderOptimizer.CountOptimized(_colliders);
            _restoreButton.text = string.Format(Strings.RestoreButton, optimized);
            _restoreButton.SetEnabled(optimized > 0);
            UpdateSummary();
            UpdateHeatmap();
        }

        private void Analyze()
        {
            _analyses.Clear();
            try
            {
                for (var i = 0; i < _colliders.Count; i++)
                {
                    var collider = _colliders[i];
                    if (EditorUtility.DisplayCancelableProgressBar(Strings.ProgressAnalyzeTitle, collider.name, (float)i / _colliders.Count))
                        break;
                    _analyses[collider] = ColliderOptimizer.Analyze(collider, Settings);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            RefreshColliders();
        }

        private void Optimize()
        {
            if (_analyses.Count == 0) Analyze();
            var list = new List<ColliderOptimizer.Analysis>(_analyses.Values);
            var applied = ColliderOptimizer.Apply(list);
            _analyses.Clear();
            RefreshColliders();
            _summary.text = string.Format(Strings.OptimizeDone, applied);
        }

        private void Restore()
        {
            var restored = ColliderOptimizer.RestoreOriginals(_colliders);
            _analyses.Clear();
            RefreshColliders();
            _summary.text = string.Format(Strings.RestoreDone, restored);
        }

        private int CountApplicable()
        {
            var count = 0;
            foreach (var analysis in _analyses.Values)
                if (analysis.CanApply) count++;
            return count;
        }

        private void UpdateSummary()
        {
            if (_analyses.Count == 0)
            {
                _summary.text = _colliders.Count > 0 ? Strings.OptimizeHintAnalyze : string.Empty;
                return;
            }

            var total = new ColliderStats();
            foreach (var analysis in _analyses.Values)
                if (analysis.Result != null) total.Add(analysis.Result.Stats);

            _summary.text = GenerateCommands.DescribeStats(total) + "\n"
                            + string.Format(Strings.MemoryLine, Format.Bytes(total.SourceBytes), Format.Bytes(total.ResultBytes));
        }

        private void UpdateHeatmap()
        {
            HeatmapDrawer.Clear();
            if (!_heatmap.value) return;
            HeatmapDrawer.Configure(_threshold.value, _errorLines.value);
            foreach (var analysis in _analyses.Values)
                if (analysis.Collider != null && analysis.Result?.Error != null)
                    HeatmapDrawer.Add(analysis.Collider.transform, analysis.Result.Error);
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-target-row");
            var icon = new Image { name = "icon", image = AssetPreview.GetMiniTypeThumbnail(typeof(MeshCollider)) };
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
                if (e.clickCount == 2 && row.userData is MeshCollider c && c != null) EditorGUIUtility.PingObject(c);
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _colliders.Count) return;
            var collider = _colliders[index];
            row.userData = collider;
            row.tooltip = Strings.TargetRowTooltip;
            row.Q<Label>("name").text = collider.name;

            var info = row.Q<Label>("info");
            var status = row.Q<Label>("status");
            foreach (var cls in new[] { "ac-status--ok", "ac-status--warning", "ac-status--muted", "ac-hidden" }) status.RemoveFromClassList(cls);
            var kind = collider.convex ? Strings.KindConvex : Strings.KindMesh;

            if (_analyses.TryGetValue(collider, out var analysis))
            {
                if (analysis.Problem != null)
                {
                    info.text = kind;
                    status.text = Strings.StatusSkipped;
                    status.tooltip = analysis.Problem;
                    status.AddToClassList("ac-status--warning");
                    return;
                }

                var stats = analysis.Result.Stats;
                info.text = string.Format(Strings.RowBeforeAfter, kind, stats.SourceTriangles, stats.ResultTriangles, Format.Distance(stats.MaxError));
                status.text = analysis.CanApply ? string.Format(Strings.RowSavings, stats.Savings * 100f) : Strings.RowAlreadyOptimal;
                status.tooltip = string.Format(Strings.RowStatsTooltip, Format.Distance(stats.MaxError), Format.Distance(stats.MeanError),
                    Format.Bytes(stats.SourceBytes), Format.Bytes(stats.ResultBytes), stats.Milliseconds);
                status.AddToClassList(analysis.CanApply ? "ac-status--ok" : "ac-status--muted");
                return;
            }

            var triangles = TriangleCount(collider.sharedMesh);
            info.text = string.Format(Strings.RowTriangles, kind, triangles);
            var isOptimized = GeneratedMeshStore.TryGetSource(collider.sharedMesh, out _);
            status.text = isOptimized ? Strings.RowOptimized : string.Empty;
            status.tooltip = isOptimized ? Strings.RowOptimizedTooltip : string.Empty;
            status.EnableInClassList("ac-hidden", !isOptimized);
            if (isOptimized) status.AddToClassList("ac-status--ok");
        }

        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++) count += mesh.GetIndexCount(i);
            return (int)(count / 3);
        }
    }
}
