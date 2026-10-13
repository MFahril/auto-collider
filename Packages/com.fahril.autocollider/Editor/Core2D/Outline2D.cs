using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// A 2D shape to fit: oriented loops (outer CCW, holes CW, even-odd fill) in the target's world-sized 2D space.
    /// Derived data (area, points, convex hull) is computed once on demand.
    /// </summary>
    internal sealed class Outline2D
    {
        private float? _area;
        private float2[] _outerPoints;
        private List<double2> _hull;
        private float? _hullArea;

        public Outline2D(string label, IReadOnlyList<float2[]> loops)
        {
            Label = label;
            Loops = loops;
            Polygon2D.Bounds(loops, out var min, out var max);
            Min = min;
            Max = max;
        }

        public string Label { get; }
        public IReadOnlyList<float2[]> Loops { get; }
        public float2 Min { get; }
        public float2 Max { get; }
        public float Diagonal => math.distance(Min, Max);
        public bool IsEmpty => Loops.Count == 0 || Area <= 0f;

        /// <summary>Filled area (holes subtracted).</summary>
        public float Area => _area ??= Polygon2D.Area(Loops);

        /// <summary>Vertices of the outer (counter-clockwise) loops — everything an enclosing shape must cover.</summary>
        public float2[] OuterPoints
        {
            get
            {
                if (_outerPoints != null) return _outerPoints;
                var points = new List<float2>();
                foreach (var loop in Loops)
                    if (Polygon2D.SignedArea(loop) > 0f)
                        points.AddRange(loop);
                return _outerPoints = points.ToArray();
            }
        }

        /// <summary>Convex hull of <see cref="OuterPoints"/> (counter-clockwise).</summary>
        public List<double2> Hull
        {
            get
            {
                if (_hull != null) return _hull;
                var points = new List<double2>(OuterPoints.Length);
                foreach (var p in OuterPoints) points.Add(p);
                return _hull = GeometryUtils.ConvexHull2D(points);
            }
        }

        public float HullArea
        {
            get
            {
                if (_hullArea.HasValue) return _hullArea.Value;
                var hull = Hull;
                var loop = new float2[hull.Count];
                for (var i = 0; i < hull.Count; i++) loop[i] = (float2)hull[i];
                _hullArea = math.abs(Polygon2D.SignedArea(loop));
                return _hullArea.Value;
            }
        }

        /// <summary>Area ÷ hull area (1 = convex).</summary>
        public float Convexity => HullArea > 0f ? math.saturate(Area / HullArea) : 1f;

        public float2[] HullLoop()
        {
            var hull = Hull;
            var loop = new float2[hull.Count];
            for (var i = 0; i < hull.Count; i++) loop[i] = (float2)hull[i];
            if (Polygon2D.SignedArea(loop) < 0f) System.Array.Reverse(loop);
            return loop;
        }
    }
}
