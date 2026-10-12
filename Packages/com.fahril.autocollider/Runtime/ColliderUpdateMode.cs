namespace AutoCollider
{
    /// <summary>How often an <see cref="AnimatedMeshCollider"/> re-bakes its mesh.</summary>
    public enum ColliderUpdateMode
    {
        /// <summary>Every rendered frame (most accurate, most expensive).</summary>
        EveryFrame = 0,

        /// <summary>Once every N frames.</summary>
        EveryNFrames = 1,

        /// <summary>A fixed number of times per second, independent of frame rate.</summary>
        TargetHz = 2,
    }
}
