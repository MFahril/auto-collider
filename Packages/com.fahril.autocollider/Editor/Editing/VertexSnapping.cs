using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>Vertex snapping (hold V): faces and move handles snap to the source-mesh vertex under the mouse.</summary>
    internal static class VertexSnapping
    {
        public const float SnapPixels = 24f;

        /// <summary>Vertex closest to <paramref name="guiPoint"/> on screen, within <paramref name="maxPixels"/>.</summary>
        public static bool TryNearest(IReadOnlyList<Vector3> worldVertices, Vector2 guiPoint, Func<Vector3, Vector2> worldToGui,
            float maxPixels, out Vector3 vertex)
        {
            vertex = default;
            var best = maxPixels * maxPixels;
            var found = false;
            for (var i = 0; i < worldVertices.Count; i++)
            {
                var d = (worldToGui(worldVertices[i]) - guiPoint).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                vertex = worldVertices[i];
                found = true;
            }
            return found;
        }

        /// <summary>
        /// After a bounds-handle drag (in the shape's local frame), move whichever face(s) changed onto the vertex's
        /// coordinate on that axis.
        /// </summary>
        public static void SnapChangedFaces(float3 oldMin, float3 oldMax, ref float3 newMin, ref float3 newMax, float3 vertexLocal)
        {
            const float epsilon = 1e-6f;
            for (var axis = 0; axis < 3; axis++)
            {
                if (math.abs(newMin[axis] - oldMin[axis]) > epsilon) newMin[axis] = math.min(vertexLocal[axis], newMax[axis] - PhysicsLimits.MinExtent);
                if (math.abs(newMax[axis] - oldMax[axis]) > epsilon) newMax[axis] = math.max(vertexLocal[axis], newMin[axis] + PhysicsLimits.MinExtent);
            }
        }
    }
}
