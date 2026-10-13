using UnityEngine;

namespace AutoCollider.Samples
{
    /// <summary>Shows the latest hits reported by <see cref="HitReporter2D"/> in the top-left corner of the Game view.</summary>
    public sealed class DemoHud2D : MonoBehaviour
    {
        private GUIStyle _style;

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUILayout.BeginArea(new Rect(12, 12, 520, 260));
            GUILayout.Label("Auto Collider 2D — part hits (Collider Group events)", _style);
            foreach (var line in HitReporter2D.Recent) GUILayout.Label(line, _style);
            GUILayout.EndArea();
        }
    }
}
