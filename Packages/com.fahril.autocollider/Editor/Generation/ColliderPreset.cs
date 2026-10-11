using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Saved generation settings, shareable with the team as an asset
    /// (<c>Assets ▸ Create ▸ Auto Collider ▸ Collider Preset</c>, or "Save Current as Preset…" in the window).
    /// </summary>
    [CreateAssetMenu(fileName = "Collider Preset", menuName = "Auto Collider/Collider Preset", order = 400)]
    internal sealed class ColliderPreset : ScriptableObject
    {
        [Tooltip("Shown in the preset menu.")]
        [SerializeField] private string _description = string.Empty;

        [SerializeField] private GenerationSettings _settings = new();

        public string Description => _description;

        public GenerationSettings Settings => _settings ??= new GenerationSettings();

        public void Capture(GenerationSettings settings) => _settings = settings.Clone();
    }
}
