using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// The multi-collider "bridge": owns every collider generated for this object and controls them as one unit.
    /// Game code talks to the group instead of to individual colliders.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Physics/Auto Collider/Collider Group")]
    public sealed class ColliderGroup : MonoBehaviour
    {
        [SerializeField] private List<ColliderEntry> _entries = new();
        [SerializeField] private Transform _container;
        [SerializeField] private GenerationRecipe _recipe = new();

        /// <summary>All tracked colliders (generated and manually registered).</summary>
        public IReadOnlyList<ColliderEntry> Entries => _entries;

        /// <summary>Child object that holds generated colliders. May be null before the first generation.</summary>
        public Transform Container
        {
            get => _container;
            internal set => _container = value;
        }

        /// <summary>Settings that produced the generated colliders (used by Regenerate).</summary>
        public GenerationRecipe Recipe => _recipe ??= new GenerationRecipe();

        /// <summary>Number of entries whose collider still exists.</summary>
        public int LiveCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries)
                    if (entry.Collider != null) count++;
                return count;
            }
        }

        /// <summary>Combined world bounds of all enabled colliders. Empty bounds at the transform position if none.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var hasAny = false;
                var bounds = new Bounds(transform.position, Vector3.zero);
                foreach (var entry in _entries)
                {
                    var c = entry.Collider;
                    if (c == null || !c.enabled) continue;
                    if (hasAny) bounds.Encapsulate(c.bounds);
                    else bounds = c.bounds;
                    hasAny = true;
                }
                return bounds;
            }
        }

        /// <summary>Find the entry that tracks <paramref name="collider"/>.</summary>
        public bool TryGetEntry(Collider collider, out ColliderEntry entry)
        {
            foreach (var e in _entries)
            {
                if (e.Collider == collider && collider != null)
                {
                    entry = e;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        /// <summary>Entries matching a predicate (live colliders only).</summary>
        public IEnumerable<ColliderEntry> Where(Func<ColliderEntry, bool> predicate)
        {
            foreach (var e in _entries)
                if (e.Collider != null && predicate(e))
                    yield return e;
        }

        /// <summary>Enable or disable every tracked collider.</summary>
        public void SetEnabled(bool enabled)
        {
            foreach (var e in _entries)
                if (e.Collider != null) e.Collider.enabled = enabled;
        }

        /// <summary>Set <see cref="Collider.isTrigger"/> on every tracked collider. Concave mesh colliders cannot be triggers and are skipped.</summary>
        public void SetTrigger(bool isTrigger)
        {
            foreach (var e in _entries)
            {
                var c = e.Collider;
                if (c == null) continue;
                if (isTrigger && c is MeshCollider { convex: false }) continue;
                c.isTrigger = isTrigger;
            }
        }

        /// <summary>Assign a physics material to every tracked collider.</summary>
        public void SetPhysicsMaterial(PhysicsMaterial material)
        {
            foreach (var e in _entries)
                if (e.Collider != null) e.Collider.sharedMaterial = material;
        }

        /// <summary>Move every collider's GameObject to <paramref name="layer"/>. Only objects that hold nothing but colliders are changed, so source meshes keep their layer.</summary>
        public void SetLayer(int layer)
        {
            foreach (var e in _entries)
            {
                var c = e.Collider;
                if (c == null) continue;
                if (c.transform == transform || c.GetComponent<Renderer>() != null) continue;
                c.gameObject.layer = layer;
            }
        }

        internal void AddEntry(ColliderEntry entry) => _entries.Add(entry);

        internal bool RemoveEntry(ColliderEntry entry) => _entries.Remove(entry);

        /// <summary>Drop entries whose collider was deleted outside the tool. Returns the number removed.</summary>
        internal int RemoveMissing() => _entries.RemoveAll(e => e.Collider == null);
    }
}
