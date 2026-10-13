using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// 2D counterpart of <see cref="ColliderRelay"/>: forwards Enter / Exit 2D physics messages to the
    /// <see cref="ColliderGroup"/>s whose 2D colliders live on this object (or whose Rigidbody2D it is). Added automatically at
    /// runtime when a group's 2D event gets a subscriber.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ColliderRelay2D : MonoBehaviour
    {
        private readonly List<ColliderGroup> _groups = new();

        /// <summary>Groups this relay forwards to.</summary>
        public IReadOnlyList<ColliderGroup> Groups => _groups;

        internal void Register(ColliderGroup group)
        {
            if (!_groups.Contains(group)) _groups.Add(group);
        }

        private void OnCollisionEnter2D(Collision2D collision) => Collision(collision, RelayPhase.Enter);
        private void OnCollisionExit2D(Collision2D collision) => Collision(collision, RelayPhase.Exit);
        private void OnTriggerEnter2D(Collider2D other) => Trigger(other, RelayPhase.Enter);
        private void OnTriggerExit2D(Collider2D other) => Trigger(other, RelayPhase.Exit);

        internal void Collision(Collision2D collision, RelayPhase phase)
        {
            for (var i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null) _groups.RemoveAt(i);
                else group.OnRelayCollision2D(gameObject, collision, phase);
            }
        }

        internal void Trigger(Collider2D other, RelayPhase phase)
        {
            for (var i = _groups.Count - 1; i >= 0; i--)
            {
                var group = _groups[i];
                if (group == null) _groups.RemoveAt(i);
                else group.OnRelayTrigger2D(gameObject, other, phase);
            }
        }
    }
}
