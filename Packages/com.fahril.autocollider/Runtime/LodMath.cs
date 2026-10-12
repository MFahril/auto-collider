using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Active-LOD computation that mirrors <see cref="LODGroup"/>'s own rule (Unity exposes no API for the current LOD):
    /// screen-relative height of the group's size, scaled by <see cref="QualitySettings.lodBias"/>, against each level's
    /// transition height.
    /// </summary>
    internal static class LodMath
    {
        /// <summary>Fraction of the screen height the group covers for <paramref name="camera"/> (before LOD bias).</summary>
        public static float RelativeHeight(LODGroup group, Camera camera)
        {
            var t = group.transform;
            var scale = t.lossyScale;
            var size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (camera.orthographic) return size * 0.5f / Mathf.Max(camera.orthographicSize, 1e-5f);

            var distance = Vector3.Distance(camera.transform.position, t.TransformPoint(group.localReferencePoint));
            var halfAngle = Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f);
            return size * 0.5f / Mathf.Max(distance * halfAngle, 1e-5f);
        }

        /// <summary>
        /// Index of the LOD level <paramref name="camera"/> would show; <c>lodCount</c> when the group is culled.
        /// Honors <see cref="QualitySettings.maximumLODLevel"/>.
        /// </summary>
        public static int ActiveLod(LODGroup group, Camera camera)
        {
            var lods = group.GetLODs();
            var height = RelativeHeight(group, camera) * QualitySettings.lodBias;
            var index = lods.Length;
            for (var i = 0; i < lods.Length; i++)
            {
                if (height < lods[i].screenRelativeTransitionHeight) continue;
                index = i;
                break;
            }
            return index < lods.Length ? Mathf.Max(index, Mathf.Min(QualitySettings.maximumLODLevel, lods.Length - 1)) : index;
        }
    }
}
