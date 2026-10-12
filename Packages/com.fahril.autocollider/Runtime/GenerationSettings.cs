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
        [Tooltip("How collider shapes are produced from each mesh. Auto picks the cheapest shape that fits.")]
        public ColliderStrategy Strategy = ColliderStrategy.Auto;

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

        [Tooltip("Mesh simplification amount. 1 = keep almost all detail, 0 = simplify as much as possible.")]
        [Range(0f, 1f)]
        public float Quality = 0.5f;

        [Tooltip("How closely the simplified surface follows the original (keeps corners and edges). Higher = more accurate, same triangle count.")]
        [Range(0f, 1f)]
        public float ShapeFidelity = 0.6f;

        [Tooltip("Upper limit for triangles per optimized mesh collider (0 = no limit). Simplification increases until it fits.")]
        [Min(0)]
        public int MaxTriangles;

        [Tooltip("Grow (+) or shrink (−) colliders along their surface, in meters. Fixes small penetration or clearance issues.")]
        public float Inflate;

        [Tooltip("Drop small disconnected mesh pieces (bolts, decals) smaller than this fraction of the object size before optimizing (0 = keep all).")]
        [Range(0f, 0.5f)]
        public float MinIslandSize;

        [Tooltip("Convex decomposition detail: voxels along the longest side. Higher = more accurate parts, slower.")]
        [Range(16, 128)]
        public int DecompositionResolution = 40;

        [Tooltip("Upper limit for convex parts per mesh. Disconnected pieces always get their own part.")]
        [Range(1, 64)]
        public int MaxParts = 12;

        [Tooltip("How much empty space a merge of two parts may add (fraction of the object volume). Lower = more, tighter parts.")]
        [Range(0f, 0.25f)]
        public float ConcavityTolerance = 0.03f;

        [Tooltip("A convex part (or, for Auto, a whole mesh) becomes a box / sphere / capsule when it fills at least this fraction of it. 1 = never.")]
        [Range(0.5f, 1f)]
        public float PrimitiveFitThreshold = 0.85f;

        [Tooltip("Auto: meshes at least this convex (volume ÷ hull volume) get one convex hull; less convex meshes get decomposition (moving) or an optimized mesh (static).")]
        [Range(0.5f, 1f)]
        public float ConvexityThreshold = 0.85f;

        [Tooltip("Skinned meshes (Per Bone, and Auto): collider shape created for each bone.")]
        public PartShape BoneShape = PartShape.Capsule;

        [Tooltip("A vertex belongs to its strongest bone only if that bone's weight is at least this (0.5 = mostly that bone).")]
        [Range(0f, 1f)]
        public float BoneWeightThreshold = 0.5f;

        [Tooltip("Bones with fewer vertices than this get no collider (fingers, twist bones, …).")]
        [Min(4)]
        public int MinBoneVertices = 12;

        [Tooltip("Give left / right bone pairs (…_L / …_R, Left… / Right…) the same collider size.")]
        public bool SymmetricBones = true;

        /// <summary>Deep copy (the material reference is shared).</summary>
        public GenerationSettings Clone() => (GenerationSettings)MemberwiseClone();
    }
}
