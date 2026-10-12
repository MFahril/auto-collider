using System;
using Object = UnityEngine.Object;

namespace AutoCollider.Editor
{
    /// <summary>One problem found by <see cref="ColliderDoctor"/>, with an optional one-click fix.</summary>
    internal sealed class DoctorIssue
    {
        public DoctorIssue(DoctorCheck check, DoctorSeverity severity, Object target, string message)
        {
            Check = check;
            Severity = severity;
            Target = target;
            Message = message;
        }

        public DoctorCheck Check { get; }
        public DoctorSeverity Severity { get; }

        /// <summary>The component (or GameObject) the issue is about; selected when the row is clicked.</summary>
        public Object Target { get; }

        public string Message { get; }
        public string FixLabel { get; private set; }
        public string FixTooltip { get; private set; }

        /// <summary>Applies the fix (records its own Undo). Null when the issue has no automatic fix.</summary>
        public Action Fix { get; private set; }

        public bool CanFix => Fix != null;

        public DoctorIssue WithFix(string label, string tooltip, Action fix)
        {
            FixLabel = label;
            FixTooltip = tooltip;
            Fix = fix;
            return this;
        }
    }
}
