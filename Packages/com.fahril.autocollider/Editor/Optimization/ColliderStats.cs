namespace AutoCollider.Editor
{
    /// <summary>Before/after numbers for an optimization (mesh colliders).</summary>
    internal struct ColliderStats
    {
        public int SourceVertices;
        public int SourceTriangles;
        public int ResultVertices;
        public int ResultTriangles;
        /// <summary>Number of collider parts (convex decomposition); 0 or 1 for single shapes.</summary>
        public int ResultParts;
        public float MaxError;
        public float MeanError;
        public double Milliseconds;

        /// <summary>Fraction of triangles removed (0..1).</summary>
        public float Savings => SourceTriangles <= 0 ? 0f : 1f - (float)ResultTriangles / SourceTriangles;

        public long SourceBytes => EstimateBytes(SourceVertices, SourceTriangles);
        public long ResultBytes => EstimateBytes(ResultVertices, ResultTriangles);

        /// <summary>Rough cooked-collision memory: positions + indices + per-triangle midphase data.</summary>
        public static long EstimateBytes(int vertices, int triangles) => vertices * 12L + triangles * 16L;

        public void Add(ColliderStats other)
        {
            SourceVertices += other.SourceVertices;
            SourceTriangles += other.SourceTriangles;
            ResultVertices += other.ResultVertices;
            ResultTriangles += other.ResultTriangles;
            ResultParts += other.ResultParts;
            if (other.MaxError > MaxError) MaxError = other.MaxError;
            MeanError = MeanError == 0f ? other.MeanError : (MeanError + other.MeanError) * 0.5f;
            Milliseconds += other.Milliseconds;
        }
    }
}
