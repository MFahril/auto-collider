using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Forwards Enter / Exit physics messages from the object it sits on to the <see cref="ColliderGroup"/>s whose colliders
    /// live there (or whose Rigidbody it is). Added automatically at runtime when a group event gets a subscriber —
    /// you never need to add it yourself.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ColliderRelay : MonoBehaviour
    {
        private readonly List<ColliderGroup> _groups = new();

        /// <summary>Groups this relay forwards to.</summary>
        public IReadOnlyList<ColliderGroup> Groups => _groups;

        internal void Register(ColliderGroup group)
        {
            if (!_groups.Contains(group)) _groups.Add(group);
        }

        private void OnCollisionEnter(Collision collision) => Collision(collision, RelayPhase.Enter);
        private void OnCollisionExit(Collision collision) => Collision(collision, RelayPhase.Exit);
        private void OnTriggerEnter(Collider other) => Trigger(other, RelayPhase.Enter);
        private void OnTriggerExit(Collider other) => Trigger(other, RelayPhase.Exit);

        internal void Collision(Collision collision, RelayPhase phase)
        {
            for (var i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null) _groups.RemoveAt(i);
                else group.OnRelayCollision(gameObject, collision, phase);
            }
        }

        internal void Trigger(Collider other, RelayPhase phase)
        {
            for (var i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null) _groups.RemoveAt(i);
                else group.OnRelayTrigger(gameObject, other, phase);
            }
        }
    }
}
