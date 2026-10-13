using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
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

        /// <summary>
        /// 2 × 0.5 unit arm rigged with two bones: "Upper" at sprite x = −1 and its child "Lower" at x = 0, each owning one
        /// 1-unit half of the mesh (core sprite skinning data only — no 2D Animation package needed).
        /// </summary>
        public Sprite SkinnedArm()
        {
            var sprite = Create("Arm", 128, 32, (x, y) => true, SpriteMeshType.FullRect);
            var xs = new[] { -1f, 0f, 0f, 1f };
            using (var positions = new NativeArray<Vector3>(8, Allocator.Temp))
            using (var weights = new NativeArray<BoneWeight>(8, Allocator.Temp))
            using (var indices = new NativeArray<ushort>(12, Allocator.Temp))
            using (var bindposes = new NativeArray<Matrix4x4>(2, Allocator.Temp))
            {
                var p = positions;
                var w = weights;
                for (var i = 0; i < 4; i++)
                {
                    p[2 * i] = new Vector3(xs[i], -0.25f, 0f);
                    p[2 * i + 1] = new Vector3(xs[i], 0.25f, 0f);
                    var bone = i < 2 ? 0 : 1;
                    w[2 * i] = w[2 * i + 1] = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
                }
                var t = indices;
                ushort[] quads = { 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6 };
                for (var i = 0; i < quads.Length; i++) t[i] = quads[i];
                var b = bindposes;
                b[0] = Matrix4x4.Translate(new Vector3(1f, 0f, 0f));
                b[1] = Matrix4x4.identity;

                sprite.SetVertexCount(8);
                sprite.SetVertexAttribute(VertexAttribute.Position, positions);
                sprite.SetVertexAttribute(VertexAttribute.BlendWeight, weights);
                sprite.SetIndices(indices);
                sprite.SetBones(new[]
                {
                    new SpriteBone { name = "Upper", position = new Vector3(-1f, 0f, 0f), rotation = Quaternion.identity, length = 1f, parentId = -1 },
                    new SpriteBone { name = "Lower", position = new Vector3(1f, 0f, 0f), rotation = Quaternion.identity, length = 1f, parentId = 0 },
                });
                sprite.SetBindPoses(bindposes);
            }
            return sprite;
        }

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
