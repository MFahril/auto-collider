using System;

namespace AutoCollider.Editor
{
    /// <summary>Maps <see cref="ColliderStrategy"/> values to implementations.</summary>
    internal static class StrategyRegistry
    {
        public static IColliderStrategy Get(ColliderStrategy strategy) => strategy switch
        {
            ColliderStrategy.BestPrimitive => new BestPrimitiveStrategy(),
            ColliderStrategy.Box => new BoxStrategy(),
            ColliderStrategy.Sphere => new SphereStrategy(),
            ColliderStrategy.Capsule => new CapsuleStrategy(),
            ColliderStrategy.ConvexHull => new ConvexHullStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };
    }
}
