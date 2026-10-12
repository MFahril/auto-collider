using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.IMGUI.Controls;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace AutoCollider.Editor
{
    /// <summary>
    /// Scene-view tool for editing the colliders of selected <see cref="ColliderGroup"/>s:
    /// click / box-select colliders (repeat-click cycles through overlaps), resize with face handles (Alt = symmetric),
    /// move / rotate, hold V to snap to mesh vertices. Edit-menu commands (Duplicate, Delete, Frame, Select All) act on
    /// the selected colliders while the tool is active. Operations live in <see cref="ColliderOperations"/>.
    /// </summary>
    [EditorTool(Strings.ToolName, typeof(ColliderGroup))]
    internal sealed class ColliderEditTool : EditorTool
    {
        private static readonly int PickHint = "AutoColliderPick".GetHashCode();

        private readonly BoxBoundsHandle _box = new();
        private readonly SphereBoundsHandle _sphere = new();
        private readonly CapsuleBoundsHandle _capsule = new();
        private readonly ColliderToolShortcutContext _shortcuts = new();
        private readonly Dictionary<ColliderGroup, Vector3[]> _snapVertices = new();
        private GUIContent _icon;

        private bool _boxSelecting;
        private Vector2 _dragStart;
        private Vector2 _dragNow;

        public override GUIContent toolbarIcon =>
            _icon ??= new GUIContent(AssetPreview.GetMiniTypeThumbnail(typeof(BoxCollider)), Strings.ToolTooltip);

        private IEnumerable<ColliderGroup> Groups => targets.OfType<ColliderGroup>().Where(g => g != null);

        public override void OnActivated()
        {
            ShortcutManager.RegisterContext(_shortcuts);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += ClearSnapCache;

            var handleSize = AutoColliderSettings.instance.Data.HandleSize;
            Handles.SizeFunction size = p => HandleUtility.GetHandleSize(p) * 0.03f * handleSize;
            _box.midpointHandleSizeFunction = size;
            _sphere.midpointHandleSizeFunction = size;
            _capsule.midpointHandleSizeFunction = size;

            ColliderEditState.Prune();
            ColliderToolCommands.ShowOverlay();
            ColliderToolCommands.Notify(Strings.ToolActivatedHint);
        }

        public override void OnWillBeDeactivated()
        {
            ShortcutManager.UnregisterContext(_shortcuts);
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= ClearSnapCache;
            ClearSnapCache();
        }

        private void OnUndoRedo()
        {
            ColliderEditState.Prune();
            ClearSnapCache();
            ColliderEditState.NotifyChanged(); // region lists and entries may have changed
        }

        private void ClearSnapCache()
        {
            _snapVertices.Clear();
            PaintMeshCache.Clear();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView view)) return;
            var e = Event.current;
            var groups = Groups.ToList();

            HandleCommands(e, view, groups);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (ColliderEditState.Mode == ColliderToolMode.Paint && ColliderEditState.PaintedCount > 0) ColliderEditState.ClearPaint();
                else if (ColliderEditState.Selected.Count > 0) ColliderEditState.ClearSelection();
                else ColliderToolCommands.ExitTool();
                e.Use();
            }

            if (e.type == EventType.Repaint) DrawColliders(groups);

            if (ColliderEditState.Mode == ColliderToolMode.Paint)
            {
                HandlePainting(e, view, groups);
                return;
            }
            if (ColliderEditState.Mode == ColliderToolMode.Edit) DrawActiveHandles(groups);
            HandlePicking(e, groups);
        }

        // ---------- Painting ----------

        private bool _brushHit;
        private Vector3 _brushPoint;
        private float _brushWorldRadius;

        private void HandlePainting(Event e, SceneView view, List<ColliderGroup> groups)
        {
            var id = GUIUtility.GetControlID(PickHint, FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseMove:
                    UpdateBrush(e.mousePosition, view, groups);
                    HandleUtility.Repaint();
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt && HandleUtility.nearestControl == id:
                    GUIUtility.hotControl = id;
                    PaintAt(e.mousePosition, view, groups, EditorGUI.actionKey);
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    PaintAt(e.mousePosition, view, groups, EditorGUI.actionKey);
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.Repaint:
                    PaintDrawing.DrawRegions(groups);
                    PaintDrawing.DrawPainted();
                    if (_brushHit) PaintDrawing.DrawBrush(_brushPoint, view.camera.transform.forward, _brushWorldRadius, EditorGUI.actionKey);
                    break;
            }
        }

        private void PaintAt(Vector2 mouse, SceneView view, List<ColliderGroup> groups, bool erase)
        {
            UpdateBrush(mouse, view, groups);
            if (!_brushHit) return;

            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var hits = PaintMesh.Brush(PaintMeshCache.For(groups), ray, view.camera.transform.position, mouse,
                ColliderEditState.BrushRadius, _brushWorldRadius * 1.5f, HandleUtility.WorldToGUIPoint);
            foreach (var byMesh in hits.GroupBy(h => h.mesh))
                ColliderEditState.Paint(byMesh.Key.Renderer, byMesh.Select(h => h.triangle), erase);
        }

        /// <summary>Where the brush touches the surface, and its radius in world units there.</summary>
        private void UpdateBrush(Vector2 mouse, SceneView view, List<ColliderGroup> groups)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            _brushHit = false;
            var nearest = float.MaxValue;
            foreach (var mesh in PaintMeshCache.For(groups))
                if (mesh.Raycast(ray, out _, out var d) && d < nearest)
                {
                    nearest = d;
                    _brushHit = true;
                }
            if (!_brushHit) return;

            _brushPoint = ray.GetPoint(nearest);
            var camera = view.camera;
            var worldPerPixel = camera.orthographic
                ? 2f * camera.orthographicSize / camera.pixelHeight
                : 2f * Vector3.Distance(camera.transform.position, _brushPoint) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / camera.pixelHeight;
            _brushWorldRadius = ColliderEditState.BrushRadius * EditorGUIUtility.pixelsPerPoint * worldPerPixel;
        }

        // ---------- Drawing ----------

        private static void DrawColliders(List<ColliderGroup> groups)
        {
            var xray = ColliderEditState.Xray;
            var labels = ColliderEditState.ShowLabels;
            foreach (var group in groups)
            {
                var space = ColliderShapes.SpaceOf(group);
                foreach (var entry in group.Entries)
                {
                    var c = entry.Collider;
                    if (!ColliderEditState.IsVisible(c)) continue;
                    var selected = ColliderEditState.IsSelected(c);
                    ColliderDrawing.Draw(c, group, space, selected, xray, labels || selected);
                }
            }
        }

        // ---------- Handles ----------

        private void DrawActiveHandles(List<ColliderGroup> groups)
        {
            var active = ColliderEditState.Active;
            if (!ColliderEditState.IsVisible(active)) return;
            if (!ColliderShapes.TryFind(active, out var group, out _) || !groups.Contains(group)) return;

            var space = ColliderShapes.SpaceOf(group);
            var groupMatrix = Matrix4x4.TRS(group.transform.position, group.transform.rotation, Vector3.one);
            var color = AutoColliderSettings.instance.Data.SelectedColor;

            switch (ColliderEditState.HandleMode)
            {
                case ColliderHandleMode.Resize when !(active is MeshCollider):
                    ResizeHandle(active, group, space, groupMatrix, color);
                    break;
                case ColliderHandleMode.Move:
                    MoveHandle(active, group, space);
                    break;
                case ColliderHandleMode.Rotate:
                    RotateHandle(active, group, space);
                    break;
            }
        }

        private void ResizeHandle(Collider active, ColliderGroup group, in TargetSpace space, Matrix4x4 groupMatrix, Color color)
        {
            var shape = ColliderShapes.ToShape(active, group, space);
            if (shape == null) return;
            var frame = groupMatrix * Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one);

            using (new Handles.DrawingScope(color, frame))
            {
                EditorGUI.BeginChangeCheck();
                switch (shape.Kind)
                {
                    case ShapeKind.Box:
                        _box.center = Vector3.zero;
                        _box.size = shape.Size;
                        _box.DrawHandle();
                        break;
                    case ShapeKind.Sphere:
                        _sphere.center = Vector3.zero;
                        _sphere.radius = shape.Radius;
                        _sphere.DrawHandle();
                        break;
                    case ShapeKind.Capsule:
                        _capsule.center = Vector3.zero;
                        _capsule.heightAxis = (CapsuleBoundsHandle.HeightAxis)shape.Direction;
                        _capsule.radius = shape.Radius;
                        _capsule.height = shape.Height;
                        _capsule.DrawHandle();
                        break;
                }
                if (!EditorGUI.EndChangeCheck()) return;

                float3 localCenter;
                switch (shape.Kind)
                {
                    case ShapeKind.Box:
                    {
                        float3 min = _box.center - _box.size * 0.5f;
                        float3 max = _box.center + _box.size * 0.5f;
                        if (TrySnapVertex(group, space, out var vertex))
                            VertexSnapping.SnapChangedFaces(-shape.Size * 0.5f, shape.Size * 0.5f, ref min, ref max,
                                (Vector3)frame.inverse.MultiplyPoint3x4(vertex));
                        localCenter = (min + max) * 0.5f;
                        shape.Size = math.max(max - min, PhysicsLimits.MinExtent);
                        break;
                    }
                    case ShapeKind.Sphere:
                        localCenter = (float3)_sphere.center;
                        shape.Radius = math.max(_sphere.radius, PhysicsLimits.MinExtent);
                        break;
                    default:
                        localCenter = (float3)_capsule.center;
                        shape.Radius = math.max(_capsule.radius, PhysicsLimits.MinExtent);
                        shape.Height = math.max(_capsule.height, 2f * shape.Radius);
                        break;
                }
                shape.Center += math.mul(shape.Rotation, localCenter);
                ApplyAndTrack(active, shape, Strings.UndoResize);
            }
        }

        private void MoveHandle(Collider active, ColliderGroup group, in TargetSpace space)
        {
            var isMesh = active is MeshCollider;
            var shape = isMesh ? null : ColliderShapes.ToShape(active, group, space);
            if (!isMesh && shape == null) return;

            Vector3 center = isMesh ? active.bounds.center : ColliderShapes.ToWorld(space, shape.Center);
            var rotation = Tools.pivotRotation == PivotRotation.Local
                ? (isMesh ? active.transform.rotation : group.transform.rotation * (Quaternion)shape.Rotation)
                : Quaternion.identity;

            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(center, rotation);
            if (!EditorGUI.EndChangeCheck()) return;
            if (TrySnapVertex(group, space, out var vertex)) moved = vertex;

            if (isMesh)
            {
                var detached = ColliderOperations.DetachToChild(active);
                ColliderEditState.Replace(active, detached);
                Undo.RecordObject(detached.transform, Strings.UndoMove);
                detached.transform.position += moved - center;
                MarkEdited(detached);
            }
            else
            {
                shape.Center = ColliderShapes.ToScaled(space, moved);
                ApplyAndTrack(active, shape, Strings.UndoMove);
            }
        }

        private void RotateHandle(Collider active, ColliderGroup group, in TargetSpace space)
        {
            if (active is SphereCollider) return;
            var isMesh = active is MeshCollider;
            var shape = isMesh ? null : ColliderShapes.ToShape(active, group, space);
            if (!isMesh && shape == null) return;

            Vector3 center = isMesh ? active.bounds.center : ColliderShapes.ToWorld(space, shape.Center);
            var worldRotation = isMesh ? active.transform.rotation : group.transform.rotation * (Quaternion)shape.Rotation;

            EditorGUI.BeginChangeCheck();
            var rotated = Handles.RotationHandle(worldRotation, center);
            if (!EditorGUI.EndChangeCheck()) return;

            if (!space.IsUniform)
            {
                ColliderToolCommands.Notify(Strings.NotifyRotateNeedsUniformScale);
                return;
            }

            if (isMesh)
            {
                var detached = ColliderOperations.DetachToChild(active);
                ColliderEditState.Replace(active, detached);
                var t = detached.transform;
                var delta = rotated * Quaternion.Inverse(worldRotation);
                Undo.RecordObject(t, Strings.UndoRotate);
                t.rotation = delta * t.rotation;
                t.position = center + delta * (t.position - center);
                MarkEdited(detached);
            }
            else
            {
                shape.Rotation = math.mul(math.inverse((quaternion)group.transform.rotation), (quaternion)rotated);
                ApplyAndTrack(active, shape, Strings.UndoRotate);
            }
        }

        private static void ApplyAndTrack(Collider active, FittedShape shape, string undoName)
        {
            var result = ColliderOperations.Apply(active, shape, undoName);
            ColliderEditState.Replace(active, result);
        }

        private static void MarkEdited(Collider collider)
        {
            if (ColliderShapes.TryFind(collider, out var group, out var entry)) ColliderOperations.MarkEdited(group, entry);
        }

        /// <summary>While V is held, the source-mesh vertex nearest to the mouse (screen space).</summary>
        private bool TrySnapVertex(ColliderGroup group, in TargetSpace space, out Vector3 vertex)
        {
            vertex = default;
            if (!ColliderToolShortcuts.VertexSnapHeld) return false;
            if (!_snapVertices.TryGetValue(group, out var vertices))
            {
                var scaled = ColliderOperations.SourceVertices(group, space);
                vertices = new Vector3[scaled.Length];
                for (var i = 0; i < scaled.Length; i++) vertices[i] = ColliderShapes.ToWorld(space, scaled[i]);
                _snapVertices[group] = vertices;
            }
            return VertexSnapping.TryNearest(vertices, Event.current.mousePosition, HandleUtility.WorldToGUIPoint,
                VertexSnapping.SnapPixels, out vertex);
        }

        // ---------- Picking ----------

        private void HandlePicking(Event e, List<ColliderGroup> groups)
        {
            var id = GUIUtility.GetControlID(PickHint, FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    // Lets clicks on empty space reach us instead of Unity's object picking (which would change selection).
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt && HandleUtility.nearestControl == id:
                    GUIUtility.hotControl = id;
                    _dragStart = _dragNow = e.mousePosition;
                    _boxSelecting = false;
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    _dragNow = e.mousePosition;
                    if (!_boxSelecting && Vector2.Distance(_dragStart, _dragNow) > 5f) _boxSelecting = true;
                    HandleUtility.Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                {
                    GUIUtility.hotControl = 0;
                    var op = e.shift ? SelectOp.Add : EditorGUI.actionKey ? SelectOp.Toggle : SelectOp.Replace;
                    var candidates = ColliderShapes.CollidersOf(groups).Where(ColliderEditState.IsVisible).ToList();

                    if (_boxSelecting)
                    {
                        var inside = ColliderPicker.InRect(ColliderPicker.RectFrom(_dragStart, _dragNow), candidates, HandleUtility.WorldToGUIPoint);
                        if (inside.Count > 0) ColliderEditState.Select(inside, op);
                        else if (op == SelectOp.Replace) ColliderEditState.ClearSelection();
                    }
                    else
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var hit = ColliderPicker.PickCycling(ray, candidates, e.mousePosition);
                        if (hit != null) ColliderEditState.Select(hit, op);
                        else if (op == SelectOp.Replace) ColliderEditState.ClearSelection();
                    }
                    _boxSelecting = false;
                    e.Use();
                    break;
                }

                case EventType.Repaint when _boxSelecting:
                    Handles.BeginGUI();
                    var settings = AutoColliderSettings.instance.Data;
                    var outline = settings.SelectedColor;
                    var fill = outline;
                    fill.a = 0.12f;
                    Handles.DrawSolidRectangleWithOutline(ColliderPicker.RectFrom(_dragStart, _dragNow), fill, outline);
                    Handles.EndGUI();
                    break;
            }
        }

        // ---------- Edit-menu commands ----------

        private static void HandleCommands(Event e, SceneView view, List<ColliderGroup> groups)
        {
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;
            var hasSelection = ColliderEditState.Selected.Count > 0;
            var name = e.commandName;
            var handled = name == "SelectAll"
                          || (hasSelection && (name == "Duplicate" || name == "SoftDelete" || name == "Delete" || name == "FrameSelected"));
            if (!handled) return;

            if (e.type == EventType.ExecuteCommand)
            {
                switch (name)
                {
                    case "Duplicate": ColliderToolCommands.Duplicate(); break;
                    case "SoftDelete":
                    case "Delete": ColliderToolCommands.Delete(); break;
                    case "FrameSelected": ColliderToolCommands.FrameSelection(view); break;
                    case "SelectAll": ColliderToolCommands.SelectAll(groups); break;
                }
            }
            e.Use();
        }
    }
}
