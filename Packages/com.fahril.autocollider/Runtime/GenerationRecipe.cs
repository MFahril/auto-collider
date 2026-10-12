using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Everything needed to regenerate a <see cref="ColliderGroup"/> non-destructively:
    /// the generation settings and the painted regions.
    /// </summary>
    [Serializable]
    public sealed class GenerationRecipe
    {
        /// <summary>Region ids start here so they never collide with automatic part indices.</summary>
        public const int FirstRegionId = 10000;

        [SerializeField] private GenerationSettings _settings = new();
        [SerializeField] private List<PaintedRegion> _regions = new();
        [SerializeField] private int _nextRegionId = FirstRegionId;

        public GenerationSettings Settings
        {
            get => _settings ??= new GenerationSettings();
            set => _settings = value ?? new GenerationSettings();
        }

        /// <summary>Painted regions. When there are any, generation builds colliders from them instead of whole meshes.</summary>
        public IReadOnlyList<PaintedRegion> Regions => _regions ??= new List<PaintedRegion>();

        public bool HasRegions => Regions.Count > 0;

        public bool IsRegionId(int partId) => partId >= FirstRegionId;

        internal PaintedRegion AddRegion(string label, Renderer renderer, int[] triangles, PartShape shape)
        {
            var region = new PaintedRegion(_nextRegionId++, label, renderer, triangles, shape);
            _regions ??= new List<PaintedRegion>();
            _regions.Add(region);
            return region;
        }

        internal bool RemoveRegion(int id) => (_regions ??= new List<PaintedRegion>()).RemoveAll(r => r.Id == id) > 0;

        internal void ClearRegions() => _regions?.Clear();
    }
}
