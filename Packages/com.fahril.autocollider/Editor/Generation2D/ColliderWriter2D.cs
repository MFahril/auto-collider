using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Creates 2D collider components for <see cref="FittedShape2D"/>s, fully Undo-able. Unrotated shapes go on the shared
    /// container (2D colliders live in their object's XY plane); rotated boxes / capsules get their own child rotated around Z.
    /// </summary>
    internal static class ColliderWriter2D
    {
        public static Collider2D Create(FittedShape2D shape, Transform container, in TargetSpace space)
        {
            var host = container != null ? container.gameObject : null;
            var scale = space.Scale.xy;
            var center = shape.Center / scale;
            // Rotation is only produced under equal, positive X / Y scale, so dividing by one component is exact.
            var uniform = math.abs(scale.x);

            if (shape.Parent != null)
            {
                // Bone colliders: a child of the bone at the shape's pose, sizes in bone-local units.
                host = CreateChild(shape.Parent, shape.Label);
                var position = shape.IsPolygonal ? float2.zero : shape.Center;
                host.transform.localPosition = new Vector3(position.x, position.y, 0f);
                host.transform.localRotation = Quaternion.Euler(0f, 0f, shape.IsPolygonal ? 0f : shape.Angle);
                center = float2.zero;
                scale = new float2(1f);
                uniform = 1f;
            }
            else if (shape.IsRotated)
            {
                host = CreateChild(container, shape.Label);
                host.transform.localPosition = new Vector3(center.x, center.y, 0f);
                host.transform.localRotation = Quaternion.Euler(0f, 0f, shape.Angle);
                center = float2.zero;
            }

            switch (shape.Kind)
            {
                case ShapeKind2D.Box:
                {
                    var box = Undo.AddComponent<BoxCollider2D>(host);
                    box.offset = (Vector2)center;
                    box.size = (Vector2)(shape.IsRotated ? shape.Size / uniform : math.abs(shape.Size / scale));
                    return box;
                }
                case ShapeKind2D.Circle:
                {
                    // CircleCollider2D grows with the larger of |scale.x| and |scale.y|.
                    var circle = Undo.AddComponent<CircleCollider2D>(host);
                    circle.offset = (Vector2)center;
                    circle.radius = shape.Radius / math.cmax(math.abs(scale));
                    return circle;
                }
                case ShapeKind2D.Capsule:
                {
                    var capsule = Undo.AddComponent<CapsuleCollider2D>(host);
                    capsule.offset = (Vector2)center;
                    capsule.direction = shape.Vertical ? CapsuleDirection2D.Vertical : CapsuleDirection2D.Horizontal;
                    var size = shape.Vertical ? new float2(2f * shape.Radius, shape.Height) : new float2(shape.Height, 2f * shape.Radius);
                    capsule.size = (Vector2)(shape.IsRotated ? size / uniform : math.abs(size / scale));
                    return capsule;
                }
                case ShapeKind2D.Edge:
                {
                    var edge = Undo.AddComponent<EdgeCollider2D>(host);
                    var loop = shape.Paths[0];
                    var points = new Vector2[loop.Length + 1];
                    for (var i = 0; i < loop.Length; i++) points[i] = loop[i] / scale;
                    points[loop.Length] = points[0];
                    edge.points = points;
                    return edge;
                }
                default:
                {
                    var polygon = Undo.AddComponent<PolygonCollider2D>(host);
                    var paths = new Vector2[shape.Paths.Length][];
                    for (var p = 0; p < paths.Length; p++)
                    {
                        var path = shape.Paths[p];
                        paths[p] = new Vector2[path.Length];
                        for (var i = 0; i < path.Length; i++) paths[p][i] = path[i] / scale;
                    }
                    SetPaths(polygon, paths);
                    return polygon;
                }
            }
        }

        /// <summary>
        /// Replace every path of <paramref name="polygon"/>. Unity re-decomposes the whole polygon after each
        /// <c>SetPath</c> on an enabled collider (a baked tilemap with 111 paths: 2.3 s); while disabled it decomposes once,
        /// when re-enabled (38 ms). Not in play mode, where the toggle would end and restart contacts.
        /// </summary>
        public static void SetPaths(PolygonCollider2D polygon, IReadOnlyList<Vector2[]> paths)
        {
            var toggle = polygon.enabled && !Application.isPlaying;
            if (toggle) polygon.enabled = false;
            polygon.pathCount = paths.Count;
            for (var p = 0; p < paths.Count; p++) polygon.SetPath(p, paths[p]);
            if (toggle) polygon.enabled = true;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Collider" : name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(parent.gameObject));
            Undo.RegisterCreatedObjectUndo(go, Strings.UndoGenerate);
            return go;
        }
    }
}
