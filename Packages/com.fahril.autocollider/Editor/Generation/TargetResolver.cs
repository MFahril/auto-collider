using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Turns the editor selection into generation targets.</summary>
    internal static class TargetResolver
    {
        /// <summary>Scene objects from the current selection (see <see cref="Resolve"/>).</summary>
        public static List<GameObject> FromSelection(bool includeChildren)
        {
            return Resolve(Selection.gameObjects, includeChildren);
        }

        /// <summary>
        /// Scene objects only (prefab/model assets are skipped), excluding Auto Collider's own container objects.
        /// With <paramref name="includeChildren"/>, objects nested under another target are dropped to avoid double work.
        /// Order follows the hierarchy so results are stable.
        /// </summary>
        public static List<GameObject> Resolve(IEnumerable<GameObject> candidates, bool includeChildren)
        {
            var set = new HashSet<GameObject>();
            foreach (var go in candidates)
                if (go != null && !EditorUtility.IsPersistent(go) && !IsGeneratedObject(go))
                    set.Add(go);

            var result = new List<GameObject>();
            foreach (var go in set)
            {
                if (includeChildren && HasAncestorIn(go.transform, set)) continue;
                result.Add(go);
            }

            result.Sort(CompareHierarchyOrder);
            return result;
        }

        /// <summary>True for a group's container or anything under it.</summary>
        public static bool IsGeneratedObject(GameObject go)
        {
            foreach (var group in go.GetComponentsInParent<ColliderGroup>(true))
                if (group.IsInContainer(go.transform))
                    return true;
            return false;
        }

        private static bool HasAncestorIn(Transform t, HashSet<GameObject> set)
        {
            for (var parent = t.parent; parent != null; parent = parent.parent)
                if (set.Contains(parent.gameObject)) return true;
            return false;
        }

        private static int CompareHierarchyOrder(GameObject a, GameObject b)
        {
            var pathA = SiblingPath(a.transform);
            var pathB = SiblingPath(b.transform);
            for (var i = 0; i < Mathf.Min(pathA.Count, pathB.Count); i++)
                if (pathA[i] != pathB[i]) return pathA[i].CompareTo(pathB[i]);
            return pathA.Count.CompareTo(pathB.Count);
        }

        private static List<int> SiblingPath(Transform t)
        {
            var path = new List<int>();
            for (; t != null; t = t.parent) path.Insert(0, t.GetSiblingIndex());
            return path;
        }
    }
}
