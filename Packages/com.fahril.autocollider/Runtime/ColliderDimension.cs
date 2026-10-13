namespace AutoCollider
{
    /// <summary>Whether a target gets 3D colliders, 2D colliders, or whatever fits its renderers.</summary>
    public enum ColliderDimension
    {
        /// <summary>Sprites (and tilemaps) get 2D colliders; meshes get 3D colliders unless a Rigidbody2D moves them.</summary>
        Auto = 0,

        /// <summary>3D colliders only; sprites are skipped.</summary>
        ThreeD = 1,

        /// <summary>2D colliders for everything, meshes included (their silhouette seen along the object's Z axis).</summary>
        TwoD = 2,
    }
}
