using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Saves generated collider meshes as sub-assets (one <c>&lt;Name&gt;_Colliders.asset</c> per target name),
    /// deduplicated by content hash. Never leaves meshes serialized inline in scenes.
    /// </summary>
    internal sealed class GeneratedMeshStore
    {
        private readonly string _folder;
        private readonly Dictionary<string, GeneratedMeshLibrary> _libraries = new();
        private readonly HashSet<GeneratedMeshLibrary> _dirty = new();

        public GeneratedMeshStore(string folder)
        {
            _folder = folder.TrimEnd('/');
        }

        /// <summary>Find a stored mesh with identical content, or create and store a new one.</summary>
        public Mesh GetOrCreate(string libraryName, string meshName, Vector3[] vertices, int[] triangles)
        {
            var hash = ContentHash(vertices, triangles);
            var library = GetLibrary(libraryName);
            if (library.TryGet(hash, out var existing)) return existing;

            var mesh = new Mesh { name = $"{meshName}_{hash.Substring(0, 8)}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            AssetDatabase.AddObjectToAsset(mesh, library);
            library.Add(hash, mesh);
            EditorUtility.SetDirty(library);
            _dirty.Add(library);
            return mesh;
        }

        /// <summary>Write pending changes to disk. Call once at the end of a batch.</summary>
        public void Flush()
        {
            foreach (var library in _dirty)
                if (library != null) AssetDatabase.SaveAssetIfDirty(library);
            _dirty.Clear();
        }

        private GeneratedMeshLibrary GetLibrary(string libraryName)
        {
            var path = $"{_folder}/{SanitizeFileName(libraryName)}_Colliders.asset";
            if (_libraries.TryGetValue(path, out var cached) && cached != null) return cached;

            var library = AssetDatabase.LoadAssetAtPath<GeneratedMeshLibrary>(path);
            if (library == null)
            {
                EnsureFolder(_folder);
                library = ScriptableObject.CreateInstance<GeneratedMeshLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }
            _libraries[path] = library;
            return library;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static string SanitizeFileName(string name)
        {
            var builder = new StringBuilder(name.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in name.Trim())
                builder.Append(System.Array.IndexOf(invalid, c) >= 0 || c == '[' || c == ']' ? '_' : c);
            return builder.Length == 0 ? "Unnamed" : builder.ToString();
        }

        /// <summary>FNV-1a 64-bit over vertex bits and indices, as hex.</summary>
        public static string ContentHash(Vector3[] vertices, int[] triangles)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;

            void Mix(uint value)
            {
                for (var shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (value >> shift) & 0xFF;
                    hash *= prime;
                }
            }

            foreach (var v in vertices)
            {
                Mix(math.asuint(v.x));
                Mix(math.asuint(v.y));
                Mix(math.asuint(v.z));
            }
            Mix(0xFFFFFFFFu);
            foreach (var t in triangles) Mix((uint)t);
            return hash.ToString("x16");
        }
    }
}
