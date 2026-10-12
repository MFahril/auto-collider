using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Swings a bone back and forth — per-bone colliders follow it without any runtime cost.</summary>
    public sealed class DemoWave : MonoBehaviour
    {
        [Tooltip("Swing amplitude in degrees.")]
        [SerializeField] private float _angle = 50f;

        [Tooltip("Swings per second.")]
        [SerializeField] private float _speed = 0.5f;

        private Quaternion _rest;

        private void Awake() => _rest = transform.localRotation;

        private void Update() =>
            transform.localRotation = _rest * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * _speed * 2f * Mathf.PI) * _angle);
    }
}
