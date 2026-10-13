namespace AutoCollider
{
    /// <summary>How 2D collider shapes are produced from a sprite or flat mesh outline.</summary>
    public enum ColliderStrategy2D
    {
        /// <summary>A box / circle / capsule when one fits, else one convex polygon when the outline is convex enough, else the simplified outline polygon.</summary>
        Auto = 0,

        /// <summary>The tightest of box, circle and capsule (cheapest wins on near-ties).</summary>
        BestPrimitive = 1,

        /// <summary>BoxCollider2D, rotated when allowed and tighter.</summary>
        Box = 2,

        /// <summary>CircleCollider2D (smallest enclosing circle).</summary>
        Circle = 3,

        /// <summary>CapsuleCollider2D along the best axis.</summary>
        Capsule = 4,

        /// <summary>One convex PolygonCollider2D around the outline.</summary>
        ConvexPolygon = 5,

        /// <summary>PolygonCollider2D following the simplified outline, holes included.</summary>
        Polygon = 6,

        /// <summary>EdgeCollider2D loops along the simplified outline (hollow: only the border collides).</summary>
        Edge = 7,

        /// <summary>One collider per bone for rigged sprites (needs the 2D Animation package); others behave like Auto.</summary>
        PerBone = 8,
    }
}
