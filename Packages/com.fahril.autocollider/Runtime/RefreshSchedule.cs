namespace AutoCollider
{
    /// <summary>Throttling rule for <see cref="AnimatedMeshCollider"/> (pure, so it is unit-testable).</summary>
    internal static class RefreshSchedule
    {
        /// <summary>True when a refresh is due. <paramref name="lastFrame"/> &lt; 0 means "never refreshed".</summary>
        public static bool IsDue(ColliderUpdateMode mode, int everyNFrames, float targetHz,
            int frame, float time, int lastFrame, float lastTime)
        {
            if (lastFrame < 0) return true;
            if (frame == lastFrame) return false;
            return mode switch
            {
                ColliderUpdateMode.EveryNFrames => frame - lastFrame >= (everyNFrames < 1 ? 1 : everyNFrames),
                // A small tolerance keeps e.g. 30 Hz at 60 fps from slipping a frame through float rounding.
                ColliderUpdateMode.TargetHz => time - lastTime >= 1f / (targetHz > 0.01f ? targetHz : 0.01f) - 1e-4f,
                _ => true,
            };
        }
    }
}
