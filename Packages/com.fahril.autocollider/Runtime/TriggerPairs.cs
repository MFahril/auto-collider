using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Resolves which of a group's colliders a trigger message belongs to. Unity's <c>OnTrigger*</c> only passes the
    /// other collider, so with several colliders on one object (the shared container) the part is found by overlap on
    /// Enter and remembered per other collider until Exit.
    /// </summary>
    internal sealed class TriggerPairs
    {
        private const int PruneThreshold = 64;

        private readonly Dictionary<Collider, List<ColliderEntry>> _pairs = new();
        private readonly Dictionary<(GameObject, Collider), int> _stayCursor = new();
        private readonly List<ColliderEntry> _scratch = new();
        private float _stayStep = -1f;

        public int ActivePairs
        {
            get
            {
                var count = 0;
                foreach (var list in _pairs.Values) count += list.Count;
                return count;
            }
        }

        public ColliderEntry Enter(ColliderGroup group, GameObject receiver, Collider other)
        {
            if (_pairs.Count > PruneThreshold) Prune();
            _pairs.TryGetValue(other, out var paired);

            ColliderEntry best = null, first = null;
            var bestScore = 0;
            foreach (var entry in group.Entries)
            {
                var c = entry.Collider;
                if (c == null || !c.enabled || c.gameObject != receiver) continue;
                if (!c.isTrigger && !other.isTrigger) continue;
                if (paired != null && paired.Contains(entry)) continue;

                first ??= entry;
                var score = Overlap(c, other);
                if (score > bestScore)
                {
                    best = entry;
                    bestScore = score;
                }
            }

            // A Rigidbody's object also receives the messages of its child colliders — without an overlapping candidate
            // the message belongs to one of those children, which their own relay reports.
            if (best == null && !IsBodyOfOtherColliders(group, receiver)) best = first;
            if (best == null) return null;

            if (paired == null) _pairs[other] = paired = new List<ColliderEntry>();
            paired.Add(best);
            return best;
        }

        public ColliderEntry Stay(GameObject receiver, Collider other)
        {
            if (!Collect(receiver, other)) return null;

            // One Stay message arrives per pair; cycle through this object's pairs within a physics step.
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

        public ColliderEntry Exit(GameObject receiver, Collider other)
        {
            if (!Collect(receiver, other)) return null;

            // The pair that no longer overlaps is the one that ended.
            ColliderEntry best = null;
            var bestScore = int.MaxValue;
            foreach (var entry in _scratch)
            {
                var score = Overlap(entry.Collider, other);
                if (score >= bestScore) continue;
                best = entry;
                bestScore = score;
            }

            var paired = _pairs[other];
            paired.Remove(best);
            if (paired.Count == 0) _pairs.Remove(other);
            return best;
        }

        /// <summary>Paired entries of <paramref name="other"/> whose collider lives on <paramref name="receiver"/> → <see cref="_scratch"/>.</summary>
        private bool Collect(GameObject receiver, Collider other)
        {
            _scratch.Clear();
            if (!_pairs.TryGetValue(other, out var paired)) return false;
            foreach (var entry in paired)
            {
                var c = entry.Collider;
                if (c != null && c.gameObject == receiver) _scratch.Add(entry);
            }
            return _scratch.Count > 0;
        }

        /// <summary>0 = apart, 1 = bounds touch (within contact offset), 2 = shapes overlap.</summary>
        private static int Overlap(Collider a, Collider b)
        {
            if (a == null || b == null) return 0;
            var bounds = a.bounds;
            bounds.Expand(2f * (a.contactOffset + b.contactOffset));
            if (!bounds.Intersects(b.bounds)) return 0;

            // ComputePenetration needs at least one primitive or convex mesh.
            if (a is MeshCollider { convex: false } && b is MeshCollider { convex: false }) return 1;
            var ta = a.transform;
            var tb = b.transform;
            return Physics.ComputePenetration(a, ta.position, ta.rotation, b, tb.position, tb.rotation, out _, out _) ? 2 : 1;
        }

        private static bool IsBodyOfOtherColliders(ColliderGroup group, GameObject receiver)
        {
            foreach (var entry in group.Entries)
            {
                var c = entry.Collider;
                if (c == null || c.gameObject == receiver) continue;
                var body = c.attachedRigidbody;
                if (body != null && body.gameObject == receiver) return true;
            }
            return false;
        }

        private void Prune()
        {
            var dead = new List<Collider>();
            foreach (var pair in _pairs)
                if (pair.Key == null) dead.Add(pair.Key);
            foreach (var key in dead) _pairs.Remove(key);
        }
    }
}
