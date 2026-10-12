using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// The multi-collider "bridge": owns every collider generated for this object and controls them as one unit.
    /// Game code talks to the group instead of to individual colliders — including collision and trigger events,
    /// which report the <see cref="ColliderEntry"/> (part) that was hit.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Physics/Auto Collider/Collider Group")]
    public sealed class ColliderGroup : MonoBehaviour, ISerializationCallbackReceiver
    {
        [SerializeField] private List<ColliderEntry> _entries = new();
        [SerializeField] private Transform _container;
        [SerializeField] private GenerationRecipe _recipe = new();

        private Dictionary<Collider, ColliderEntry> _lookup;
        private TriggerPairs _triggerPairs;
        private bool _routing;
        private bool _stayRouting;

        private Action<ColliderEntry, Collision> _collisionEnter, _collisionStay, _collisionExit;
        private Action<ColliderEntry, Collider> _triggerEnter, _triggerStay, _triggerExit;

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

        // ---------- Events ----------

        /// <summary>
        /// One of the group's colliders started touching another collider. Reports the entry that was hit; works with or
        /// without a Rigidbody (messages are routed through <see cref="ColliderRelay"/> components added at runtime on first subscription).
        /// </summary>
        public event Action<ColliderEntry, Collision> CollisionEnter
        {
            add { _collisionEnter += value; EnsureRouting(false); }
            remove => _collisionEnter -= value;
        }

        /// <summary>Called every physics step while one of the group's colliders keeps touching another collider.</summary>
        public event Action<ColliderEntry, Collision> CollisionStay
        {
            add { _collisionStay += value; EnsureRouting(true); }
            remove => _collisionStay -= value;
        }

        /// <summary>One of the group's colliders stopped touching another collider.</summary>
        public event Action<ColliderEntry, Collision> CollisionExit
        {
            add { _collisionExit += value; EnsureRouting(false); }
            remove => _collisionExit -= value;
        }

        /// <summary>
        /// A collider entered one of the group's triggers (or one of the group's colliders entered a trigger).
        /// The second argument is the other collider. Unity's trigger messages don't say which of several colliders on the
        /// same object was involved; the group resolves it by overlap.
        /// </summary>
        public event Action<ColliderEntry, Collider> TriggerEnter
        {
            add { _triggerEnter += value; EnsureRouting(false); }
            remove => _triggerEnter -= value;
        }

        /// <summary>Called every physics step while a trigger overlap involving one of the group's colliders lasts.</summary>
        public event Action<ColliderEntry, Collider> TriggerStay
        {
            add { _triggerStay += value; EnsureRouting(true); }
            remove => _triggerStay -= value;
        }

        /// <summary>A trigger overlap involving one of the group's colliders ended.</summary>
        public event Action<ColliderEntry, Collider> TriggerExit
        {
            add { _triggerExit += value; EnsureRouting(false); }
            remove => _triggerExit -= value;
        }

        /// <summary>
        /// Add relays to the objects that receive physics messages for the tracked colliders (play mode only).
        /// Called automatically when an event gets its first subscriber; call it again after adding colliders at runtime.
        /// </summary>
        public void RefreshRouting()
        {
            if (!_routing || !Application.isPlaying) return;
            foreach (var entry in _entries)
            {
                var c = entry.Collider;
                if (c == null) continue;
                AddRelay(c.gameObject);
                var body = c.attachedRigidbody;
                if (body != null) AddRelay(body.gameObject);
            }
        }

        private void EnsureRouting(bool stay)
        {
            var changed = !_routing || (stay && !_stayRouting);
            _routing = true;
            _stayRouting |= stay;
            if (changed) RefreshRouting();
        }

        private void AddRelay(GameObject host)
        {
            if (!host.TryGetComponent<ColliderRelay>(out var relay)) relay = host.AddComponent<ColliderRelay>();
            relay.Register(this);
            if (_stayRouting && !host.TryGetComponent<ColliderStayRelay>(out _)) host.AddComponent<ColliderStayRelay>();
        }

        internal void OnRelayCollision(GameObject receiver, Collision collision, RelayPhase phase)
        {
            var mine = collision.thisCollider;
            if (mine == null || !TryGetEntry(mine, out var entry)) return;

            // Unity sends collision messages to the Rigidbody's object (and to the collider's own object without one);
            // accepting only that receiver keeps every contact from being reported twice.
            var body = mine.attachedRigidbody;
            if (receiver != (body != null ? body.gameObject : mine.gameObject)) return;

            var handler = phase switch
            {
                RelayPhase.Enter => _collisionEnter,
                RelayPhase.Stay => _collisionStay,
                _ => _collisionExit,
            };
            handler?.Invoke(entry, collision);
        }

        internal void OnRelayTrigger(GameObject receiver, Collider other, RelayPhase phase)
        {
            if (other == null) return;
            _triggerPairs ??= new TriggerPairs();
            var entry = phase switch
            {
                RelayPhase.Enter => _triggerPairs.Enter(this, receiver, other),
                RelayPhase.Stay => _triggerPairs.Stay(receiver, other),
                _ => _triggerPairs.Exit(receiver, other),
            };
            if (entry == null) return;

            var handler = phase switch
            {
                RelayPhase.Enter => _triggerEnter,
                RelayPhase.Stay => _triggerStay,
                _ => _triggerExit,
            };
            handler?.Invoke(entry, other);
        }

        // ---------- Queries ----------

        /// <summary>Find the entry that tracks <paramref name="collider"/>.</summary>
        public bool TryGetEntry(Collider collider, out ColliderEntry entry)
        {
            entry = null;
            if (collider == null) return false;

            _lookup ??= BuildLookup();
            if (_lookup.TryGetValue(collider, out entry) && entry.Collider == collider) return true;

            // Entries can be re-pointed (edit tool) — rebuild once before giving up.
            _lookup = BuildLookup();
            return _lookup.TryGetValue(collider, out entry);
        }

        private Dictionary<Collider, ColliderEntry> BuildLookup()
        {
            var lookup = new Dictionary<Collider, ColliderEntry>(_entries.Count);
            foreach (var e in _entries)
                if (e.Collider != null) lookup[e.Collider] = e;
            return lookup;
        }

        /// <summary>Entries matching a predicate (live colliders only).</summary>
        public IEnumerable<ColliderEntry> Where(Func<ColliderEntry, bool> predicate)
        {
            foreach (var e in _entries)
                if (e.Collider != null && predicate(e))
                    yield return e;
        }

        // ---------- Bulk control ----------

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

        /// <summary>
        /// Per-collider layer overrides on every tracked collider: extra layers to collide with (<paramref name="include"/>),
        /// layers to ignore (<paramref name="exclude"/>) and the priority that decides conflicts with the other collider's overrides.
        /// </summary>
        public void SetLayerOverrides(LayerMask include, LayerMask exclude, int priority)
        {
            foreach (var e in _entries)
            {
                var c = e.Collider;
                if (c == null) continue;
                c.includeLayers = include;
                c.excludeLayers = exclude;
                c.layerOverridePriority = priority;
            }
        }

        /// <summary>Turn contact reporting for <c>Physics.ContactEvent</c> on or off for every tracked collider.</summary>
        public void SetProvidesContacts(bool providesContacts)
        {
            foreach (var e in _entries)
                if (e.Collider != null) e.Collider.providesContacts = providesContacts;
        }

        // ---------- Entry management (editor) ----------

        internal void AddEntry(ColliderEntry entry)
        {
            _entries.Add(entry);
            _lookup = null;
        }

        internal bool RemoveEntry(ColliderEntry entry)
        {
            _lookup = null;
            return _entries.Remove(entry);
        }

        /// <summary>Drop entries whose collider was deleted outside the tool. Returns the number removed.</summary>
        internal int RemoveMissing()
        {
            _lookup = null;
            return _entries.RemoveAll(e => e.Collider == null);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        // Undo / prefab reverts replace the entry objects.
        void ISerializationCallbackReceiver.OnAfterDeserialize() => _lookup = null;
    }
}
