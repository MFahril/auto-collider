using Unity.Mathematics;

namespace AutoCollider.Editor
{
    internal enum ShapeKind
    {
        Box,
        Sphere,
        Capsule,
        ConvexMesh,
        ConcaveMesh,
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

        /// <summary>Concave (optimized) mesh data.</summary>
        public MeshGeometry Mesh;

        /// <summary>Enclosed volume, used to compare fits (0 for concave meshes).</summary>
        public float Volume;

        /// <summary>Before/after statistics for mesh shapes (null for primitives).</summary>
        public ColliderStats? Stats;

        /// <summary>Sampled deviation from the source mesh, for the heatmap (mesh shapes only).</summary>
        public ErrorMetric.Result Error;

        /// <summary>Optional user-facing note from the strategy (e.g. "Auto chose Convex Hull").</summary>
        public string Note;

        /// <summary>
        /// When set (bone colliders), the shape is expressed in this transform's <b>local</b> space and the collider is created
        /// on its own child of it, so it follows the bone. Otherwise the shape is in the target's scaled space.
        /// </summary>
        public UnityEngine.Transform Parent;

        /// <summary>The painted region this shape was built from (its entry's PartId), or -1.</summary>
        public int RegionId = -1;

        /// <summary>LOD level the shape was fitted for (per-LOD generation), or -1 for every LOD.</summary>
        public int Lod = -1;

        public bool IsRotated => !GeometryUtils.IsNearIdentity(Rotation);

        public bool IsMesh => Kind == ShapeKind.ConvexMesh || Kind == ShapeKind.ConcaveMesh;

        /// <summary>Vertices / triangles of mesh shapes (hull or concave mesh); null for primitives.</summary>
        public float3[] MeshVertices => Kind == ShapeKind.ConvexMesh ? Hull.Vertices : Mesh?.Vertices;

        public int[] MeshTriangles => Kind == ShapeKind.ConvexMesh ? Hull.Triangles : Mesh?.Triangles;

        public static FittedShape FromMesh(MeshGeometry mesh) => new()
        {
            Kind = ShapeKind.ConcaveMesh,
            Mesh = mesh,
        };

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
