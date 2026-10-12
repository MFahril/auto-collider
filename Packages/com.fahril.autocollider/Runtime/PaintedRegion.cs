using System;
using UnityEngine;

namespace AutoCollider
{
    /// <summary>
    /// Triangles painted on one renderer's mesh that become one collider (or several, for <see cref="PartShape.ConvexParts"/>).
    /// Stored in the <see cref="GenerationRecipe"/>, so Regenerate rebuilds exactly the same colliders.
    /// </summary>
    [Serializable]
    public sealed class PaintedRegion
    {
        [SerializeField] private int _id;
        [SerializeField] private string _label;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private int[] _triangles = Array.Empty<int>();
        [SerializeField] private PartShape _shape = PartShape.Auto;

        public PaintedRegion(int id, string label, Renderer renderer, int[] triangles, PartShape shape)
        {
            _id = id;
            _label = label;
            _renderer = renderer;
            _triangles = triangles ?? Array.Empty<int>();
            _shape = shape;
        }

        /// <summary>Stable id; colliders built from this region use it as their entry's <see cref="ColliderEntry.PartId"/>.</summary>
        public int Id => _id;

        public string Label
        {
            get => _label;
            set => _label = value;
        }

        /// <summary>Renderer whose mesh the triangles index into.</summary>
        public Renderer Renderer => _renderer;

        /// <summary>Triangle numbers (0-based, in submesh order) of the renderer's mesh.</summary>
        public int[] Triangles => _triangles;

        public PartShape Shape
        {
            get => _shape;
            set => _shape = value;
        }
    }
}
