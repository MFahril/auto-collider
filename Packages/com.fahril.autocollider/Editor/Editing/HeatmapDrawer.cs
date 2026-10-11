using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Scene-view error heatmap: a marker per sampled source vertex, green (0) → yellow → red (≥ threshold),
    /// plus optional lines to the closest collider point.
    /// </summary>
    internal static class HeatmapDrawer
    {
        private sealed class Entry
        {
            public Transform Space;
            public ErrorMetric.Result Error;
            public float MarkerSize;
        }

        private static readonly List<Entry> Entries = new();
        private static float _threshold = 0.01f;
        private static bool _drawLines = true;

        public static bool IsActive => Entries.Count > 0;

        /// <param name="space">Transform whose scaled space the samples are in (position + rotation, no scale).</param>
        public static void Add(Transform space, ErrorMetric.Result error)
        {
            if (space == null || error == null || error.Samples.Length == 0) return;
            GeometryUtils.MinMax(error.Samples, out var min, out var max);
            Entries.Add(new Entry { Space = space, Error = error, MarkerSize = Unity.Mathematics.math.length(max - min) * 0.004f });
            Subscribe();
        }

        public static void Configure(float threshold, bool drawLines)
        {
            _threshold = Mathf.Max(threshold, 1e-6f);
            _drawLines = drawLines;
            SceneView.RepaintAll();
        }

        public static void Clear()
        {
            if (Entries.Count == 0) return;
            Entries.Clear();
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.RepaintAll();
        }

        /// <summary>Gradient used by the heatmap and the legend: green → yellow → red.</summary>
        public static Color ColorFor(float error, float threshold)
        {
            var t = Mathf.Clamp01(error / Mathf.Max(threshold, 1e-6f));
            return t < 0.5f
                ? Color.Lerp(new Color(0.2f, 0.85f, 0.35f), new Color(1f, 0.85f, 0.2f), t * 2f)
                : Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.25f, 0.2f), (t - 0.5f) * 2f);
        }

        private static void Subscribe()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            SceneView.RepaintAll();
        }

        private static void OnSceneGui(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;

            // Samples are grouped into color buckets so each bucket is a single DrawLines call.
            var buckets = new List<Vector3>[ColorBuckets];
            for (var b = 0; b < ColorBuckets; b++) buckets[b] = new List<Vector3>();

            foreach (var entry in Entries)
            {
                if (entry.Space == null) continue;
                foreach (var list in buckets) list.Clear();

                var error = entry.Error;
                var s = entry.MarkerSize;
                for (var i = 0; i < error.Samples.Length; i++)
                {
                    var bucket = buckets[Bucket(error.Distances[i])];
                    Vector3 p = error.Samples[i];
                    bucket.Add(p - Vector3.right * s); bucket.Add(p + Vector3.right * s);
                    bucket.Add(p - Vector3.up * s); bucket.Add(p + Vector3.up * s);
                    bucket.Add(p - Vector3.forward * s); bucket.Add(p + Vector3.forward * s);

                    if (_drawLines && error.Distances[i] > _threshold * 0.25f)
                    {
                        bucket.Add(p);
                        bucket.Add(error.Closest[i]);
                    }
                }

                var matrix = Matrix4x4.TRS(entry.Space.position, entry.Space.rotation, Vector3.one);
                for (var b = 0; b < ColorBuckets; b++)
                {
                    if (buckets[b].Count == 0) continue;
                    var color = ColorFor((b + 0.5f) / ColorBuckets * _threshold, _threshold);
                    using (new Handles.DrawingScope(color, matrix))
                        Handles.DrawLines(buckets[b].ToArray());
                }
            }
        }

        private const int ColorBuckets = 8;

        private static int Bucket(float error) =>
            Mathf.Clamp(Mathf.FloorToInt(error / _threshold * ColorBuckets), 0, ColorBuckets - 1);
    }
}
