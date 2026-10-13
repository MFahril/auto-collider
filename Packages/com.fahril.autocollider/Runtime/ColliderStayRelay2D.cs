using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Forwards 2D Stay messages to the sibling <see cref="ColliderRelay2D"/>. Only added when a group's
    /// <c>CollisionStay2D</c> / <c>TriggerStay2D</c> has a subscriber (Unity calls Stay every physics step).
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ColliderRelay2D))]
    public sealed class ColliderStayRelay2D : MonoBehaviour
    {
        private ColliderRelay2D _relay;

        private void Awake() => _relay = GetComponent<ColliderRelay2D>();

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (_relay != null) _relay.Collision(collision, RelayPhase.Stay);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_relay != null) _relay.Trigger(other, RelayPhase.Stay);
        }
    }
}
