using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Drops a ball above a random target every few seconds so the generated colliders get something to stop.</summary>
    public sealed class BallDropper : MonoBehaviour
    {
        [Tooltip("Objects to drop balls on.")]
        [SerializeField] private Transform[] _targets;

        [Tooltip("Seconds between balls.")]
        [Min(0.1f)]
        [SerializeField] private float _interval = 1.5f;

        [Tooltip("Height above the target where balls start.")]
        [SerializeField] private float _height = 4f;

        [Tooltip("Material for the balls (optional).")]
        [SerializeField] private Material _material;

        private float _next;
        private int _count;

        private void Update()
        {
            if (_targets == null || _targets.Length == 0 || Time.time < _next) return;
            _next = Time.time + _interval;

            var target = _targets[_count % _targets.Length];
            if (target == null) return;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = $"Ball {++_count}";
            ball.transform.localScale = Vector3.one * 0.3f;
            ball.transform.position = target.position + Vector3.up * _height + Random.insideUnitSphere * 0.4f;
            if (_material != null) ball.GetComponent<Renderer>().sharedMaterial = _material;
            ball.AddComponent<Rigidbody>();
            Destroy(ball, 8f);
        }
    }
}
