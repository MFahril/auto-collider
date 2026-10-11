namespace AutoCollider
{
    /// <summary>Whether each renderer gets its own collider or the whole target is fitted as one.</summary>
    public enum MeshGranularity
    {
        /// <summary>One collider set per renderer (e.g. chair legs get separate boxes).</summary>
        PerRenderer = 0,
        /// <summary>All renderers of the target are combined and fitted together.</summary>
        Combined = 1,
    }
}
