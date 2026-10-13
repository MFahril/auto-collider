using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Walks back and forth and faces the walking direction (flip X) — the Animator plays the run frames.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Walker2D : MonoBehaviour
    {
        [Tooltip("Walking distance to each side of the start position.")]
        [Min(0f)]
        [SerializeField] private float _range = 2f;

        [Tooltip("Walking speed in units per second.")]
        [Min(0f)]
        [SerializeField] private float _speed = 1f;

        private SpriteRenderer _renderer;
        private Vector3 _start;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _start = transform.position;
        }

        private void Update()
        {
            if (_range <= 0f || _speed <= 0f) return;
            var travelled = Time.time * _speed;
            transform.position = _start + Vector3.right * (Mathf.PingPong(travelled, 2f * _range) - _range);
            _renderer.flipX = Mathf.Repeat(travelled, 4f * _range) > 2f * _range;
        }
    }
}
