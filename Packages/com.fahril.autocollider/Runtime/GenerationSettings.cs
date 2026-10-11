using System;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Serializable collider generation settings. Stored on <see cref="ColliderGroup"/> (inside its recipe)
    /// so a group can be regenerated with exactly the settings that created it.
    /// </summary>
    [Serializable]
    public sealed class GenerationSettings
    {
        [Tooltip("How collider shapes are produced from each mesh.")]
        public ColliderStrategy Strategy = ColliderStrategy.BestPrimitive;

        [Tooltip("Per Renderer: every mesh gets its own collider. Combined: the whole object is fitted as one shape.")]
        public MeshGranularity Granularity = MeshGranularity.PerRenderer;

        [Tooltip("Allow rotated (oriented) boxes and capsules. They are placed on their own child objects. Ignored on objects with non-uniform scale.")]
        public bool AllowRotation = true;

        [Tooltip("Include meshes on child objects.")]
        public bool IncludeChildren = true;

        [Tooltip("Include inactive child objects and disabled renderers.")]
        public bool IncludeInactive;

        [Tooltip("With a LODGroup, only use LOD 0 renderers.")]
        public bool Lod0Only = true;

        [Tooltip("Skip renderers whose largest world-space size is below this value (0 = keep all).")]
        [Min(0f)]
        public float MinSize;

        [Tooltip("Skip objects whose name matches this regular expression (empty = none).")]
        public string ExcludeNamePattern = string.Empty;

        [Tooltip("What happens to colliders that already exist on the target.")]
        public ReplaceMode Replace = ReplaceMode.ReplaceGenerated;

        [Tooltip("Make generated colliders triggers.")]
        public bool IsTrigger;

        [Tooltip("Physics material assigned to generated colliders (optional).")]
        public PhysicsMaterial Material;

        /// <summary>Deep copy (the material reference is shared).</summary>
        public GenerationSettings Clone() => (GenerationSettings)MemberwiseClone();
    }
}
