namespace AutoCollider
{
    /// <summary>Where an <see cref="AnimatedMeshCollider"/> takes its mesh from.</summary>
    public enum AnimatedMeshSource
    {
        /// <summary>Bake the current pose (bones + blend shapes) — accurate, costs a skin + re-cook per refresh.</summary>
        BakeMesh = 0,

        /// <summary>Use the renderer's shared (bind-pose) mesh — no per-frame cost, ignores animation. Refreshed only when the mesh is swapped.</summary>
        SharedMesh = 1,
    }
}
