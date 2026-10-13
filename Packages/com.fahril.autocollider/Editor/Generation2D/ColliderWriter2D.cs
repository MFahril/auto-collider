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
            var host = container.gameObject;
            var scale = space.Scale.xy;
            var center = shape.Center / scale;
            // Rotation is only produced under equal, positive X / Y scale, so dividing by one component is exact.
            var uniform = math.abs(scale.x);

            if (shape.IsRotated)
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
                    polygon.pathCount = shape.Paths.Length;
                    for (var p = 0; p < shape.Paths.Length; p++)
                    {
                        var path = shape.Paths[p];
                        var points = new Vector2[path.Length];
                        for (var i = 0; i < path.Length; i++) points[i] = path[i] / scale;
                        polygon.SetPath(p, points);
                    }
                    return polygon;
                }
            }
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
