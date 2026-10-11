using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// The coordinate frame fitting happens in: the target's position and rotation, but <b>world-sized</b> (scale removed).
    /// Fitting in this space keeps spheres and capsules correct under non-uniform scale; <see cref="ColliderWriter"/>
    /// converts results back to the target's local space by dividing by <see cref="Scale"/>.
    /// </summary>
    internal readonly struct TargetSpace
    {
        private const float UniformTolerance = 1e-4f;

        public readonly float4x4 WorldToScaled;
        public readonly float4x4 ScaledToWorld;
        /// <summary>Target lossy scale (signed).</summary>
        public readonly float3 Scale;
        /// <summary>All scale components positive and equal — rotated primitives are safe (no shear).</summary>
        public readonly bool IsUniform;

        private TargetSpace(Transform target)
        {
            ScaledToWorld = float4x4.TRS(target.position, target.rotation, new float3(1));
            WorldToScaled = math.inverse(ScaledToWorld);
            Scale = target.lossyScale;
            var abs = math.abs(Scale);
            IsUniform = math.all(Scale > 0)
                        && math.abs(abs.x - abs.y) <= UniformTolerance * abs.x
                        && math.abs(abs.x - abs.z) <= UniformTolerance * abs.x;
        }

        public static bool TryCreate(Transform target, out TargetSpace space, out string error)
        {
            space = new TargetSpace(target);
            if (math.any(math.abs(space.Scale) < 1e-6f))
            {
                error = Strings.ErrorZeroScale;
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Scaled-space point → target local point.</summary>
        public float3 ToLocalPoint(float3 scaledPoint) => scaledPoint / Scale;

        /// <summary>Scaled-space extent → target local extent (always positive).</summary>
        public float3 ToLocalSize(float3 scaledSize) => math.abs(scaledSize / Scale);
    }
}
