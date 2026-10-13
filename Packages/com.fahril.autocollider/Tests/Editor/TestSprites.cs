using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoCollider.Editor.Tests
{
    /// <summary>Procedural sprites for 2D tests (in-memory textures; call <see cref="DestroyAll"/> in TearDown).</summary>
    internal sealed class TestSprites
    {
        public const float PixelsPerUnit = 64f;
        private readonly List<Object> _created = new();

        /// <summary><paramref name="width"/> × <paramref name="height"/> pixels, opaque where <paramref name="solid"/>(x, y).</summary>
        public Sprite Create(string name, int width, int height, Func<int, int, bool> solid, SpriteMeshType meshType = SpriteMeshType.Tight)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point };
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = solid(x, y) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0,
                meshType, Vector4.zero, true);
            sprite.name = name;
            _created.Add(texture);
            _created.Add(sprite);
            return sprite;
        }

        /// <summary>1 × 1 unit opaque square.</summary>
        public Sprite Square() => Create("Square", 64, 64, (x, y) => true);

        /// <summary>Disc of radius 0.5 units.</summary>
        public Sprite Disc() => Create("Disc", 64, 64, (x, y) => Dist(x, y, 32) <= 32f);

        /// <summary>Ring: outer radius 1, inner radius 0.5 units (a hole in the middle).</summary>
        public Sprite Ring() => Create("Ring", 128, 128, (x, y) =>
        {
            var d = Dist(x, y, 64);
            return d <= 64f && d >= 32f;
        });

        /// <summary>L: 2 × 2 units with the top-right 1 × 1 quarter cut away (area 3).</summary>
        public Sprite L() => Create("L", 128, 128, (x, y) => x < 64 || y < 64);

        /// <summary><paramref name="width"/> × <paramref name="height"/> unit opaque rectangle.</summary>
        public Sprite Rectangle(float width, float height) =>
            Create("Rect", Mathf.RoundToInt(width * PixelsPerUnit), Mathf.RoundToInt(height * PixelsPerUnit), (x, y) => true);

        private static float Dist(int x, int y, float center) => Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));

        public static GameObject SpriteObject(string name, Sprite sprite, Vector3 position, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<SpriteRenderer>().sprite = sprite;
            return go;
        }

        public void DestroyAll()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }
    }
}
