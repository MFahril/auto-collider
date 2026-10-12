using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Shows the latest <see cref="HitReporter"/> messages in the Game view.</summary>
    public sealed class DemoHud : MonoBehaviour
    {
        private GUIStyle _style;

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            GUI.Label(new Rect(12, 8, 900, 24), "Auto Collider demo — hits reported by Collider Group events:", _style);
            for (var i = 0; i < HitReporter.Recent.Count; i++)
                GUI.Label(new Rect(12, 32 + i * 20, 900, 22), HitReporter.Recent[i], _style);
        }
    }
}
