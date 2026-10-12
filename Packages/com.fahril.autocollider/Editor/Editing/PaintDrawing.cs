using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutoCollider.Editor
{
    /// <summary>Scene-view drawing for paint mode: current paint, existing regions (one tint each) and the brush.</summary>
    internal static class PaintDrawing
    {
        private const float PaintAlpha = 0.45f;
        private const float RegionAlpha = 0.22f;
        private static Material _material;

        private static readonly Color[] RegionPalette =
        {
            new(0.27f, 0.85f, 0.45f), new(0.30f, 0.70f, 1.00f), new(0.95f, 0.75f, 0.25f),
            new(0.80f, 0.45f, 0.95f), new(0.95f, 0.45f, 0.45f), new(0.40f, 0.90f, 0.85f),
        };

        public static Color RegionColor(int index) => RegionPalette[Mathf.Abs(index) % RegionPalette.Length];

        public static void DrawPainted()
        {
            var color = AutoColliderSettings.instance.Data.SelectedColor;
            color.a = PaintAlpha;
            foreach (var pair in ColliderEditState.Painted)
            {
                if (pair.Key == null) continue;
                var group = pair.Key.GetComponentInParent<ColliderGroup>(true);
                var mesh = PaintMeshCache.Get(pair.Key, group);
                if (mesh != null) DrawTriangles(mesh, pair.Value, color);
            }
        }

        public static void DrawRegions(IEnumerable<ColliderGroup> groups)
        {
            foreach (var group in groups)
            {
                var regions = group.Recipe.Regions;
                for (var i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    var mesh = PaintMeshCache.Get(region.Renderer, group);
                    if (mesh == null) continue;
                    var color = RegionColor(i);
                    color.a = RegionAlpha;
                    DrawTriangles(mesh, region.Triangles, color);
                }
            }
        }

        public static void DrawBrush(Vector3 point, Vector3 viewDirection, float radius, bool erase)
        {
            var settings = AutoColliderSettings.instance.Data;
            Handles.zTest = CompareFunction.Always;
            Handles.color = erase ? new Color(1f, 0.35f, 0.3f) : settings.SelectedColor;
            Handles.DrawWireDisc(point, viewDirection, radius, 2f);
        }

        private static void DrawTriangles(PaintMesh mesh, IEnumerable<int> triangles, Color color)
        {
            var material = Material;
            if (material == null) return;
            // Lift slightly off the surface so the overlay doesn't z-fight with the mesh.
            var lift = mesh.Bounds.size.magnitude * 0.0015f;

            material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            foreach (var t in triangles)
            {
                if (t < 0 || t >= mesh.TriangleCount) continue;
                var offset = mesh.Normals[t] * lift;
                GL.Vertex(mesh.Vertices[mesh.Triangles[t * 3]] + offset);
                GL.Vertex(mesh.Vertices[mesh.Triangles[t * 3 + 1]] + offset);
                GL.Vertex(mesh.Vertices[mesh.Triangles[t * 3 + 2]] + offset);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static Material Material
        {
            get
            {
                if (_material != null) return _material;
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return null;
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull", (int)CullMode.Off);
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                return _material;
            }
        }
    }
}
