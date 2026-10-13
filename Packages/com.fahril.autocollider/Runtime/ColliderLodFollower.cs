using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Enables only the colliders fitted for the LOD level that is currently shown (entries with
    /// <see cref="ColliderEntry.Lod"/> ≥ 0; entries for every LOD stay untouched). Distant objects then use their cheaper
    /// low-LOD colliders. Added automatically when colliders are generated per LOD level.
    /// </summary>
    [AddComponentMenu("Physics/Auto Collider/Collider LOD Follower")]
    [RequireComponent(typeof(ColliderGroup))]
    [DisallowMultipleComponent]
    public sealed class ColliderLodFollower : MonoBehaviour
    {
        [Tooltip("LOD Group whose active level the colliders follow.")]
        [SerializeField] private LODGroup _lodGroup;

        [Tooltip("Camera that decides the LOD level. Empty = Camera.main.")]
        [SerializeField] private Camera _camera;

        private ColliderGroup _group;
        private int _requested = int.MinValue;

        /// <summary>LOD Group whose active level the colliders follow.</summary>
        public LODGroup LodGroup
        {
            get => _lodGroup;
            set => _lodGroup = value;
        }

        /// <summary>Camera that decides the LOD level (null = <see cref="Camera.main"/>).</summary>
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        /// <summary>LOD level whose colliders are enabled, or -1 before the first update.</summary>
        public int ActiveLevel { get; private set; } = -1;

        private void Reset() => _lodGroup = GetComponentInChildren<LODGroup>();

        private void OnEnable()
        {
            _group = GetComponent<ColliderGroup>();
            _requested = int.MinValue;
            ActiveLevel = -1;
        }

        // Generation shows LOD 0's colliders; leave the object that way when following stops.
        private void OnDisable() => Apply(0);

        private void LateUpdate()
        {
            if (_lodGroup == null) return;
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;
            Apply(LodMath.ActiveLod(_lodGroup, cam));
        }

        /// <summary>
        /// Enable the colliders for <paramref name="lod"/>. A level without colliders of its own uses the closest more detailed
        /// level that has some (culled → the lowest-detail colliders, since physics still runs off screen).
        /// </summary>
        public void Apply(int lod)
        {
            if (_group == null) _group = GetComponent<ColliderGroup>();
            if (_group == null || lod == _requested) return;
            _requested = lod;

            var level = ResolveLevel(lod);
            if (level < 0 || level == ActiveLevel) return;
            ActiveLevel = level;
            foreach (var entry in _group.Entries)
                if (entry.Lod >= 0 && entry.IsAlive)
                    entry.Enabled = entry.Lod == level;
        }

        private int ResolveLevel(int lod)
        {
            int below = -1, above = int.MaxValue;
            foreach (var entry in _group.Entries)
            {
                var l = entry.Lod;
                if (l < 0 || !entry.IsAlive) continue;
                if (l <= lod && l > below) below = l;
                if (l > lod && l < above) above = l;
            }
            if (below >= 0) return below;
            return above != int.MaxValue ? above : -1;
        }
    }
}
