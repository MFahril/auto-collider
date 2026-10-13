using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Drops a ball sprite above a random target every few seconds so the generated 2D colliders get something to stop.</summary>
    public sealed class BallDropper2D : MonoBehaviour
    {
        [Tooltip("Objects to drop balls on.")]
        [SerializeField] private Transform[] _targets;

        [Tooltip("Sprite for the balls.")]
        [SerializeField] private Sprite _ballSprite;

        [Tooltip("Seconds between balls.")]
        [Min(0.1f)]
        [SerializeField] private float _interval = 1f;

        [Tooltip("Height above the target where balls start.")]
        [SerializeField] private float _height = 4f;

        [Tooltip("Ball size in units.")]
        [Min(0.05f)]
        [SerializeField] private float _size = 0.4f;

        private float _next;
        private int _count;

        private void Update()
        {
            if (_targets == null || _targets.Length == 0 || Time.time < _next) return;
            _next = Time.time + _interval;

            var target = _targets[_count % _targets.Length];
            if (target == null) return;
            var ball = new GameObject($"Ball {++_count}");
            ball.transform.position = target.position + Vector3.up * _height + (Vector3)(Random.insideUnitCircle * 0.4f);
            var renderer = ball.AddComponent<SpriteRenderer>();
            renderer.sprite = _ballSprite;
            renderer.sortingOrder = 1;
            var spriteSize = _ballSprite != null ? _ballSprite.bounds.size.x : 1f;
            ball.transform.localScale = Vector3.one * (_size / spriteSize);
            ball.AddComponent<CircleCollider2D>();
            ball.AddComponent<Rigidbody2D>();
            Destroy(ball, 8f);
        }
    }
}
