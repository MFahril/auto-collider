using System;
using System.Reflection;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Reads 2D Animation's <c>SpriteSkin</c> through reflection, so Auto Collider needs no dependency on that package
    /// (everything else about sprite skinning comes from core <c>UnityEngine.U2D</c> sprite APIs).
    /// </summary>
    internal static class SpriteSkinAccess
    {
        private const string TypeName = "UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime";

        private static bool _resolved;
        private static Type _type;
        private static PropertyInfo _boneTransforms;

        /// <summary>The SpriteSkin type, or null when the 2D Animation package is not installed.</summary>
        public static Type SkinType
        {
            get
            {
                if (_resolved) return _type;
                _resolved = true;
                _type = Type.GetType(TypeName, false);
                _boneTransforms = _type?.GetProperty("boneTransforms", BindingFlags.Public | BindingFlags.Instance);
                if (_boneTransforms == null) _type = null;
                return _type;
            }
        }

        public static bool IsAvailable => SkinType != null;

        /// <summary>The SpriteSkin on the renderer's object, or null.</summary>
        public static Component Find(Component renderer)
        {
            var type = SkinType;
            return type != null && renderer != null ? renderer.GetComponent(type) : null;
        }

        /// <summary>Bone transforms of the SpriteSkin on the renderer's object, or null when it has none.</summary>
        public static Transform[] Bones(Component renderer)
        {
            var skin = Find(renderer);
            return skin != null ? _boneTransforms.GetValue(skin) as Transform[] : null;
        }
    }
}
