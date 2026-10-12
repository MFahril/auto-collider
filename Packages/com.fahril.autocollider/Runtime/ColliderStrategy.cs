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
        /// <summary>Simplified (concave) MeshCollider — static or kinematic objects only.</summary>
        OptimizedMesh = 5,
        /// <summary>Several convex parts (hulls, or primitives where they fit) — concave shapes on moving Rigidbodies.</summary>
        ConvexDecomposition = 6,
        /// <summary>Picks the cheapest strategy that fits each mesh (primitive → hull → decomposition / optimized mesh); skinned meshes get per-bone colliders.</summary>
        Auto = 7,
        /// <summary>Skinned meshes: one collider per bone, parented to the bone so it follows the animation.</summary>
        PerBone = 8,
    }
}
