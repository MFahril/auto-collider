using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Draws computed-but-not-applied shapes ("ghost preview") in every Scene view.</summary>
    internal static class PreviewDrawer
    {
        private static readonly List<TargetResult> Results = new();

        public static bool IsActive => Results.Count > 0;

        public static void Show(IEnumerable<TargetResult> results)
        {
            Clear();
            Results.AddRange(results);
            if (Results.Count == 0) return;
            SceneView.duringSceneGui += OnSceneGui;
            SceneView.RepaintAll();
        }

        public static void Clear()
        {
            if (Results.Count == 0) return;
            Results.Clear();
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.RepaintAll();
        }

        private static void OnSceneGui(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;

            var settings = AutoColliderSettings.instance.Data;
            foreach (var result in Results)
            {
                if (result.Target == null) continue;
                // Follow the live transform so the preview stays attached while objects move.
                var t = result.Target.transform;
                var scaledToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                foreach (var shape in result.Shapes)
                    ShapeDrawing.Draw(shape, shape.Parent != null ? shape.Parent.localToWorldMatrix : scaledToWorld,
                        ShapeDrawing.ColorFor(shape.Kind, settings));
                foreach (var shape in result.Shapes2D)
                    ShapeDrawing2D.Draw(shape, shape.Parent != null ? shape.Parent.localToWorldMatrix : scaledToWorld,
                        ShapeDrawing2D.ColorFor(shape.Kind, settings));
            }
        }
    }
}
