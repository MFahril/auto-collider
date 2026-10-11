using System.Collections.Generic;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Simplified concave MeshCollider (static / kinematic objects). Uses <see cref="MeshOptimizer"/>; inflate is
    /// applied inside the optimizer, so the pipeline must not inflate this shape again.
    /// </summary>
    internal sealed class OptimizedMeshStrategy : IColliderStrategy
    {
        private readonly GenerationSettings _settings;

        public OptimizedMeshStrategy(GenerationSettings settings)
        {
            _settings = settings;
        }

        public List<FittedShape> Fit(FitInput input, FitContext context)
        {
            var optimized = MeshOptimizer.Optimize(input.Geometry, _settings);
            if (optimized.Mesh.TriangleCount == 0)
                return new List<FittedShape> { BoxStrategy.FitBox(input, context) };

            var shape = FittedShape.FromMesh(optimized.Mesh);
            shape.Stats = optimized.Stats;
            shape.Error = optimized.Error;
            return new List<FittedShape> { shape };
        }
    }
}
