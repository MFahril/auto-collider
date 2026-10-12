using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Keeps a <see cref="MeshCollider"/> in sync with an animated <see cref="SkinnedMeshRenderer"/> at runtime
    /// (bones and blend shapes). Refreshing re-cooks the collider, so it is throttled (<see cref="UpdateMode"/>) and can be
    /// limited to frames where the renderer is visible. For most characters, per-bone primitive colliders are far cheaper —
    /// use this when the exact animated surface matters (cloth-like props, morphing objects).
    /// </summary>
    [AddComponentMenu("Physics/Auto Collider/Animated Mesh Collider")]
    [DisallowMultipleComponent]
    public sealed class AnimatedMeshCollider : MonoBehaviour
    {
        [Tooltip("Animated renderer whose mesh the collider follows.")]
        [SerializeField] private SkinnedMeshRenderer _source;

        [Tooltip("Collider that receives the mesh. Best on the renderer's own object (no extra transform per refresh).")]
        [SerializeField] private MeshCollider _target;

        [Tooltip("Bake Mesh: current animated pose (accurate). Shared Mesh: bind pose, refreshed only when the mesh changes (cheap).")]
        [SerializeField] private AnimatedMeshSource _mode = AnimatedMeshSource.BakeMesh;

        [Tooltip("How often the pose is re-baked. Each refresh re-cooks the collider.")]
        [SerializeField] private ColliderUpdateMode _updateMode = ColliderUpdateMode.TargetHz;

        [Tooltip("Every N Frames: refresh once every this many frames.")]
        [Min(1)]
        [SerializeField] private int _everyNFrames = 3;

        [Tooltip("Target Hz: refreshes per second.")]
        [Min(0.1f)]
        [SerializeField] private float _targetHz = 15f;

        [Tooltip("Skip refreshes while no camera renders the source renderer.")]
        [SerializeField] private bool _onlyWhenVisible = true;

        private Mesh _baked;
        private Mesh _originalMesh;
        private bool _hasOriginal;
        private Mesh _lastShared;
        private int _lastFrame = -1;
        private float _lastTime;
        private readonly List<Vector3> _vertices = new();

        /// <summary>Animated renderer whose mesh the collider follows.</summary>
        public SkinnedMeshRenderer Source
        {
            get => _source;
            set => _source = value;
        }

        /// <summary>Collider that receives the mesh.</summary>
        public MeshCollider Target
        {
            get => _target;
            set => _target = value;
        }

        /// <summary>Where the mesh comes from.</summary>
        public AnimatedMeshSource Mode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>Refresh frequency rule.</summary>
        public ColliderUpdateMode UpdateMode
        {
            get => _updateMode;
            set => _updateMode = value;
        }

        /// <summary>Frames between refreshes for <see cref="ColliderUpdateMode.EveryNFrames"/>.</summary>
        public int EveryNFrames
        {
            get => _everyNFrames;
            set => _everyNFrames = Mathf.Max(1, value);
        }

        /// <summary>Refreshes per second for <see cref="ColliderUpdateMode.TargetHz"/>.</summary>
        public float TargetHz
        {
            get => _targetHz;
            set => _targetHz = Mathf.Max(0.1f, value);
        }

        /// <summary>Skip refreshes while the source renderer is not visible to any camera.</summary>
        public bool OnlyWhenVisible
        {
            get => _onlyWhenVisible;
            set => _onlyWhenVisible = value;
        }

        /// <summary>Number of times the collider mesh was replaced since this component was enabled (diagnostics / profiling).</summary>
        public int RefreshCount { get; private set; }

        private void Reset()
        {
            _source = GetComponent<SkinnedMeshRenderer>();
            _target = GetComponent<MeshCollider>();
        }

        private void OnEnable()
        {
            _lastFrame = -1;
            RefreshCount = 0;
        }

        private void LateUpdate()
        {
            if (_source == null || _target == null) return;

            if (_mode == AnimatedMeshSource.SharedMesh)
            {
                if (_source.sharedMesh != _lastShared) Refresh();
                return;
            }

            if (_onlyWhenVisible && !_source.isVisible) return;
            if (!RefreshSchedule.IsDue(_updateMode, _everyNFrames, _targetHz, Time.frameCount, Time.time, _lastFrame, _lastTime)) return;
            Refresh();
        }

        /// <summary>Update the collider now, ignoring the schedule.</summary>
        public void Refresh()
        {
            if (_source == null || _target == null) return;
            _lastFrame = Time.frameCount;
            _lastTime = Time.time;
            if (!_hasOriginal)
            {
                // Put back on destroy, so the collider never keeps a destroyed baked mesh.
                _originalMesh = _target.sharedMesh;
                _hasOriginal = true;
            }

            Mesh mesh;
            if (_mode == AnimatedMeshSource.SharedMesh)
            {
                mesh = _lastShared = _source.sharedMesh;
            }
            else
            {
                if (_baked == null) _baked = new Mesh { name = _source.name + " (Animated Collider)", hideFlags = HideFlags.DontSave };
                // useScale: the baked vertices then map through the renderer's full transform, like the original mesh.
                _source.BakeMesh(_baked, true);
                if (_target.transform != _source.transform) ToTargetSpace(_baked);
                mesh = _baked;
            }

            // Re-assigning the same mesh instance does not re-cook — clear first.
            _target.sharedMesh = null;
            _target.sharedMesh = mesh;
            RefreshCount++;
        }

        private void ToTargetSpace(Mesh mesh)
        {
            var toTarget = _target.transform.worldToLocalMatrix * _source.transform.localToWorldMatrix;
            mesh.GetVertices(_vertices);
            for (var i = 0; i < _vertices.Count; i++) _vertices[i] = toTarget.MultiplyPoint3x4(_vertices[i]);
            mesh.SetVertices(_vertices);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_target != null && _baked != null && _target.sharedMesh == _baked) _target.sharedMesh = _originalMesh;
            if (_baked == null) return;
            if (Application.isPlaying) Destroy(_baked);
            else DestroyImmediate(_baked);
        }
    }
}
