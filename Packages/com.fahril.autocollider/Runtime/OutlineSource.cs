namespace AutoCollider
{
    /// <summary>Where a sprite's outline comes from.</summary>
    public enum OutlineSource
    {
        /// <summary>Trace the opaque pixels (exact); falls back to the physics shape, then the mesh, for tightly packed atlas sprites.</summary>
        Auto = 0,

        /// <summary>The sprite's physics shape — use this for a Custom Physics Shape drawn in the Sprite Editor (Unity's generated one is a coarse enclosing polygon).</summary>
        PhysicsShape = 1,

        /// <summary>The sprite's render mesh outline (coarse enclosing polygon for Tight meshes, a rectangle for Full Rect).</summary>
        SpriteMesh = 2,

        /// <summary>Trace the opaque pixels (uses Alpha Threshold and Outline Resolution).</summary>
        Alpha = 3,
    }
}
