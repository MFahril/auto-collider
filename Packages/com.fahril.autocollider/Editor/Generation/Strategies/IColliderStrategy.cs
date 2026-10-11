using System.Collections.Generic;

namespace AutoCollider.Editor
{
    /// <summary>Turns one piece of geometry into one or more fitted shapes. Pure: never touches the scene.</summary>
    internal interface IColliderStrategy
    {
        List<FittedShape> Fit(FitInput input, FitContext context);
    }

    /// <summary>Per-target options that affect fitting.</summary>
    internal readonly struct FitContext
    {
        public FitContext(bool allowRotation, bool isDynamic = false)
        {
            AllowRotation = allowRotation;
            IsDynamic = isDynamic;
        }

        /// <summary>Rotated primitives allowed (setting enabled and the target has uniform scale).</summary>
        public bool AllowRotation { get; }

        /// <summary>The target is moved by a non-kinematic Rigidbody (concave mesh colliders are not allowed).</summary>
        public bool IsDynamic { get; }
    }
}
