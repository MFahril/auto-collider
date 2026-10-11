using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Wireframe drawing of <see cref="FittedShape"/>s with <see cref="Handles"/> (Scene view only).</summary>
    internal static class ShapeDrawing
    {
        private const int ArcSegments = 16;
        private static readonly ConditionalWeakTable<object, Vector3[]> MeshEdges = new();

        /// <param name="scaledToWorld">Target scaled-space → world matrix (position + rotation, no scale).</param>
        public static void Draw(FittedShape shape, Matrix4x4 scaledToWorld, Color color)
        {
            var shapeMatrix = scaledToWorld * Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one);
            using (new Handles.DrawingScope(color, shapeMatrix))
            {
                switch (shape.Kind)
                {
                    case ShapeKind.Box:
                        Handles.DrawWireCube(Vector3.zero, shape.Size);
                        break;
                    case ShapeKind.Sphere:
                        Handles.DrawWireDisc(Vector3.zero, Vector3.right, shape.Radius);
                        Handles.DrawWireDisc(Vector3.zero, Vector3.up, shape.Radius);
                        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, shape.Radius);
                        break;
                    case ShapeKind.Capsule:
                        DrawCapsule(shape.Direction, shape.Radius, shape.Height);
                        break;
                    case ShapeKind.ConvexMesh:
                    case ShapeKind.ConcaveMesh:
                        Handles.DrawLines(Edges(shape));
                        break;
                }
            }
        }

        public static Color ColorFor(ShapeKind kind, AutoColliderSettingsData settings) => kind switch
        {
            ShapeKind.Box => settings.BoxColor,
            ShapeKind.Sphere => settings.SphereColor,
            ShapeKind.Capsule => settings.CapsuleColor,
            _ => settings.MeshColor,
        };

        private static void DrawCapsule(int direction, float radius, float height)
        {
            var axis = Vector3.zero;
            axis[direction] = 1f;
            var p1 = Vector3.zero;
            p1[(direction + 1) % 3] = 1f;
            var p2 = Vector3.Cross(axis, p1);

            var half = Mathf.Max(0f, height * 0.5f - radius);
            var top = axis * half;
            var bottom = -axis * half;

            Handles.DrawWireDisc(top, axis, radius);
            Handles.DrawWireDisc(bottom, axis, radius);
            foreach (var p in new[] { p1, -p1, p2, -p2 })
                Handles.DrawLine(top + p * radius, bottom + p * radius);

            foreach (var p in new[] { p1, p2 })
            {
                Handles.DrawPolyLine(Arc(top, p, axis, radius));
                Handles.DrawPolyLine(Arc(bottom, p, -axis, radius));
            }
        }

        /// <summary>Half circle from +side to −side, bulging toward <paramref name="bulge"/>.</summary>
        private static Vector3[] Arc(Vector3 center, Vector3 side, Vector3 bulge, float radius)
        {
            var points = new Vector3[ArcSegments + 1];
            for (var i = 0; i <= ArcSegments; i++)
            {
                var a = Mathf.PI * i / ArcSegments;
                points[i] = center + (side * Mathf.Cos(a) + bulge * Mathf.Sin(a)) * radius;
            }
            return points;
        }

        /// <summary>Unique edges of a mesh shape as line-segment pairs, cached per hull / mesh object.</summary>
        private static Vector3[] Edges(FittedShape shape)
        {
            object key = shape.Kind == ShapeKind.ConvexMesh ? shape.Hull : shape.Mesh;
            if (MeshEdges.TryGetValue(key, out var cached)) return cached;

            var vertices = shape.MeshVertices;
            var t = shape.MeshTriangles;
            var seen = new HashSet<(int, int)>();
            var segments = new List<Vector3>();
            for (var i = 0; i + 2 < t.Length; i += 3)
            for (var e = 0; e < 3; e++)
            {
                var a = t[i + e];
                var b = t[i + (e + 1) % 3];
                if (!seen.Add(a < b ? (a, b) : (b, a))) continue;
                segments.Add((float3)vertices[a]);
                segments.Add((float3)vertices[b]);
            }

            var array = segments.ToArray();
            MeshEdges.Add(key, array);
            return array;
        }
    }
}
