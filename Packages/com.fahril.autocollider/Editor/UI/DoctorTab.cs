using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AutoCollider.Editor.UiFactory;

namespace AutoCollider.Editor
{
    /// <summary>Doctor tab: scans the selection or the open scenes for collider problems and fixes them in one click.</summary>
    internal sealed class DoctorTab : VisualElement
    {
        private const float RowHeight = 24f;

        private readonly List<DoctorIssue> _issues = new();
        private readonly DropdownField _scope;
        private readonly Toggle _checkMissing;
        private readonly Label _summary;
        private readonly ListView _list;
        private readonly Button _fixAll;
        private bool _scanned;
        private int _scannedObjects;

        public DoctorTab()
        {
            AddToClassList("ac-tab");

            var intro = Section(this, null);
            var text = new Label(Strings.DoctorIntro);
            text.AddToClassList("ac-subtitle");
            intro.Add(text);

            var controls = Row(intro);
            _scope = new DropdownField(new List<string> { Strings.ScopeSelection, Strings.ScopeScene }, 1) { tooltip = Strings.DoctorScopeTooltip };
            _scope.AddToClassList("ac-scope");
            _scope.AddToClassList("ac-row__grow");
            controls.Add(_scope);
            var scan = new Button(Scan) { text = Strings.DoctorScan, tooltip = Strings.DoctorScanTooltip };
            scan.AddToClassList("ac-button");
            scan.AddToClassList("ac-button--primary");
            controls.Add(scan);

            var settings = AutoColliderSettings.instance.Data;
            _checkMissing = new Toggle(Strings.DoctorCheckMissing) { tooltip = Strings.DoctorCheckMissingTooltip, value = settings.DoctorCheckMissingColliders };
            _checkMissing.RegisterValueChangedCallback(e =>
            {
                AutoColliderSettings.instance.Data.DoctorCheckMissingColliders = e.newValue;
                AutoColliderSettings.instance.SaveSettings();
                if (_scanned) Scan();
            });
            intro.Add(_checkMissing);

            var results = Section(this, null);
            _summary = new Label(Strings.DoctorNotScanned);
            _summary.AddToClassList("ac-result__summary");
            results.Add(_summary);

            _list = new ListView(_issues, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            };
            _list.AddToClassList("ac-target-list");
            _list.AddToClassList("ac-doctor-list");
            results.Add(_list);

            _fixAll = new Button(FixAll) { tooltip = Strings.DoctorFixAllTooltip };
            _fixAll.AddToClassList("ac-button");
            _fixAll.AddToClassList("ac-button--primary");
            results.Add(_fixAll);

            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Rescan);
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Rescan);
            Refresh();
        }

        /// <summary>Scan the open scenes (used by the Run Doctor menu).</summary>
        public void ScanScene()
        {
            _scope.index = 1;
            Scan();
        }

        private void Rescan()
        {
            if (_scanned) Scan();
        }

        private void Scan()
        {
            var roots = _scope.index == 0 ? Selection.gameObjects.Where(g => !EditorUtility.IsPersistent(g)).ToList() : null;
            var options = new ColliderDoctor.Options(_checkMissing.value, AutoColliderSettings.instance.Data.DoctorTriangleBudget);
            _issues.Clear();
            _issues.AddRange(ColliderDoctor.Scan(roots, options));
            _scannedObjects = roots?.Count ?? Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
            _scanned = true;
            Refresh();
        }

        private void Refresh()
        {
            _list.style.height = Mathf.Clamp(_issues.Count, 1, 14) * RowHeight + 4;
            _list.RefreshItems();
            _list.EnableInClassList("ac-hidden", _issues.Count == 0);

            var fixable = _issues.Count(i => i.CanFix);
            _fixAll.text = string.Format(Strings.DoctorFixAll, fixable);
            _fixAll.SetEnabled(fixable > 0);
            _fixAll.EnableInClassList("ac-hidden", !_scanned || _issues.Count == 0);

            if (!_scanned) _summary.text = Strings.DoctorNotScanned;
            else if (_issues.Count == 0) _summary.text = string.Format(Strings.DoctorHealthy, _scannedObjects);
            else
                _summary.text = string.Format(Strings.DoctorSummary,
                    _issues.Count(i => i.Severity == DoctorSeverity.Error),
                    _issues.Count(i => i.Severity == DoctorSeverity.Warning),
                    _issues.Count(i => i.Severity == DoctorSeverity.Info));
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ac-doctor-row");

            var severity = new VisualElement { name = "severity" };
            severity.AddToClassList("ac-severity");
            row.Add(severity);

            var target = new Button { name = "target", tooltip = Strings.DoctorSelectTooltip };
            target.AddToClassList("ac-doctor-row__target");
            target.clicked += () =>
            {
                if (!(row.userData is DoctorIssue issue) || issue.Target == null) return;
                Selection.activeObject = issue.Target is Component c ? c.gameObject : issue.Target;
                EditorGUIUtility.PingObject(issue.Target);
            };
            row.Add(target);

            var message = new Label { name = "message" };
            message.AddToClassList("ac-doctor-row__message");
            row.Add(message);

            var fix = new Button { name = "fix" };
            fix.AddToClassList("ac-button");
            fix.AddToClassList("ac-button--small");
            fix.clicked += () =>
            {
                if (!(row.userData is DoctorIssue issue)) return;
                ColliderDoctor.FixOne(issue);
                Scan();
            };
            row.Add(fix);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _issues.Count) return;
            var issue = _issues[index];
            row.userData = issue;

            var severity = row.Q("severity");
            severity.EnableInClassList("ac-severity--error", issue.Severity == DoctorSeverity.Error);
            severity.EnableInClassList("ac-severity--warning", issue.Severity == DoctorSeverity.Warning);
            severity.EnableInClassList("ac-severity--info", issue.Severity == DoctorSeverity.Info);
            severity.tooltip = Strings.SeverityName(issue.Severity);

            var target = row.Q<Button>("target");
            target.text = issue.Target == null ? Strings.DoctorMissingObject : TargetName(issue.Target);

            var message = row.Q<Label>("message");
            message.text = issue.Message;
            message.tooltip = issue.Message;

            var fix = row.Q<Button>("fix");
            fix.text = issue.FixLabel ?? string.Empty;
            fix.tooltip = issue.FixTooltip ?? string.Empty;
            fix.EnableInClassList("ac-hidden", !issue.CanFix);
        }

        private static string TargetName(Object target) => target switch
        {
            Component c => $"{c.gameObject.name} ({ObjectNames.NicifyVariableName(c.GetType().Name)})",
            _ => target.name,
        };

        private void FixAll()
        {
            var fixable = _issues.Count(i => i.CanFix);
            if (fixable == 0) return;
            ColliderDoctor.FixAll(_issues);
            Scan();
        }
    }
}
