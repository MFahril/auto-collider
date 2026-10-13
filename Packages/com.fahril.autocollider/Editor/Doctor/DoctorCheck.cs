namespace AutoCollider.Editor
{
    /// <summary>The problems the Collider Doctor looks for.</summary>
    internal enum DoctorCheck
    {
        /// <summary>Concave mesh collider moved by a non-kinematic Rigidbody (PhysX rejects it).</summary>
        ConcaveOnMovingBody,

        /// <summary>Concave mesh collider marked as trigger (not supported).</summary>
        ConcaveTrigger,

        /// <summary>Mesh collider without a mesh.</summary>
        MissingMesh,

        /// <summary>Concave mesh collider above the triangle budget.</summary>
        DetailedMesh,

        /// <summary>Convex mesh collider whose mesh has more than 255 triangles (PhysX simplifies it while cooking).</summary>
        ComplexConvex,

        /// <summary>Mesh collider under negative scale (re-cooked mirrored at load).</summary>
        NegativeScale,

        /// <summary>User sphere / capsule under strongly non-uniform scale (Unity sizes it by the largest axis).</summary>
        ScaledPrimitive,

        /// <summary>Two identical colliders on one object.</summary>
        DuplicateCollider,

        /// <summary>Collider Group entries whose collider was deleted.</summary>
        MissingEntries,

        /// <summary>Animated Mesh Collider without source / target, or Shared Mesh mode on a mesh without Read/Write.</summary>
        AnimatedSetup,

        /// <summary>Visible mesh or sprite without any collider (optional check).</summary>
        NoCollider,

        /// <summary>Polygon Collider 2D with more points than the budget.</summary>
        ComplexPolygon2D,

        /// <summary>Tilemap Collider 2D whose tiles are separate shapes (no composite).</summary>
        TilemapWithoutComposite,

        /// <summary>2D collider under a 3D Rigidbody, or 3D collider under a Rigidbody2D: two physics worlds that never meet.</summary>
        MixedPhysics,

        /// <summary>2D collider on an object rotated around X / Y (2D physics ignores that rotation).</summary>
        Tilted2D,
    }
}
