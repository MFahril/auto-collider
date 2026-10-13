using System.Collections.Generic;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>World-space paint meshes per renderer, rebuilt when the renderer moves; cleared on undo / hierarchy changes.</summary>
    internal static class PaintMeshCache
    {
        private static readonly Dictionary<Renderer, PaintMesh> Cache = new();

        /// <summary>Paintable meshes of the groups (their recipe's renderer filters).</summary>
        public static List<PaintMesh> For(IEnumerable<ColliderGroup> groups)
        {
            var result = new List<PaintMesh>();
            foreach (var group in groups)
            {
                if (group == null) continue;
                foreach (var renderer in MeshSource.CollectRenderers(group.gameObject, group.Recipe.Settings, null, includeSprites: true))
                {
                    var mesh = Get(renderer, group);
                    if (mesh != null) result.Add(mesh);
                }
            }
            return result;
        }

        public static PaintMesh Get(Renderer renderer, ColliderGroup group)
        {
            if (renderer == null || group == null) return null;
            if (Cache.TryGetValue(renderer, out var mesh) && mesh.BuiltWith == renderer.localToWorldMatrix) return mesh;
            mesh = PaintMesh.Build(renderer, group);
            Cache[renderer] = mesh;
            return mesh;
        }

        public static void Clear() => Cache.Clear();
    }
}
