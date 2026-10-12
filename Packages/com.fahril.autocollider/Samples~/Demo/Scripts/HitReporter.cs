using System.Collections.Generic;
using AutoCollider;
using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>
    /// Listens to a Collider Group and reports which part was hit — one subscription for all of the object's colliders.
    /// Generate colliders for the object first (select it ▸ Shift+Alt+C), then press Play.
    /// </summary>
    public sealed class HitReporter : MonoBehaviour
    {
        private const int MaxLines = 8;

        /// <summary>Most recent hits, newest first (shown by <see cref="DemoHud"/>).</summary>
        public static readonly List<string> Recent = new();

        private ColliderGroup _group;

        private void OnEnable()
        {
            if (!TryGetComponent(out _group))
            {
                Debug.Log($"{name}: no Collider Group yet — select it and press Shift+Alt+C to generate colliders.", this);
                return;
            }
            _group.CollisionEnter += OnCollision;
            _group.TriggerEnter += OnTrigger;
        }

        private void OnDisable()
        {
            if (_group == null) return;
            _group.CollisionEnter -= OnCollision;
            _group.TriggerEnter -= OnTrigger;
        }

        private void OnCollision(ColliderEntry entry, Collision collision) =>
            Report($"{collision.collider.name} hit {name} ▸ {entry.Label}", entry.Collider);

        private void OnTrigger(ColliderEntry entry, Collider other) =>
            Report($"{other.name} entered {name} ▸ {entry.Label}", entry.Collider);

        private static void Report(string message, Object context)
        {
            Recent.Insert(0, message);
            if (Recent.Count > MaxLines) Recent.RemoveAt(Recent.Count - 1);
            Debug.Log(message, context);
        }
    }
}
