using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Forwards Stay messages to the sibling <see cref="ColliderRelay"/>. Separate because Unity calls Stay every physics
    /// step for every contact — it is only added when a group's <c>CollisionStay</c> / <c>TriggerStay</c> has a subscriber.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ColliderRelay))]
    public sealed class ColliderStayRelay : MonoBehaviour
    {
        private ColliderRelay _relay;

        private void Awake() => _relay = GetComponent<ColliderRelay>();

        private void OnCollisionStay(Collision collision)
        {
            if (_relay != null) _relay.Collision(collision, RelayPhase.Stay);
        }

        private void OnTriggerStay(Collider other)
        {
            if (_relay != null) _relay.Trigger(other, RelayPhase.Stay);
        }
    }
}
