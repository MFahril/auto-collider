namespace AutoCollider
{
    /// <summary>Collider shape for a painted region or a bone.</summary>
    public enum PartShape
    {
        /// <summary>Let the Auto strategy decide (primitive → hull → parts / optimized mesh).</summary>
        Auto = 0,
        /// <summary>Box, oriented to the selection when rotation is allowed.</summary>
        Box = 1,
        Sphere = 2,
        Capsule = 3,
        /// <summary>One convex hull (≤ 255 triangles).</summary>
        ConvexHull = 4,
        /// <summary>Several convex parts (convex decomposition of the selection).</summary>
        ConvexParts = 5,
    }
}
