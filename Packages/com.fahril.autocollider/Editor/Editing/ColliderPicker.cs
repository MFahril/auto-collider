using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Picking for the collider tool: exact per-collider raycasts (works for every collider type, ignores layers),
    /// cycling through overlapping colliders on repeated clicks, and rectangle selection.
    /// </summary>
    internal static class ColliderPicker
    {
        private const float CyclePixels = 4f;

        private static Vector2 _lastClick = new(float.NaN, float.NaN);
        private static int _cycleIndex;

        /// <summary>Colliders hit by the ray, nearest first.</summary>
        public static List<Collider> PickAll(Ray ray, IEnumerable<Collider> candidates, float maxDistance = 100000f)
        {
            Physics.SyncTransforms();
            var hits = new List<(float distance, Collider collider)>();
            foreach (var c in candidates)
                if (c != null && c.Raycast(ray, out var hit, maxDistance))
                    hits.Add((hit.distance, c));
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));

            var result = new List<Collider>(hits.Count);
            foreach (var h in hits) result.Add(h.collider);
            return result;
        }

        /// <summary>
        /// Nearest hit; clicking again at (almost) the same spot cycles to the next collider behind it.
        /// </summary>
        public static Collider PickCycling(Ray ray, IEnumerable<Collider> candidates, Vector2 guiPosition)
        {
            var hits = PickAll(ray, candidates);
            if (hits.Count == 0)
            {
                _lastClick = new Vector2(float.NaN, float.NaN);
                return null;
            }

            var sameSpot = Vector2.Distance(guiPosition, _lastClick) <= CyclePixels;
            _cycleIndex = sameSpot ? (_cycleIndex + 1) % hits.Count : 0;
            _lastClick = guiPosition;
            return hits[_cycleIndex];
        }

        /// <summary>Colliders whose bounds center projects inside <paramref name="guiRect"/>.</summary>
        public static List<Collider> InRect(Rect guiRect, IEnumerable<Collider> candidates, Func<Vector3, Vector2> worldToGui)
        {
            var result = new List<Collider>();
            foreach (var c in candidates)
                if (c != null && guiRect.Contains(worldToGui(c.bounds.center))) result.Add(c);
            return result;
        }

        /// <summary>Rect between two GUI points (any drag direction).</summary>
        public static Rect RectFrom(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
}
