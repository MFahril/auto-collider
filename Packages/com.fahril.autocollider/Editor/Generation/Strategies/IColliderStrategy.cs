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
        public FitContext(bool allowRotation)
        {
            AllowRotation = allowRotation;
        }

        /// <summary>Rotated primitives allowed (setting enabled and the target has uniform scale).</summary>
        public bool AllowRotation { get; }
    }
}
