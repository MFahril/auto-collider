using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Wireframes for <see cref="FittedShape2D"/>s (preview), drawn in the target's XY plane.</summary>
    internal static class ShapeDrawing2D
    {
        private const int CircleSegments = 48;

        public static void Draw(FittedShape2D shape, Matrix4x4 scaledToWorld, Color color)
        {
            using (new Handles.DrawingScope(color, scaledToWorld))
            {
                switch (shape.Kind)
                {
                    case ShapeKind2D.Box:
                        Loop(Box(shape));
                        break;
                    case ShapeKind2D.Circle:
                        Loop(Circle(shape.Center, shape.Radius));
                        break;
                    case ShapeKind2D.Capsule:
                        Loop(Capsule(shape));
                        break;
                    default:
                        foreach (var path in shape.Paths) Loop(path);
                        break;
                }
            }
        }

        public static Color ColorFor(ShapeKind2D kind, AutoColliderSettingsData settings) => kind switch
        {
            ShapeKind2D.Box => settings.BoxColor,
            ShapeKind2D.Circle => settings.SphereColor,
            ShapeKind2D.Capsule => settings.CapsuleColor,
            _ => settings.MeshColor,
        };

        private static void Loop(float2[] points)
        {
            if (points == null || points.Length < 2) return;
            var line = new Vector3[points.Length + 1];
            for (var i = 0; i < points.Length; i++) line[i] = new Vector3(points[i].x, points[i].y, 0f);
            line[points.Length] = line[0];
            Handles.DrawAAPolyLine(2f, line);
        }

        private static float2[] Box(FittedShape2D shape)
        {
            var r = math.radians(shape.Angle);
            var u = new float2(math.cos(r), math.sin(r)) * shape.Size.x * 0.5f;
            var v = new float2(-math.sin(r), math.cos(r)) * shape.Size.y * 0.5f;
            var c = shape.Center;
            return new[] { c - u - v, c + u - v, c + u + v, c - u + v };
        }

        private static float2[] Circle(float2 center, float radius)
        {
            var points = new float2[CircleSegments];
            for (var i = 0; i < CircleSegments; i++)
            {
                var a = 2f * math.PI * i / CircleSegments;
                points[i] = center + new float2(math.cos(a), math.sin(a)) * radius;
            }
            return points;
        }

        /// <summary>Stadium outline: two half circles joined by straight sides.</summary>
        public static float2[] Capsule(FittedShape2D shape)
        {
            var axisAngle = math.radians(shape.Angle + (shape.Vertical ? 90f : 0f));
            var axis = new float2(math.cos(axisAngle), math.sin(axisAngle));
            var half = math.max(shape.Height * 0.5f - shape.Radius, 0f);
            var half2 = CircleSegments / 2;
            var points = new float2[half2 * 2 + 2];
            var start = math.atan2(axis.y, axis.x) - math.PI * 0.5f;
            for (var i = 0; i <= half2; i++)
            {
                var a = start + math.PI * i / half2;
                points[i] = shape.Center + axis * half + new float2(math.cos(a), math.sin(a)) * shape.Radius;
                points[half2 + 1 + i] = shape.Center - axis * half + new float2(math.cos(a + math.PI), math.sin(a + math.PI)) * shape.Radius;
            }
            return points;
        }
    }
}
