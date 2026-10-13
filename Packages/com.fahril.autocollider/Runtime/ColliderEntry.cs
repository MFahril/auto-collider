using System;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// One collider tracked by a <see cref="ColliderGroup"/> — a 3D <see cref="UnityEngine.Collider"/> or a 2D
    /// <see cref="UnityEngine.Collider2D"/> (exactly one of the two is set).
    /// </summary>
    [Serializable]
    public sealed class ColliderEntry
    {
        [SerializeField] private Collider _collider;
        [SerializeField] private Collider2D _collider2D;
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

        public ColliderEntry(Collider2D collider, string label, int partId, bool generated, bool ownsGameObject = false)
        {
            _collider2D = collider;
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

        /// <summary>The tracked 3D collider. Null for 2D entries, or if it was deleted outside the tool.</summary>
        public Collider Collider
        {
            get => _collider;
            internal set => _collider = value;
        }

        /// <summary>The tracked 2D collider. Null for 3D entries, or if it was deleted outside the tool.</summary>
        public Collider2D Collider2D
        {
            get => _collider2D;
            internal set => _collider2D = value;
        }

        /// <summary>True for an entry whose (live) collider is a <see cref="UnityEngine.Collider2D"/>.</summary>
        public bool Is2D => _collider2D != null;

        /// <summary>True while the tracked collider (3D or 2D) still exists.</summary>
        public bool IsAlive => _collider != null || _collider2D != null;

        /// <summary>The tracked collider as a component (3D or 2D), or null when it was deleted.</summary>
        public Component Component => _collider != null ? _collider : _collider2D != null ? _collider2D : null;

        /// <summary>Enabled state of the collider, whichever kind it is.</summary>
        public bool Enabled
        {
            get => _collider != null ? _collider.enabled : _collider2D != null && _collider2D.enabled;
            set
            {
                if (_collider != null) _collider.enabled = value;
                else if (_collider2D != null) _collider2D.enabled = value;
            }
        }

        /// <summary>Trigger state of the collider. Concave mesh colliders can't be triggers and ignore <c>true</c>.</summary>
        public bool IsTrigger
        {
            get => _collider != null ? _collider.isTrigger : _collider2D != null && _collider2D.isTrigger;
            set
            {
                if (_collider != null)
                {
                    if (!(value && _collider is MeshCollider { convex: false })) _collider.isTrigger = value;
                }
                else if (_collider2D != null)
                {
                    _collider2D.isTrigger = value;
                }
            }
        }

        /// <summary>World bounds of the collider (empty when it was deleted).</summary>
        public Bounds Bounds => _collider != null ? _collider.bounds : _collider2D != null ? _collider2D.bounds : default;

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
