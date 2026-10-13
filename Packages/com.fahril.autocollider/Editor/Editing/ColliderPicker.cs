using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Picking for the collider tool: exact per-collider tests (3D raycasts; 2D: the ray meets the collider's plane, then
    /// <see cref="Collider2D.OverlapPoint"/> — edge colliders within a few pixels), cycling through overlapping colliders on
    /// repeated clicks, and rectangle selection. Ignores layers.
    /// </summary>
    internal static class ColliderPicker
    {
        private const float CyclePixels = 4f;
        private const float EdgePickPixels = 6f;

        private static Vector2 _lastClick = new(float.NaN, float.NaN);
        private static int _cycleIndex;

        /// <summary>Colliders (3D and 2D) hit by the ray, nearest first.</summary>
        public static List<Component> PickAll(Ray ray, IEnumerable<Component> candidates, float maxDistance = 100000f)
        {
            Physics.SyncTransforms();
            Physics2D.SyncTransforms();
            var hits = new List<(float distance, Component collider)>();
            foreach (var c in candidates)
            {
                switch (c)
                {
                    case Collider c3 when c3 != null && c3.Raycast(ray, out var hit, maxDistance):
                        hits.Add((hit.distance, c3));
                        break;
                    case Collider2D c2 when c2 != null && TryPick2D(c2, ray, out var distance):
                        hits.Add((distance, c2));
                        break;
                }
            }
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));

            var result = new List<Component>(hits.Count);
            foreach (var h in hits) result.Add(h.collider);
            return result;
        }

        private static bool TryPick2D(Collider2D collider, Ray ray, out float distance)
        {
            var t = collider.transform;
            var plane = new Plane(t.forward, t.position);
            if (!plane.Raycast(ray, out distance))
            {
                // Seen edge-on (or from behind): try the plane from the other side.
                plane.Flip();
                if (!plane.Raycast(ray, out distance)) return false;
            }
            var point = ray.GetPoint(distance);
            if (!(collider is EdgeCollider2D edge)) return collider.OverlapPoint(point);

            // Edges enclose nothing: hit when the point is within a few pixels of a segment.
            // Without a scene camera (e.g. tests) handle sizes are meaningless: use a fraction of the edge's own size.
            var tolerance = Camera.current != null
                ? HandleUtility.GetHandleSize(point) * EdgePickPixels / 80f
                : edge.bounds.size.magnitude * 0.02f;
            var points = edge.points;
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var a = t.TransformPoint(points[i] + edge.offset);
                var b = t.TransformPoint(points[i + 1] + edge.offset);
                if (HandleUtility.DistancePointLine(point, a, b) <= tolerance) return true;
            }
            return false;
        }

        /// <summary>
        /// Nearest hit; clicking again at (almost) the same spot cycles to the next collider behind it.
        /// </summary>
        public static Component PickCycling(Ray ray, IEnumerable<Component> candidates, Vector2 guiPosition)
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
        public static List<Component> InRect(Rect guiRect, IEnumerable<Component> candidates, Func<Vector3, Vector2> worldToGui)
        {
            var result = new List<Component>();
            foreach (var c in candidates)
                if (c != null && guiRect.Contains(worldToGui(BoundsOf(c).center))) result.Add(c);
            return result;
        }

        /// <summary>World bounds of a 3D or 2D collider.</summary>
        public static Bounds BoundsOf(Component collider) => collider switch
        {
            Collider c3 => c3.bounds,
            Collider2D c2 => c2.bounds,
            _ => default,
        };

        /// <summary>Rect between two GUI points (any drag direction).</summary>
        public static Rect RectFrom(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
}
