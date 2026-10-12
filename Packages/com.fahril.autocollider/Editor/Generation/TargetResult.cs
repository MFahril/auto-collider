using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    internal enum TargetStatus
    {
        Ok,
        Warning,
        Skipped,
        Error,
    }

    /// <summary>Outcome of generating colliders for one target (computed shapes, and after apply, what was written).</summary>
    internal sealed class TargetResult
    {
        public TargetResult(GameObject target)
        {
            Target = target;
        }

        public GameObject Target { get; }
        public TargetSpace Space { get; set; }
        public List<FittedShape> Shapes { get; } = new();
        public List<string> Warnings { get; } = new();

        /// <summary>Informational notes, e.g. what the Auto strategy chose.</summary>
        public List<string> Notes { get; } = new();
        public TargetStatus Status { get; set; } = TargetStatus.Ok;
        public string Message { get; set; }
        public int RendererCount { get; set; }
        public int SourceTriangles { get; set; }
        public int CollidersCreated { get; set; }
        public int CollidersRemoved { get; set; }

        /// <summary>LOD Group the shapes were fitted per level for (per-LOD generation), else null.</summary>
        public LODGroup LodGroup { get; set; }

        /// <summary>Accumulated statistics of mesh shapes (a field so <see cref="ColliderStats.Add"/> mutates in place).</summary>
        public ColliderStats Stats;

        public bool HasMeshStats => Stats.SourceTriangles > 0;

        public void Skip(string reason)
        {
            Status = TargetStatus.Skipped;
            Message = reason;
        }

        public void Warn(string warning)
        {
            Warnings.Add(warning);
            if (Status == TargetStatus.Ok) Status = TargetStatus.Warning;
        }
    }
}
