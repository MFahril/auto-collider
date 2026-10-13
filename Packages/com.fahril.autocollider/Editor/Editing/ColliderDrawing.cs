using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutoCollider.Editor
{
    /// <summary>Scene-view drawing of live colliders for the edit tool: type colors, fill, selection, X-ray, labels.</summary>
    internal static class ColliderDrawing
    {
        private const float HiddenAlpha = 0.25f;
        private const float FillInflate = 1.002f;
        private static readonly ConditionalWeakTable<Mesh, Vector3[]> MeshEdges = new();
        private static GUIStyle _labelStyle;

        public static void Draw(Collider collider, ColliderGroup group, in TargetSpace space, bool selected, bool xray, bool label)
        {
            var settings = AutoColliderSettings.instance.Data;
            var baseColor = selected ? settings.SelectedColor : ColorFor(collider, settings);
            if (!collider.enabled) baseColor.a *= 0.4f;

            if (collider is MeshCollider meshCollider)
            {
                DrawMesh(meshCollider, baseColor, xray);
            }
            else
            {
                var shape = ColliderShapes.ToShape(collider, group, space);
                if (shape == null) return;
                var groupMatrix = Matrix4x4.TRS(group.transform.position, group.transform.rotation, Vector3.one);

                var fill = baseColor;
                fill.a = settings.FillOpacity * (selected ? 2f : 1f);
                // The fill is pushed out by 0.2% so it doesn't z-fight with the mesh surface it usually sits on.
                using (new Handles.DrawingScope(groupMatrix * Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one * FillInflate)))
                {
                    Handles.zTest = CompareFunction.LessEqual;
                    DrawFill(shape, fill);
                }

                if (xray)
                {
                    Handles.zTest = CompareFunction.Greater;
                    ShapeDrawing.Draw(shape, groupMatrix, Faded(baseColor));
                }
                Handles.zTest = CompareFunction.LessEqual;
                ShapeDrawing.Draw(shape, groupMatrix, baseColor);
            }
            Handles.zTest = CompareFunction.Always;

            if (label && TryLabel(collider, out var text))
                Handles.Label(collider.bounds.center, text, LabelStyle);
        }

        /// <summary>3D or 2D collider.</summary>
        public static void Draw(Component collider, ColliderGroup group, in TargetSpace space, bool selected, bool xray, bool label)
        {
            if (collider is Collider c3) Draw(c3, group, space, selected, xray, label);
            else if (collider is Collider2D c2) Draw2D(c2, group, space, selected, label);
        }

        /// <summary>2D colliders: outline (and a translucent fill for convex shapes) in the collider's plane.</summary>
        public static void Draw2D(Collider2D collider, ColliderGroup group, in TargetSpace space, bool selected, bool label)
        {
            var settings = AutoColliderSettings.instance.Data;
            var shape = ColliderShapes2D.ToShape(collider, group, space);
            if (shape == null) return;
            var color = selected ? settings.SelectedColor : ShapeDrawing2D.ColorFor(shape.Kind, settings);
            if (!collider.enabled) color.a *= 0.4f;

            var plane = Matrix4x4.TRS(group.transform.position, group.transform.rotation, Vector3.one)
                        * Matrix4x4.Translate(new Vector3(0f, 0f, ColliderShapes2D.PlaneZ(collider, space)));
            Handles.zTest = CompareFunction.Always;
            if (shape.Kind != ShapeKind2D.Polygon && shape.Kind != ShapeKind2D.Edge)
            {
                var fill = color;
                fill.a = settings.FillOpacity * (selected ? 2f : 1f);
                var outline = ColliderShapes2D.SurfacePoints(shape);
                var points = new Vector3[outline.Length];
                for (var i = 0; i < points.Length; i++) points[i] = new Vector3(outline[i].x, outline[i].y, 0f);
                using (new Handles.DrawingScope(fill, plane)) Handles.DrawAAConvexPolygon(points);
            }
            ShapeDrawing2D.Draw(shape, plane, color);

            if (label && TryLabel(collider, out var text))
                Handles.Label(collider.bounds.center, text, LabelStyle);
        }

        public static Color ColorFor(Collider collider, AutoColliderSettingsData settings) => collider switch
        {
            BoxCollider => settings.BoxColor,
            SphereCollider => settings.SphereColor,
            CapsuleCollider => settings.CapsuleColor,
            _ => settings.MeshColor,
        };

        private static void DrawFill(FittedShape shape, Color color)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                {
                    var h = (Vector3)(shape.Size * 0.5f);
                    var faces = new[]
                    {
                        (Vector3.right, Vector3.up, Vector3.forward), (Vector3.up, Vector3.forward, Vector3.right), (Vector3.forward, Vector3.right, Vector3.up),
                    };
                    Handles.color = color;
                    foreach (var (n, u, v) in faces)
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var c = Vector3.Scale(n, h) * side;
                        var du = Vector3.Scale(u, h);
                        var dv = Vector3.Scale(v, h);
                        Handles.DrawAAConvexPolygon(c - du - dv, c + du - dv, c + du + dv, c - du + dv);
                    }
                    break;
                }
                case ShapeKind.Sphere:
                    Handles.color = color;
                    Handles.SphereHandleCap(0, Vector3.zero, Quaternion.identity, shape.Radius * 2f, EventType.Repaint);
                    break;
                case ShapeKind.Capsule:
                {
                    var axis = Vector3.zero;
                    axis[shape.Direction] = 1f;
                    var half = Mathf.Max(0f, shape.Height * 0.5f - shape.Radius);
                    Handles.color = color;
                    Handles.SphereHandleCap(0, axis * half, Quaternion.identity, shape.Radius * 2f, EventType.Repaint);
                    Handles.SphereHandleCap(0, -axis * half, Quaternion.identity, shape.Radius * 2f, EventType.Repaint);
                    break;
                }
            }
        }

        private static void DrawMesh(MeshCollider collider, Color color, bool xray)
        {
            var mesh = collider.sharedMesh;
            if (mesh == null) return;
            var edges = Edges(mesh);
            using (new Handles.DrawingScope(collider.transform.localToWorldMatrix))
            {
                if (xray)
                {
                    Handles.zTest = CompareFunction.Greater;
                    Handles.color = Faded(color);
                    Handles.DrawLines(edges);
                }
                Handles.zTest = CompareFunction.LessEqual;
                Handles.color = color;
                Handles.DrawLines(edges);
            }
        }

        private static Vector3[] Edges(Mesh mesh)
        {
            if (MeshEdges.TryGetValue(mesh, out var cached)) return cached;
            MeshSource.ReadMesh(mesh, out var vertices, out var triangles);
            var seen = new HashSet<(int, int)>();
            var segments = new List<Vector3>();
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            for (var e = 0; e < 3; e++)
            {
                var a = triangles[i + e];
                var b = triangles[i + (e + 1) % 3];
                if (!seen.Add(a < b ? (a, b) : (b, a))) continue;
                segments.Add(vertices[a]);
                segments.Add(vertices[b]);
            }
            var array = segments.ToArray();
            MeshEdges.Add(mesh, array);
            return array;
        }

        private static bool TryLabel(Component collider, out string text)
        {
            text = null;
            if (!ColliderOperations.TryFindAny(collider, out _, out var entry)) return false;
            text = (entry.Locked ? "🔒 " : string.Empty) + (string.IsNullOrEmpty(entry.Label) ? collider.name : entry.Label);
            return true;
        }

        private static Color Faded(Color c)
        {
            c.a *= HiddenAlpha;
            return c;
        }

        private static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { background = Texture2D.blackTexture },
            padding = new RectOffset(3, 3, 1, 1),
        };
    }
}
