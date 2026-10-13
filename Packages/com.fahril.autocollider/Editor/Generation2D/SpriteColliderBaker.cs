using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Bakes a <see cref="SpriteColliderSwitcher"/>'s per-sprite polygons: finds every sprite the renderer shows (Animator
    /// clips, current sprite, extra sprites), traces each with the 2D outline engine and stores the simplified paths.
    /// </summary>
    internal static class SpriteColliderBaker
    {
        private const string SpriteProperty = "m_Sprite";

        /// <summary>Sprites the switcher's renderer can show, in a stable order (current sprite first, then clips, then extras).</summary>
        public static List<Sprite> CollectSprites(SpriteColliderSwitcher switcher)
        {
            var sprites = new List<Sprite>();
            var seen = new HashSet<Sprite>();
            void Add(Sprite s)
            {
                if (s != null && seen.Add(s)) sprites.Add(s);
            }

            if (switcher.TryGetComponent<SpriteRenderer>(out var renderer)) Add(renderer.sprite);
            foreach (var s in AnimatedSprites(switcher.transform)) Add(s);
            foreach (var s in switcher.ExtraSprites) Add(s);
            return sprites;
        }

        /// <summary>Sprites keyed on <paramref name="target"/>'s SpriteRenderer in the clips of the Animator above it.</summary>
        public static IEnumerable<Sprite> AnimatedSprites(Transform target)
        {
            var animator = target.GetComponentInParent<Animator>(true);
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) yield break;

            var path = AnimationUtility.CalculateTransformPath(target, animator.transform);
            foreach (var clip in controller.animationClips)
            {
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.type != typeof(SpriteRenderer) || binding.propertyName != SpriteProperty || binding.path != path) continue;
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                        if (key.value is Sprite sprite)
                            yield return sprite;
                }
            }
        }

        /// <summary>Simplified collider paths for <paramref name="sprite"/> in sprite units, or null when it has no outline.</summary>
        public static List<Vector2[]> Trace(Sprite sprite, GenerationSettings settings)
        {
            var outline = new Outline2D(sprite.name, SpriteSource.Loops(sprite, settings, null));
            if (outline.IsEmpty) return null;
            var shape = Strategies2D.Polygon(outline, settings);
            if (shape == null) return null;

            var paths = new List<Vector2[]>(shape.Paths.Length);
            foreach (var path in shape.Paths)
            {
                var points = new Vector2[path.Length];
                for (var i = 0; i < path.Length; i++) points[i] = (Vector2)path[i];
                paths.Add(points);
            }
            return paths;
        }

        /// <summary>
        /// Re-bake every sprite and apply the current one, as one Undo step. Adds a Polygon Collider 2D to the switcher's object
        /// when none is assigned. Returns the number of sprites baked.
        /// </summary>
        public static int Bake(SpriteColliderSwitcher switcher, GenerationSettings settings)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Strings.UndoBakeSprites);

            var collider = switcher.Collider;
            if (collider == null && !switcher.TryGetComponent(out collider)) collider = Undo.AddComponent<PolygonCollider2D>(switcher.gameObject);

            var baked = 0;
            var traced = new List<(Sprite, List<Vector2[]>)>();
            foreach (var sprite in CollectSprites(switcher))
            {
                var paths = Trace(sprite, settings);
                if (paths != null) traced.Add((sprite, paths));
            }

            Undo.RecordObject(switcher, Strings.UndoBakeSprites);
            switcher.Collider = collider;
            switcher.ClearShapes();
            foreach (var (sprite, paths) in traced)
            {
                switcher.SetShape(sprite, paths);
                baked++;
            }
            RecordPrefab(switcher);

            if (collider != null)
            {
                Undo.RecordObject(collider, Strings.UndoBakeSprites);
                collider.offset = Vector2.zero;
                switcher.Apply();
                RecordPrefab(collider);
            }
            Undo.CollapseUndoOperations(undoGroup);
            return baked;
        }

        /// <summary>Add (and bake) a switcher on every selected object with a Sprite Renderer, as one Undo step.</summary>
        public static int AddTo(IEnumerable<GameObject> objects, GenerationSettings settings)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Strings.UndoAddSpriteSwitcher);

            var added = 0;
            foreach (var go in objects)
            {
                if (go == null || EditorUtility.IsPersistent(go) || !go.TryGetComponent<SpriteRenderer>(out _)) continue;
                if (go.TryGetComponent<SpriteColliderSwitcher>(out _)) continue;
                var switcher = Undo.AddComponent<SpriteColliderSwitcher>(go);
                Bake(switcher, settings);
                added++;
            }
            Undo.CollapseUndoOperations(undoGroup);
            return added;
        }

        /// <summary>Sprites the renderer can show that have no baked shape.</summary>
        public static int CountUnbaked(SpriteColliderSwitcher switcher)
        {
            var count = 0;
            foreach (var sprite in CollectSprites(switcher))
                if (!switcher.HasShape(sprite))
                    count++;
            return count;
        }

        private static void RecordPrefab(Object o)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }
    }
}
