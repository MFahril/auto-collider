namespace AutoCollider
{
    /// <summary>How collider shapes are produced from a mesh.</summary>
    public enum ColliderStrategy
    {
        /// <summary>Tightest of box, sphere and capsule (smallest enclosing volume).</summary>
        BestPrimitive = 0,
        /// <summary>Box; oriented to the mesh when rotation is allowed.</summary>
        Box = 1,
        /// <summary>Minimal enclosing sphere.</summary>
        Sphere = 2,
        /// <summary>Capsule along the best axis.</summary>
        Capsule = 3,
        /// <summary>Convex MeshCollider (≤ 255 triangles).</summary>
        ConvexHull = 4,
    }
}
