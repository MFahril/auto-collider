using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal enum ShapeKind2D
    {
        Box,
        Circle,
        Capsule,
        Polygon,
        Edge,
    }

    /// <summary>
    /// A computed 2D collider (not yet in the scene), in the target's world-sized 2D space (its XY plane, scale removed —
    /// see <see cref="TargetSpace"/>). Rotated primitives (<see cref="Angle"/> ≠ 0) get their own child object.
    /// </summary>
    internal sealed class FittedShape2D
    {
        public ShapeKind2D Kind;
        public string Label;
        public float2 Center;

        /// <summary>Counter-clockwise rotation in degrees (boxes and capsules).</summary>
        public float Angle;

        /// <summary>Box size.</summary>
        public float2 Size;

        /// <summary>Circle / capsule radius.</summary>
        public float Radius;

        /// <summary>Capsule length including both caps.</summary>
        public float Height;

        /// <summary>Capsule axis: vertical (Y) or horizontal (X), before <see cref="Angle"/>.</summary>
        public bool Vertical = true;

        /// <summary>Polygon paths (outer CCW, holes CW) or, for <see cref="ShapeKind2D.Edge"/>, one closed loop.</summary>
        public float2[][] Paths;

        /// <summary>Area covered (fit comparisons).</summary>
        public float Area;

        public string Note;

        /// <summary>LOD level the shape was fitted for, or -1.</summary>
        public int Lod = -1;

        public bool IsRotated => (Kind == ShapeKind2D.Box || Kind == ShapeKind2D.Capsule) && math.abs(Angle) > 0.01f;

        public bool IsPolygonal => Kind == ShapeKind2D.Polygon || Kind == ShapeKind2D.Edge;

        public int PointCount
        {
            get
            {
                if (Paths == null) return 0;
                var count = 0;
                foreach (var path in Paths) count += path.Length;
                return count;
            }
        }

        /// <summary>Shallow copy (paths are replaced, never written into, by later steps).</summary>
        public FittedShape2D Clone() => (FittedShape2D)MemberwiseClone();

        public static FittedShape2D FromBox(Box2DFit box) => new()
        {
            Kind = ShapeKind2D.Box, Center = box.Center, Size = box.Size, Angle = box.Angle, Area = box.Area,
        };

        public static FittedShape2D FromCircle(CircleFit circle) => new()
        {
            Kind = ShapeKind2D.Circle, Center = circle.Center, Radius = circle.Radius, Area = circle.Area,
        };

        public static FittedShape2D FromCapsule(Capsule2DFit capsule) => new()
        {
            Kind = ShapeKind2D.Capsule, Center = capsule.Center, Angle = capsule.Angle, Radius = capsule.Radius,
            Height = capsule.Height, Vertical = capsule.Vertical, Area = capsule.Area,
        };

        public static FittedShape2D FromPaths(float2[][] paths, bool edge)
        {
            var area = 0f;
            foreach (var path in paths) area += Polygon2D.SignedArea(path);
            return new FittedShape2D { Kind = edge ? ShapeKind2D.Edge : ShapeKind2D.Polygon, Paths = paths, Area = math.abs(area) };
        }
    }
}
