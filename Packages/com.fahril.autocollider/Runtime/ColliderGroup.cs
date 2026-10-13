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
        [SerializeField] private Transform _container2D;
        [SerializeField] private GenerationRecipe _recipe = new();
        [SerializeField] private List<Component> _ownedComponents = new();

        private Dictionary<Component, ColliderEntry> _lookup;
        private TriggerPairs _triggerPairs;
        private TriggerPairs2D _triggerPairs2D;
        private bool _routing;
        private bool _stayRouting;

        private Action<ColliderEntry, Collision> _collisionEnter, _collisionStay, _collisionExit;
        private Action<ColliderEntry, Collider> _triggerEnter, _triggerStay, _triggerExit;
        private Action<ColliderEntry, Collision2D> _collisionEnter2D, _collisionStay2D, _collisionExit2D;
        private Action<ColliderEntry, Collider2D> _triggerEnter2D, _triggerStay2D, _triggerExit2D;

        /// <summary>All tracked colliders (generated and manually registered).</summary>
        public IReadOnlyList<ColliderEntry> Entries => _entries;

        /// <summary>Child object that holds generated 3D colliders. May be null before the first generation.</summary>
        public Transform Container
        {
            get => _container;
            internal set => _container = value;
        }

        /// <summary>
        /// Child object that holds generated 2D colliders (Unity doesn't allow 2D and 3D colliders on one GameObject).
        /// May be null before the first 2D generation.
        /// </summary>
        public Transform Container2D
        {
            get => _container2D;
            internal set => _container2D = value;
        }

        /// <summary>True when <paramref name="t"/> is (or is inside) one of the generated-collider containers.</summary>
        public bool IsInContainer(Transform t) =>
            t != null && ((_container != null && t.IsChildOf(_container)) || (_container2D != null && t.IsChildOf(_container2D)));

        /// <summary>Settings that produced the generated colliders (used by Regenerate).</summary>
        public GenerationRecipe Recipe => _recipe ??= new GenerationRecipe();

        /// <summary>Number of entries whose collider still exists.</summary>
        public int LiveCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _entries)
                    if (entry.IsAlive) count++;
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
                    if (!entry.IsAlive || !entry.Enabled) continue;
                    if (hasAny) bounds.Encapsulate(entry.Bounds);
                    else bounds = entry.Bounds;
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
        /// One of the group's 2D colliders started touching another 2D collider. Reports the entry that was hit
        /// (exact, from <see cref="Collision2D.otherCollider"/>); works with or without a Rigidbody2D.
        /// </summary>
        public event Action<ColliderEntry, Collision2D> CollisionEnter2D
        {
            add { _collisionEnter2D += value; EnsureRouting(false); }
            remove => _collisionEnter2D -= value;
        }

        /// <summary>Called every physics step while one of the group's 2D colliders keeps touching another 2D collider.</summary>
        public event Action<ColliderEntry, Collision2D> CollisionStay2D
        {
            add { _collisionStay2D += value; EnsureRouting(true); }
            remove => _collisionStay2D -= value;
        }

        /// <summary>One of the group's 2D colliders stopped touching another 2D collider.</summary>
        public event Action<ColliderEntry, Collision2D> CollisionExit2D
        {
            add { _collisionExit2D += value; EnsureRouting(false); }
            remove => _collisionExit2D -= value;
        }

        /// <summary>
        /// A 2D collider entered one of the group's 2D triggers (or one of the group's 2D colliders entered a trigger).
        /// The second argument is the other collider; the group's part is resolved with <see cref="Collider2D.IsTouching(Collider2D)"/>.
        /// </summary>
        public event Action<ColliderEntry, Collider2D> TriggerEnter2D
        {
            add { _triggerEnter2D += value; EnsureRouting(false); }
            remove => _triggerEnter2D -= value;
        }

        /// <summary>Called every physics step while a 2D trigger overlap involving one of the group's colliders lasts.</summary>
        public event Action<ColliderEntry, Collider2D> TriggerStay2D
        {
            add { _triggerStay2D += value; EnsureRouting(true); }
            remove => _triggerStay2D -= value;
        }

        /// <summary>A 2D trigger overlap involving one of the group's colliders ended.</summary>
        public event Action<ColliderEntry, Collider2D> TriggerExit2D
        {
            add { _triggerExit2D += value; EnsureRouting(false); }
            remove => _triggerExit2D -= value;
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
                if (c != null)
                {
                    AddRelay(c.gameObject);
                    var body = c.attachedRigidbody;
                    if (body != null) AddRelay(body.gameObject);
                }

                var c2 = entry.Collider2D;
                if (c2 != null)
                {
                    AddRelay2D(c2.gameObject);
                    var body2 = c2.attachedRigidbody;
                    if (body2 != null) AddRelay2D(body2.gameObject);
                }
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

        private void AddRelay2D(GameObject host)
        {
            if (!host.TryGetComponent<ColliderRelay2D>(out var relay)) relay = host.AddComponent<ColliderRelay2D>();
            relay.Register(this);
            if (_stayRouting && !host.TryGetComponent<ColliderStayRelay2D>(out _)) host.AddComponent<ColliderStayRelay2D>();
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

        internal void OnRelayCollision2D(GameObject receiver, Collision2D collision, RelayPhase phase)
        {
            // In 2D, otherCollider is the receiving side's collider — ours.
            var mine = collision.otherCollider;
            if (mine == null || !TryGetEntry(mine, out var entry)) return;

            // Same canonical receiver as 3D: the Rigidbody2D's object, else the collider's own object.
            var body = mine.attachedRigidbody;
            if (receiver != (body != null ? body.gameObject : mine.gameObject)) return;

            var handler = phase switch
            {
                RelayPhase.Enter => _collisionEnter2D,
                RelayPhase.Stay => _collisionStay2D,
                _ => _collisionExit2D,
            };
            handler?.Invoke(entry, collision);
        }

        internal void OnRelayTrigger2D(GameObject receiver, Collider2D other, RelayPhase phase)
        {
            if (other == null) return;
            _triggerPairs2D ??= new TriggerPairs2D();
            var entry = phase switch
            {
                RelayPhase.Enter => _triggerPairs2D.Enter(this, receiver, other),
                RelayPhase.Stay => _triggerPairs2D.Stay(receiver, other),
                _ => _triggerPairs2D.Exit(receiver, other),
            };
            if (entry == null) return;

            var handler = phase switch
            {
                RelayPhase.Enter => _triggerEnter2D,
                RelayPhase.Stay => _triggerStay2D,
                _ => _triggerExit2D,
            };
            handler?.Invoke(entry, other);
        }

        // ---------- Queries ----------

        /// <summary>Find the entry that tracks the 3D <paramref name="collider"/>.</summary>
        public bool TryGetEntry(Collider collider, out ColliderEntry entry) => TryFind(collider, out entry);

        /// <summary>Find the entry that tracks the 2D <paramref name="collider"/>.</summary>
        public bool TryGetEntry(Collider2D collider, out ColliderEntry entry) => TryFind(collider, out entry);

        private bool TryFind(Component collider, out ColliderEntry entry)
        {
            entry = null;
            if (collider == null) return false;

            _lookup ??= BuildLookup();
            if (_lookup.TryGetValue(collider, out entry) && entry.Component == collider) return true;

            // Entries can be re-pointed (edit tool) — rebuild once before giving up.
            _lookup = BuildLookup();
            return _lookup.TryGetValue(collider, out entry);
        }

        private Dictionary<Component, ColliderEntry> BuildLookup()
        {
            var lookup = new Dictionary<Component, ColliderEntry>(_entries.Count);
            foreach (var e in _entries)
            {
                var component = e.Component;
                if (component != null) lookup[component] = e;
            }
            return lookup;
        }

        /// <summary>Entries matching a predicate (live colliders only, 3D and 2D).</summary>
        public IEnumerable<ColliderEntry> Where(Func<ColliderEntry, bool> predicate)
        {
            foreach (var e in _entries)
                if (e.IsAlive && predicate(e))
                    yield return e;
        }

        // ---------- Bulk control ----------

        /// <summary>Enable or disable every tracked collider (3D and 2D).</summary>
        public void SetEnabled(bool enabled)
        {
            foreach (var e in _entries)
                if (e.IsAlive) e.Enabled = enabled;
        }

        /// <summary>Set the trigger flag on every tracked collider (3D and 2D). Concave mesh colliders cannot be triggers and are skipped.</summary>
        public void SetTrigger(bool isTrigger)
        {
            foreach (var e in _entries)
                if (e.IsAlive) e.IsTrigger = isTrigger;
        }

        /// <summary>Assign a physics material to every tracked 3D collider.</summary>
        public void SetPhysicsMaterial(PhysicsMaterial material)
        {
            foreach (var e in _entries)
                if (e.Collider != null) e.Collider.sharedMaterial = material;
        }

        /// <summary>Assign a 2D physics material to every tracked 2D collider.</summary>
        public void SetPhysicsMaterial(PhysicsMaterial2D material)
        {
            foreach (var e in _entries)
                if (e.Collider2D != null) e.Collider2D.sharedMaterial = material;
        }

        /// <summary>Move every collider's GameObject to <paramref name="layer"/>. Only objects that hold nothing but colliders are changed, so source meshes and sprites keep their layer.</summary>
        public void SetLayer(int layer)
        {
            foreach (var e in _entries)
            {
                var c = e.Component;
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
                if (c != null)
                {
                    c.includeLayers = include;
                    c.excludeLayers = exclude;
                    c.layerOverridePriority = priority;
                }

                var c2 = e.Collider2D;
                if (c2 != null)
                {
                    c2.includeLayers = include;
                    c2.excludeLayers = exclude;
                    c2.layerOverridePriority = priority;
                }
            }
        }

        /// <summary>Turn contact reporting for <c>Physics.ContactEvent</c> on or off for every tracked 3D collider.</summary>
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

        /// <summary>
        /// Non-collider components Auto Collider added for its colliders (e.g. the static Rigidbody2D a tilemap's
        /// CompositeCollider2D needs); removed together with the colliders.
        /// </summary>
        internal List<Component> OwnedComponents => _ownedComponents ??= new List<Component>();

        /// <summary>Drop entries whose collider was deleted outside the tool. Returns the number removed.</summary>
        internal int RemoveMissing()
        {
            _lookup = null;
            return _entries.RemoveAll(e => !e.IsAlive);
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        // Undo / prefab reverts replace the entry objects.
        void ISerializationCallbackReceiver.OnAfterDeserialize() => _lookup = null;
    }
}
