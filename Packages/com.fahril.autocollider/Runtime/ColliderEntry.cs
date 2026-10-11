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

        public ColliderEntry(Collider collider, string label, int partId, bool generated)
        {
            _collider = collider;
            _label = label;
            _partId = partId;
            _generated = generated;
        }

        /// <summary>The tracked collider. Null if it was deleted outside the tool.</summary>
        public Collider Collider => _collider;

        /// <summary>Display name, e.g. "Seat" or "Hull 3".</summary>
        public string Label
        {
            get => _label;
            set => _label = value;
        }

        /// <summary>Index of the generated part that produced this collider; -1 for manual colliders.</summary>
        public int PartId => _partId;

        /// <summary>True when Auto Collider created this collider. Only generated colliders are ever removed by the tool.</summary>
        public bool Generated => _generated;

        /// <summary>Locked entries survive regenerate and remove operations.</summary>
        public bool Locked
        {
            get => _locked;
            set => _locked = value;
        }
    }
}
