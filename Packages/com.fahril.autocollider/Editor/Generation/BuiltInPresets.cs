using System.Collections.Generic;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Quick optimization presets. They only change the mesh-optimization values (quality, fidelity, triangle cap),
    /// leaving strategy, filters and output untouched.
    /// </summary>
    internal static class BuiltInPresets
    {
        public sealed class Preset
        {
            public Preset(string name, string description, float quality, float fidelity, int maxTriangles)
            {
                Name = name;
                Description = description;
                Quality = quality;
                Fidelity = fidelity;
                MaxTriangles = maxTriangles;
            }

            public string Name { get; }
            public string Description { get; }
            public float Quality { get; }
            public float Fidelity { get; }
            public int MaxTriangles { get; }

            public void ApplyTo(GenerationSettings settings)
            {
                settings.Quality = Quality;
                settings.ShapeFidelity = Fidelity;
                settings.MaxTriangles = MaxTriangles;
            }

            public bool Matches(GenerationSettings settings) =>
                UnityEngine.Mathf.Approximately(settings.Quality, Quality)
                && UnityEngine.Mathf.Approximately(settings.ShapeFidelity, Fidelity)
                && settings.MaxTriangles == MaxTriangles;
        }

        public static readonly Preset HighQuality = new(Strings.PresetHighQuality, Strings.PresetHighQualityInfo, 0.8f, 0.9f, 0);
        public static readonly Preset Balanced = new(Strings.PresetBalanced, Strings.PresetBalancedInfo, 0.5f, 0.6f, 0);
        public static readonly Preset Performance = new(Strings.PresetPerformance, Strings.PresetPerformanceInfo, 0.3f, 0.5f, 2000);
        public static readonly Preset Mobile = new(Strings.PresetMobile, Strings.PresetMobileInfo, 0.15f, 0.4f, 500);

        public static IReadOnlyList<Preset> All { get; } = new[] { HighQuality, Balanced, Performance, Mobile };

        /// <summary>Name of the built-in preset matching the settings, or null ("Custom").</summary>
        public static string Identify(GenerationSettings settings)
        {
            foreach (var preset in All)
                if (preset.Matches(settings)) return preset.Name;
            return null;
        }
    }
}
