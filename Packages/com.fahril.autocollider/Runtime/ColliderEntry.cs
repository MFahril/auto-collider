using System;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>One collider tracked by a <see cref="ColliderGroup"/>.</summary>
    [Serializable]
    public sealed class ColliderEntry
    {
        [SerializeField] private Collider _collider;
        [SerializeField] private string _label;
        [SerializeField] private int _partId = -1;
        [SerializeField] private bool _generated;
        [SerializeField] private bool _locked;
        [SerializeField] private bool _ownsGameObject;
        // Stored +1 so 0 (what entries serialized before this field deserialize to) means every LOD.
        [SerializeField] private int _lodPlusOne;

        public ColliderEntry(Collider collider, string label, int partId, bool generated, bool ownsGameObject = false)
        {
            _collider = collider;
            _label = label;
            _partId = partId;
            _generated = generated;
            _ownsGameObject = ownsGameObject;
        }

        /// <summary>
        /// True when the collider sits alone on a GameObject Auto Collider created for it (rotated primitive, bone collider),
        /// which is then moved / rotated as a transform and deleted together with the collider.
        /// </summary>
        public bool OwnsGameObject
        {
            get => _ownsGameObject;
            internal set => _ownsGameObject = value;
        }

        /// <summary>The tracked collider. Null if it was deleted outside the tool.</summary>
        public Collider Collider
        {
            get => _collider;
            internal set => _collider = value;
        }

        /// <summary>Display name, e.g. "Seat" or "Hull 3".</summary>
        public string Label
        {
            get => _label;
            set => _label = value;
        }

        /// <summary>
        /// Index of the generated part that produced this collider, or the <see cref="PaintedRegion.Id"/> it was built from
        /// (≥ <see cref="GenerationRecipe.FirstRegionId"/>); -1 for manual colliders.
        /// </summary>
        public int PartId
        {
            get => _partId;
            internal set => _partId = value;
        }

        /// <summary>True when Auto Collider created this collider. Only generated colliders are ever removed by the tool.</summary>
        public bool Generated => _generated;

        /// <summary>
        /// LOD level this collider was fitted for (-1 = every LOD). A <see cref="ColliderLodFollower"/> enables only the
        /// colliders of the active LOD.
        /// </summary>
        public int Lod
        {
            get => _lodPlusOne - 1;
            set => _lodPlusOne = Mathf.Max(-1, value) + 1;
        }

        /// <summary>Locked entries survive regenerate and remove operations.</summary>
        public bool Locked
        {
            get => _locked;
            set => _locked = value;
        }
    }
}
