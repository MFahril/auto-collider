using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Main object of a generated-mesh asset. Collider meshes are stored as its sub-assets, indexed by content hash
    /// so identical meshes (e.g. many prefab instances) are saved once. Optimized meshes remember their source mesh,
    /// so re-optimizing always starts from the original and "Restore Original" is possible.
    /// </summary>
    internal sealed class GeneratedMeshLibrary : ScriptableObject
    {
        [Serializable]
        private struct Item
        {
            public string Hash;
            public Mesh Mesh;
            public Mesh Source;
        }

        [SerializeField] private List<Item> _items = new();

        public int Count => _items.Count;

        public bool TryGet(string hash, out Mesh mesh)
        {
            foreach (var item in _items)
            {
                if (item.Hash == hash && item.Mesh != null)
                {
                    mesh = item.Mesh;
                    return true;
                }
            }
            mesh = null;
            return false;
        }

        /// <summary>The original mesh an optimized mesh was made from (null for meshes generated from renderers).</summary>
        public bool TryGetSource(Mesh mesh, out Mesh source)
        {
            foreach (var item in _items)
            {
                if (item.Mesh == mesh && item.Source != null)
                {
                    source = item.Source;
                    return true;
                }
            }
            source = null;
            return false;
        }

        public void Add(string hash, Mesh mesh, Mesh source = null)
        {
            _items.RemoveAll(i => i.Hash == hash || i.Mesh == null);
            _items.Add(new Item { Hash = hash, Mesh = mesh, Source = source });
        }
    }
}
