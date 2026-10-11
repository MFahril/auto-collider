namespace AutoCollider
{
    /// <summary>
    /// Physics engine limits that collider generation must respect.
    /// Centralized so a change in a future Unity/PhysX version is a one-line fix.
    /// </summary>
    public static class PhysicsLimits
    {
        /// <summary>Maximum polygon count PhysX accepts for a convex <see cref="UnityEngine.MeshCollider"/>.</summary>
        public const int MaxConvexPolygons = 255;

        /// <summary>Minimum number of non-coplanar vertices needed to build a convex hull.</summary>
        public const int MinHullVertices = 4;

        /// <summary>Smallest collider extent we generate; thinner shapes are padded to this to stay stable in PhysX.</summary>
        public const float MinExtent = 0.001f;
    }
}
