using System;

namespace AutoCollider.Editor
{
    /// <summary>Maps <see cref="ColliderStrategy"/> values to implementations.</summary>
    internal static class StrategyRegistry
    {
        public static IColliderStrategy Get(ColliderStrategy strategy, GenerationSettings settings = null) => strategy switch
        {
            ColliderStrategy.BestPrimitive => new BestPrimitiveStrategy(),
            ColliderStrategy.Box => new BoxStrategy(),
            ColliderStrategy.Sphere => new SphereStrategy(),
            ColliderStrategy.Capsule => new CapsuleStrategy(),
            ColliderStrategy.ConvexHull => new ConvexHullStrategy(),
            ColliderStrategy.OptimizedMesh => new OptimizedMeshStrategy(settings ?? new GenerationSettings()),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };

        /// <summary>True when the strategy outputs mesh colliders that use the optimization settings.</summary>
        public static bool UsesMeshSettings(ColliderStrategy strategy) =>
            strategy == ColliderStrategy.ConvexHull || strategy == ColliderStrategy.OptimizedMesh;
    }
}
