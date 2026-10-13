using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Resolves which of a group's 2D colliders a trigger message belongs to. <c>OnTrigger*2D</c> only passes the other
    /// collider; 2D physics knows exactly which shapes touch (<see cref="Collider2D.IsTouching(Collider2D)"/>), so on Enter the
    /// receiver's touching, not-yet-paired collider is taken, and the pair is remembered until Exit.
    /// </summary>
    internal sealed class TriggerPairs2D
    {
        private const int PruneThreshold = 64;

        private readonly Dictionary<Collider2D, List<ColliderEntry>> _pairs = new();
        private readonly Dictionary<(GameObject, Collider2D), int> _stayCursor = new();
        private readonly List<ColliderEntry> _scratch = new();
        private float _stayStep = -1f;

        public ColliderEntry Enter(ColliderGroup group, GameObject receiver, Collider2D other)
        {
            if (_pairs.Count > PruneThreshold) Prune();
            _pairs.TryGetValue(other, out var paired);

            ColliderEntry touching = null, first = null;
            foreach (var entry in group.Entries)
            {
                var c = entry.Collider2D;
                if (c == null || !c.enabled || c.gameObject != receiver) continue;
                if (!c.isTrigger && !other.isTrigger) continue;
                if (paired != null && paired.Contains(entry)) continue;
                first ??= entry;
                if (c.IsTouching(other))
                {
                    touching = entry;
                    break;
                }
            }

            // A Rigidbody2D's object also receives its children's messages: without a touching collider of its own,
            // the message belongs to a child, whose relay reports it.
            var best = touching ?? (IsBodyOfOtherColliders(group, receiver) ? null : first);
            if (best == null) return null;
            if (paired == null) _pairs[other] = paired = new List<ColliderEntry>();
            paired.Add(best);
            return best;
        }

        public ColliderEntry Stay(GameObject receiver, Collider2D other)
        {
            if (!Collect(receiver, other)) return null;
            if (!Mathf.Approximately(Time.fixedTime, _stayStep))
            {
                _stayStep = Time.fixedTime;
                _stayCursor.Clear();
            }
            var key = (receiver, other);
            _stayCursor.TryGetValue(key, out var cursor);
            _stayCursor[key] = cursor + 1;
            return _scratch[cursor % _scratch.Count];
        }

        public ColliderEntry Exit(GameObject receiver, Collider2D other)
        {
            if (!Collect(receiver, other)) return null;
            // The pair that stopped touching is the one that ended.
            var ended = _scratch[0];
            foreach (var entry in _scratch)
            {
                if (entry.Collider2D.IsTouching(other)) continue;
                ended = entry;
                break;
            }
            var paired = _pairs[other];
            paired.Remove(ended);
            if (paired.Count == 0) _pairs.Remove(other);
            return ended;
        }

        private bool Collect(GameObject receiver, Collider2D other)
        {
            _scratch.Clear();
            if (!_pairs.TryGetValue(other, out var paired)) return false;
            foreach (var entry in paired)
            {
                var c = entry.Collider2D;
                if (c != null && c.gameObject == receiver) _scratch.Add(entry);
            }
            return _scratch.Count > 0;
        }

        private static bool IsBodyOfOtherColliders(ColliderGroup group, GameObject receiver)
        {
            foreach (var entry in group.Entries)
            {
                var c = entry.Collider2D;
                if (c == null || c.gameObject == receiver) continue;
                var body = c.attachedRigidbody;
                if (body != null && body.gameObject == receiver) return true;
            }
            return false;
        }

        private void Prune()
        {
            var dead = new List<Collider2D>();
            foreach (var pair in _pairs)
                if (pair.Key == null) dead.Add(pair.Key);
            foreach (var key in dead) _pairs.Remove(key);
        }
    }
}
