using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal enum ShapeKind
    {
        Box,
        Sphere,
        Capsule,
        ConvexMesh,
    }

    /// <summary>
    /// A fitted collider shape, expressed in the target's <b>scaled space</b> (target frame, world units — see <see cref="TargetSpace"/>).
    /// Pure data: nothing in the scene exists until <see cref="ColliderWriter"/> applies it.
    /// </summary>
    internal sealed class FittedShape
    {
        public ShapeKind Kind;
        public string Label;

        public float3 Center;
        public quaternion Rotation = quaternion.identity;

        /// <summary>Box full size.</summary>
        public float3 Size;

        /// <summary>Sphere / capsule radius.</summary>
        public float Radius;

        /// <summary>Capsule height including both caps.</summary>
        public float Height;

        /// <summary>Capsule axis in the shape frame: 0 = X, 1 = Y, 2 = Z.</summary>
        public int Direction = 1;

        /// <summary>Convex mesh data (≤ 255 triangles).</summary>
        public ConvexHull Hull;

        /// <summary>Enclosed volume, used to compare fits.</summary>
        public float Volume;

        public bool IsRotated => !GeometryUtils.IsNearIdentity(Rotation);

        public static FittedShape FromBox(BoxFit box) => new()
        {
            Kind = ShapeKind.Box,
            Center = box.Center,
            Rotation = box.Rotation,
            Size = box.Size,
            Volume = box.Volume,
        };

        public static FittedShape FromSphere(SphereFit sphere) => new()
        {
            Kind = ShapeKind.Sphere,
            Center = sphere.Center,
            Radius = sphere.Radius,
            Volume = sphere.Volume,
        };

        public static FittedShape FromCapsule(CapsuleFit capsule) => new()
        {
            Kind = ShapeKind.Capsule,
            Center = capsule.Center,
            Rotation = capsule.Rotation,
            Direction = capsule.Direction,
            Radius = capsule.Radius,
            Height = capsule.Height,
            Volume = capsule.Volume,
        };

        public static FittedShape FromHull(ConvexHull hull) => new()
        {
            Kind = ShapeKind.ConvexMesh,
            Hull = hull,
            Volume = hull.Volume,
        };
    }
}
