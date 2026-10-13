namespace AutoCollider
{
    /// <summary>How tilemaps get colliders.</summary>
    public enum TilemapColliderMode
    {
        /// <summary>Unity's live tilemap colliders: TilemapCollider2D merged by a CompositeCollider2D (static Rigidbody2D). Follows tile edits.</summary>
        Composite = 0,

        /// <summary>The tiles' outline traced, simplified and baked into polygon colliders (fewer points; re-generate after editing tiles).</summary>
        Baked = 1,
    }
}
