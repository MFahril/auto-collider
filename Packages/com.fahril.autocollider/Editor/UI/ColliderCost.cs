using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Rough relative physics cost of a set of colliders, for the inspector meter (not a profiler).</summary>
    internal static class ColliderCost
    {
        public const float LowThreshold = 10f;
        public const float HighThreshold = 40f;

        public static float Estimate(IEnumerable<ColliderEntry> entries)
        {
            var total = 0f;
            foreach (var entry in entries)
                if (entry.Collider != null && entry.Collider.enabled)
                    total += Estimate(entry.Collider);
            return total;
        }

        public static float Estimate(Collider collider) => collider switch
        {
            SphereCollider => 1f,
            CapsuleCollider => 1.2f,
            BoxCollider => 1.5f,
            MeshCollider { convex: true } mesh => 3f + Triangles(mesh) / 64f,
            MeshCollider mesh => 6f + Triangles(mesh) / 32f,
            _ => 2f,
        };

        public static string Rating(float cost) =>
            cost < LowThreshold ? Strings.CostLow : cost < HighThreshold ? Strings.CostMedium : Strings.CostHigh;

        /// <summary>Five-dot meter, e.g. "●●○○○". Low = 1–2 dots, Medium = 3, High = 4–5 (matches <see cref="Rating"/>).</summary>
        public static string Meter(float cost)
        {
            var filled = cost <= 0f ? 0
                : cost < LowThreshold * 0.5f ? 1
                : cost < LowThreshold ? 2
                : cost < HighThreshold ? 3
                : cost < HighThreshold * 2f ? 4
                : 5;
            return new string('●', filled) + new string('○', 5 - filled);
        }

        public static string KindName(Collider collider) => collider switch
        {
            BoxCollider => Strings.KindBox,
            SphereCollider => Strings.KindSphere,
            CapsuleCollider => Strings.KindCapsule,
            MeshCollider { convex: true } => Strings.KindConvex,
            MeshCollider => Strings.KindMesh,
            _ => collider.GetType().Name,
        };

        private static int Triangles(MeshCollider mesh)
        {
            var shared = mesh.sharedMesh;
            if (shared == null) return 0;
            long count = 0;
            for (var i = 0; i < shared.subMeshCount; i++) count += shared.GetIndexCount(i);
            return (int)(count / 3);
        }
    }
}
