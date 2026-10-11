using System;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Everything needed to regenerate a <see cref="ColliderGroup"/> non-destructively.
    /// Painted regions are added in a later milestone.
    /// </summary>
    [Serializable]
    public sealed class GenerationRecipe
    {
        [SerializeField] private GenerationSettings _settings = new();

        public GenerationSettings Settings
        {
            get => _settings ??= new GenerationSettings();
            set => _settings = value ?? new GenerationSettings();
        }
    }
}
