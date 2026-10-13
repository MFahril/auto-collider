using System.Collections.Generic;
using AutoCollider;
using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>
    /// Listens to a Collider Group's 2D events and reports which part was hit — one subscription for all of the object's
    /// 2D colliders. Generate colliders for the object first (select it ▸ Shift+Alt+C), then press Play.
    /// </summary>
    public sealed class HitReporter2D : MonoBehaviour
    {
        private const int MaxLines = 8;

        /// <summary>Most recent hits, newest first (shown by <see cref="DemoHud2D"/>).</summary>
        public static readonly List<string> Recent = new();

        private ColliderGroup _group;

        private void OnEnable()
        {
            if (!TryGetComponent(out _group))
            {
                Debug.Log($"{name}: no Collider Group yet — select it and press Shift+Alt+C to generate colliders.", this);
                return;
            }
            _group.CollisionEnter2D += OnCollision;
            _group.TriggerEnter2D += OnTrigger;
        }

        private void OnDisable()
        {
            if (_group == null) return;
            _group.CollisionEnter2D -= OnCollision;
            _group.TriggerEnter2D -= OnTrigger;
        }

        private void OnCollision(ColliderEntry entry, Collision2D collision) =>
            Report($"{collision.collider.name} hit {name} ▸ {entry.Label}", entry.Collider2D);

        private void OnTrigger(ColliderEntry entry, Collider2D other) =>
            Report($"{other.name} entered {name} ▸ {entry.Label}", entry.Collider2D);

        private static void Report(string message, Object context)
        {
            Recent.Insert(0, message);
            if (Recent.Count > MaxLines) Recent.RemoveAt(Recent.Count - 1);
            Debug.Log(message, context);
        }
    }
}
