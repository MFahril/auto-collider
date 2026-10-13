using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Batch-scoped memo of strategy results. Scenes repeat props (hundreds of identical rocks, crates, chairs): parts with the
    /// same geometry in target space (same mesh and scale — the target's own position / rotation don't matter), strategy,
    /// settings and fit context get the same shapes, so each is fitted once per batch. Inactive outside a <see cref="Scope"/>.
    /// </summary>
    internal static class FitCache
    {
        private static Dictionary<(ulong geometry, string settings, bool rotation, bool dynamic), List<FittedShape>> _entries;

        /// <summary>True inside a <see cref="Scope"/>.</summary>
        public static bool Active => _entries != null;

        /// <summary>Cache hits since the scope began (diagnostics / tests).</summary>
        public static int Hits { get; private set; }

        /// <summary>Enable caching until the returned handle is disposed.</summary>
        public static IDisposable Scope()
        {
            _entries = new Dictionary<(ulong, string, bool, bool), List<FittedShape>>();
            Hits = 0;
            return new Handle();
        }

        /// <summary>Fit <paramref name="part"/>, or return copies of an identical earlier fit.</summary>
        /// <param name="settingsKey">Everything else that affects the result (strategy and settings), as one string.</param>
        public static List<FittedShape> Fit(IColliderStrategy strategy, MeshGeometry part, FitContext context, string settingsKey)
        {
            if (_entries == null || settingsKey == null) return strategy.Fit(FitInput.Create(part), context);

            var key = (Hash(part), settingsKey, context.AllowRotation, context.IsDynamic);
            if (_entries.TryGetValue(key, out var cached)) Hits++;
            else _entries[key] = cached = strategy.Fit(FitInput.Create(part), context);
            return cached.ConvertAll(s => s.Clone());
        }

        private static Dictionary<(ulong outline, string settings, bool rotation), List<FittedShape2D>> _entries2D;

        /// <summary>2D: fit <paramref name="outline"/>, or return copies of an identical earlier fit.</summary>
        public static List<FittedShape2D> Fit2D(IColliderStrategy2D strategy, Outline2D outline, FitContext context, string settingsKey)
        {
            if (_entries == null || settingsKey == null) return strategy.Fit(outline, context);

            _entries2D ??= new Dictionary<(ulong, string, bool), List<FittedShape2D>>();
            var key = (Hash(outline), settingsKey, context.AllowRotation);
            if (_entries2D.TryGetValue(key, out var cached)) Hits++;
            else _entries2D[key] = cached = strategy.Fit(outline, context);
            return cached.ConvertAll(s => s.Clone());
        }

        private static ulong Hash(Outline2D outline)
        {
            const ulong prime = 1099511628211UL;
            var hash = 14695981039346656037UL;
            foreach (var loop in outline.Loops)
            {
                hash = (hash ^ (uint)loop.Length) * prime;
                foreach (var p in loop)
                {
                    hash = (hash ^ math.asuint(p.x)) * prime;
                    hash = (hash ^ math.asuint(p.y)) * prime;
                }
            }
            return hash;
        }

        /// <summary>64-bit FNV-1a over vertex bits and indices.</summary>
        private static ulong Hash(MeshGeometry part)
        {
            const ulong prime = 1099511628211UL;
            var hash = 14695981039346656037UL;
            void Add(uint value)
            {
                hash ^= value;
                hash *= prime;
            }

            Add((uint)part.Vertices.Length);
            foreach (var v in part.Vertices)
            {
                Add(math.asuint(v.x));
                Add(math.asuint(v.y));
                Add(math.asuint(v.z));
            }
            Add((uint)part.Triangles.Length);
            foreach (var t in part.Triangles) Add((uint)t);
            return hash;
        }

        private sealed class Handle : IDisposable
        {
            public void Dispose()
            {
                _entries = null;
                _entries2D = null;
                SpriteSource.ClearCache();
            }
        }
    }
}
