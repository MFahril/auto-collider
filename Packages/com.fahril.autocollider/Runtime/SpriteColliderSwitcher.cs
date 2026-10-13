using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Matching colliders for frame-by-frame sprite animation: swaps a <see cref="PolygonCollider2D"/>'s paths whenever the
    /// <see cref="SpriteRenderer"/> shows another sprite. The per-sprite outlines are baked in the editor (from the Animator's
    /// clips), so the runtime cost is one path copy per sprite change — no tracing, no allocation.
    /// </summary>
    [AddComponentMenu("Physics 2D/Auto Collider/Sprite Collider Switcher")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteColliderSwitcher : MonoBehaviour
    {
        [Serializable]
        internal sealed class Path
        {
            public Vector2[] Points;
        }

        [Serializable]
        internal sealed class SpriteShape
        {
            public Sprite Sprite;
            public List<Path> Paths = new();
        }

        [Tooltip("Polygon collider whose paths follow the sprite. Best on this object (on another object the paths are converted each swap).")]
        [SerializeField] private PolygonCollider2D _collider;

        [Tooltip("Sprites to bake besides the ones found in the Animator's clips (e.g. sprites assigned from code).")]
        [SerializeField] private List<Sprite> _extraSprites = new();

        [SerializeField] private List<SpriteShape> _shapes = new();

        private SpriteRenderer _renderer;
        private Dictionary<Sprite, SpriteShape> _lookup;
        private readonly List<Vector2> _buffer = new();
        private Sprite _applied;
        private bool _appliedFlipX, _appliedFlipY;

        /// <summary>Polygon collider whose paths follow the sprite.</summary>
        public PolygonCollider2D Collider
        {
            get => _collider;
            set => _collider = value;
        }

        /// <summary>Sprites to bake besides the ones found in the Animator's clips.</summary>
        public List<Sprite> ExtraSprites => _extraSprites ??= new List<Sprite>();

        /// <summary>Number of sprites with a baked shape.</summary>
        public int ShapeCount => _shapes.Count;

        /// <summary>Sprites with a baked shape, in bake order.</summary>
        public IEnumerable<Sprite> Sprites
        {
            get
            {
                foreach (var shape in _shapes) yield return shape.Sprite;
            }
        }

        /// <summary>Total number of baked path points (all sprites).</summary>
        public int PointCount
        {
            get
            {
                var count = 0;
                foreach (var shape in _shapes)
                foreach (var path in shape.Paths)
                    count += path.Points?.Length ?? 0;
                return count;
            }
        }

        /// <summary>True when <paramref name="sprite"/> has a baked shape.</summary>
        public bool HasShape(Sprite sprite) => sprite != null && Lookup().ContainsKey(sprite);

        /// <summary>The baked paths of <paramref name="sprite"/> (sprite units, unflipped), or null.</summary>
        public IReadOnlyList<Vector2[]> GetShape(Sprite sprite)
        {
            if (sprite == null || !Lookup().TryGetValue(sprite, out var shape)) return null;
            var paths = new Vector2[shape.Paths.Count][];
            for (var i = 0; i < paths.Length; i++) paths[i] = shape.Paths[i].Points;
            return paths;
        }

        /// <summary>Store the collider paths for <paramref name="sprite"/>, in sprite units (relative to its pivot, unflipped).</summary>
        public void SetShape(Sprite sprite, IReadOnlyList<Vector2[]> paths)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (!Lookup().TryGetValue(sprite, out var shape))
            {
                shape = new SpriteShape { Sprite = sprite };
                _shapes.Add(shape);
                _lookup[sprite] = shape;
            }
            shape.Paths.Clear();
            foreach (var points in paths) shape.Paths.Add(new Path { Points = (Vector2[])points.Clone() });
            if (sprite == _applied) _applied = null;
        }

        /// <summary>Forget every baked shape.</summary>
        public void ClearShapes()
        {
            _shapes.Clear();
            _lookup = null;
            _applied = null;
        }

        /// <summary>
        /// Copy the current sprite's shape into the collider now (normally done automatically when the sprite changes).
        /// False when the sprite has no baked shape — the collider then keeps its previous paths.
        /// </summary>
        public bool Apply()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            var sprite = _renderer.sprite;
            _applied = sprite;
            _appliedFlipX = _renderer.flipX;
            _appliedFlipY = _renderer.flipY;
            if (_collider == null || sprite == null || !Lookup().TryGetValue(sprite, out var shape)) return false;

            var sameObject = _collider.transform == transform;
            var toCollider = sameObject ? Matrix4x4.identity : _collider.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            var flip = new Vector2(_appliedFlipX ? -1f : 1f, _appliedFlipY ? -1f : 1f);

            _collider.pathCount = shape.Paths.Count;
            for (var p = 0; p < shape.Paths.Count; p++)
            {
                _buffer.Clear();
                var points = shape.Paths[p].Points;
                foreach (var point in points)
                {
                    var flipped = point * flip;
                    _buffer.Add(sameObject ? flipped : (Vector2)toCollider.MultiplyPoint3x4(flipped));
                }
                _collider.SetPath(p, _buffer);
            }
            return true;
        }

        private void Awake() => _renderer = GetComponent<SpriteRenderer>();

        private void OnEnable() => _applied = null;

        private void LateUpdate()
        {
            var sprite = _renderer.sprite;
            if (sprite == _applied && _renderer.flipX == _appliedFlipX && _renderer.flipY == _appliedFlipY) return;
            Apply();
        }

        private void OnValidate() => _lookup = null;

        private Dictionary<Sprite, SpriteShape> Lookup()
        {
            if (_lookup != null) return _lookup;
            _lookup = new Dictionary<Sprite, SpriteShape>(_shapes.Count);
            foreach (var shape in _shapes)
                if (shape.Sprite != null)
                    _lookup[shape.Sprite] = shape;
            return _lookup;
        }
    }
}
