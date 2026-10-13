using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>A rectangle: center, size along its own axes, rotation in degrees (counter-clockwise).</summary>
    internal struct Box2DFit
    {
        public float2 Center;
        public float2 Size;
        public float Angle;
        public float Area;
    }

    /// <summary>Enclosing rectangles for 2D points.</summary>
    internal static class Box2DFitter
    {
        public static Box2DFit Aabb(IReadOnlyList<float2> points)
        {
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var p in points)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }
            var size = math.max(max - min, PhysicsLimits.MinExtent);
            return new Box2DFit { Center = (min + max) * 0.5f, Size = size, Angle = 0f, Area = size.x * size.y };
        }

        /// <summary>Minimum-area rectangle (rotating calipers over the convex hull); angle canonicalized to (−45°, 45°].</summary>
        public static Box2DFit MinArea(List<double2> hull, IReadOnlyList<float2> points)
        {
            if (hull.Count < 3) return Aabb(points);
            var dir = GeometryUtils.MinAreaRectDirection(hull);
            var angle = math.degrees((float)math.atan2(dir.y, dir.x));
            // Any multiple of 90° describes the same rectangle; pick the one closest to axis-aligned.
            angle -= 90f * math.round(angle / 90f);
            return InFrame(points, angle);
        }

        /// <summary>Tightest rectangle rotated by <paramref name="angleDegrees"/>.</summary>
        public static Box2DFit InFrame(IReadOnlyList<float2> points, float angleDegrees)
        {
            var r = math.radians(angleDegrees);
            var u = new float2(math.cos(r), math.sin(r));
            var v = new float2(-u.y, u.x);
            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);
            foreach (var p in points)
            {
                var local = new float2(math.dot(p, u), math.dot(p, v));
                min = math.min(min, local);
                max = math.max(max, local);
            }
            var size = math.max(max - min, PhysicsLimits.MinExtent);
            var mid = (min + max) * 0.5f;
            return new Box2DFit { Center = u * mid.x + v * mid.y, Size = size, Angle = angleDegrees, Area = size.x * size.y };
        }
    }
}
