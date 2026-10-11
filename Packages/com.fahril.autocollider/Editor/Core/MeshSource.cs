using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Collects renderer meshes under a target and converts them to <see cref="MeshGeometry"/> in the target's scaled space.
    /// Reads through <see cref="Mesh.AcquireReadOnlyMeshData(Mesh)"/>, so Read/Write-disabled meshes work in the editor.
    /// </summary>
    internal static class MeshSource
    {
        /// <summary>Renderers that contribute meshes to <paramref name="target"/>, after all settings filters.</summary>
        public static List<Renderer> CollectRenderers(GameObject target, GenerationSettings settings, ICollection<string> warnings)
        {
            var renderers = settings.IncludeChildren
                ? target.GetComponentsInChildren<Renderer>(true)
                : target.GetComponents<Renderer>();

            var group = target.GetComponent<ColliderGroup>();
            var container = group != null ? group.Container : null;
            var excludedByLod = settings.Lod0Only ? HigherLodRenderers(target) : null;
            var namePattern = BuildPattern(settings.ExcludeNamePattern, warnings);

            var result = new List<Renderer>();
            foreach (var renderer in renderers)
            {
                if (SharedMesh(renderer) == null) continue;
                if (!settings.IncludeInactive && (!renderer.enabled || !renderer.gameObject.activeInHierarchy)) continue;
                if (container != null && renderer.transform.IsChildOf(container)) continue;
                if (IsInsideNestedGroup(renderer.transform, target.transform)) continue;
                if (excludedByLod != null && excludedByLod.Contains(renderer)) continue;
                if (namePattern != null && namePattern.IsMatch(renderer.gameObject.name)) continue;
                if (settings.MinSize > 0f && MaxComponent(renderer.bounds.size) < settings.MinSize) continue;
                result.Add(renderer);
            }
            return result;
        }

        /// <summary>Read a renderer's mesh into the target's scaled space. Skinned meshes are baked in their current pose.</summary>
        public static MeshGeometry Read(Renderer renderer, in TargetSpace space)
        {
            var mesh = SharedMesh(renderer);
            Mesh baked = null;
            float4x4 meshToWorld;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(baked, true);
                mesh = baked;
                // useScale = true: vertices already include the renderer scale, so only position + rotation remain.
                var t = skinned.transform;
                meshToWorld = float4x4.TRS(t.position, t.rotation, new float3(1));
            }
            else
            {
                meshToWorld = renderer.transform.localToWorldMatrix;
            }

            try
            {
                var toScaled = math.mul(space.WorldToScaled, meshToWorld);
                ReadMesh(mesh, out var vertices, out var triangles);
                var result = new float3[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                    result[i] = math.transform(toScaled, vertices[i]);
                return new MeshGeometry(renderer.gameObject.name, result, triangles);
            }
            finally
            {
                if (baked != null) Object.DestroyImmediate(baked);
            }
        }

        /// <summary>Vertices and triangle-topology indices of all submeshes.</summary>
        public static void ReadMesh(Mesh mesh, out float3[] vertices, out int[] triangles)
        {
            using var dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var data = dataArray[0];

            using (var verts = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            {
                data.GetVertices(verts);
                vertices = new float3[verts.Length];
                for (var i = 0; i < verts.Length; i++) vertices[i] = verts[i];
            }

            var indices = new List<int>();
            for (var sub = 0; sub < data.subMeshCount; sub++)
            {
                var desc = data.GetSubMesh(sub);
                if (desc.topology != MeshTopology.Triangles || desc.indexCount == 0) continue;
                using var subIndices = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                data.GetIndices(subIndices, sub);
                indices.AddRange(subIndices);
            }
            triangles = indices.ToArray();
        }

        public static Mesh SharedMesh(Renderer renderer)
        {
            return renderer switch
            {
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                MeshRenderer => renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null,
                _ => null,
            };
        }

        private static bool IsInsideNestedGroup(Transform child, Transform target)
        {
            for (var t = child; t != null && t != target; t = t.parent)
                if (t.TryGetComponent<ColliderGroup>(out _)) return true;
            return false;
        }

        private static HashSet<Renderer> HigherLodRenderers(GameObject target)
        {
            var excluded = new HashSet<Renderer>();
            foreach (var lodGroup in target.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length == 0) continue;
                var lod0 = new HashSet<Renderer>(lods[0].renderers);
                for (var i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null && !lod0.Contains(r)) excluded.Add(r);
            }
            return excluded;
        }

        private static Regex BuildPattern(string pattern, ICollection<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            try
            {
                return new Regex(pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                warnings?.Add(string.Format(Strings.WarningInvalidPattern, pattern));
                return null;
            }
        }

        private static float MaxComponent(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }
}
