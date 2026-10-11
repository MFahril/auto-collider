namespace AutoCollider.Editor
{
    /// <summary>Human-readable units for the UI.</summary>
    internal static class Format
    {
        /// <summary>Meters as mm / cm / m.</summary>
        public static string Distance(float meters)
        {
            var abs = System.Math.Abs(meters);
            if (abs < 0.01f) return $"{meters * 1000f:0.#} mm";
            if (abs < 1f) return $"{meters * 100f:0.#} cm";
            return $"{meters:0.##} m";
        }

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.#} KB";
            return $"{bytes / (1024f * 1024f):0.##} MB";
        }
    }
}
