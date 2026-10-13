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
            {
                if (!entry.IsAlive || !entry.Enabled) continue;
                total += entry.Collider != null ? Estimate(entry.Collider) : Estimate(entry.Collider2D);
            }
            return total;
        }

        /// <summary>2D: circle &lt; capsule &lt; box &lt; polygon (grows with its points) &lt; edge chains.</summary>
        public static float Estimate(Collider2D collider) => collider switch
        {
            CircleCollider2D => 0.8f,
            CapsuleCollider2D => 1f,
            BoxCollider2D => 1.2f,
            PolygonCollider2D polygon => 1.5f + polygon.GetTotalPointCount() / 16f,
            EdgeCollider2D edge => 2f + edge.pointCount / 8f,
            _ => 2f,
        };

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

        public static string KindName(Component collider) => collider switch
        {
            BoxCollider => Strings.KindBox,
            SphereCollider => Strings.KindSphere,
            CapsuleCollider => Strings.KindCapsule,
            MeshCollider { convex: true } => Strings.KindConvex,
            MeshCollider => Strings.KindMesh,
            BoxCollider2D => Strings.KindBox2D,
            CircleCollider2D => Strings.KindCircle2D,
            CapsuleCollider2D => Strings.KindCapsule2D,
            PolygonCollider2D => Strings.KindPolygon2D,
            EdgeCollider2D => Strings.KindEdge2D,
            null => string.Empty,
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
