using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AutoCollider.Editor
{
    /// <summary>Inspector for <see cref="AnimatedMeshCollider"/>: settings plus warnings with one-click fixes and a cost estimate.</summary>
    [CustomEditor(typeof(AnimatedMeshCollider))]
    [CanEditMultipleObjects]
    internal sealed class AnimatedMeshColliderEditor : UnityEditor.Editor
    {
        private HelpBox _missing;
        private HelpBox _concave;
        private HelpBox _readable;
        private Label _cost;

        private AnimatedMeshCollider Component => (AnimatedMeshCollider)target;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            AutoColliderStyles.Apply(root);
            root.AddToClassList("ac-inspector");

            var property = serializedObject.GetIterator();
            property.NextVisible(true);
            while (property.NextVisible(false))
            {
                if (property.propertyPath == "m_Script") continue;
                root.Add(new PropertyField(property.Copy()));
            }

            _missing = new HelpBox(Strings.AnimatedMissing, HelpBoxMessageType.Warning);
            root.Add(_missing);

            _concave = new HelpBox(Strings.AnimatedConcaveDynamic, HelpBoxMessageType.Error);
            var makeConvex = new Button(MakeConvex) { text = Strings.AnimatedMakeConvex, tooltip = Strings.AnimatedMakeConvexTooltip };
            makeConvex.AddToClassList("ac-button");
            _concave.Add(makeConvex);
            root.Add(_concave);

            _readable = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            var enableReadWrite = new Button(EnableReadWrite) { text = Strings.AnimatedEnableReadWrite, tooltip = Strings.AnimatedEnableReadWriteTooltip };
            enableReadWrite.AddToClassList("ac-button");
            _readable.Add(enableReadWrite);
            root.Add(_readable);

            _cost = new Label();
            _cost.AddToClassList("ac-hint");
            root.Add(_cost);

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.schedule.Execute(Refresh).Every(1000);
            Refresh();
            return root;
        }

        private void Refresh()
        {
            if (target == null || _missing == null) return;
            var c = Component;
            var source = c.Source;
            var collider = c.Target;

            Show(_missing, source == null || collider == null);
            Show(_concave, collider != null && !collider.convex && GenerationPipeline.IsDynamic(collider.gameObject));

            var mesh = source != null ? source.sharedMesh : null;
            var needsReadable = c.Mode == AnimatedMeshSource.SharedMesh && mesh != null && !mesh.isReadable;
            if (needsReadable) _readable.text = string.Format(Strings.AnimatedNotReadable, mesh.name);
            Show(_readable, needsReadable);

            _cost.text = mesh == null ? string.Empty
                : c.Mode == AnimatedMeshSource.SharedMesh ? Strings.AnimatedSharedHint
                : string.Format(Strings.AnimatedCost, MeshTriangles(mesh), Frequency(c));
        }

        private static string Frequency(AnimatedMeshCollider c) => c.UpdateMode switch
        {
            ColliderUpdateMode.EveryFrame => Strings.AnimatedCostPerFrame,
            ColliderUpdateMode.EveryNFrames => string.Format(Strings.AnimatedCostPerNFrames, c.EveryNFrames),
            _ => string.Format(Strings.AnimatedCostHz, c.TargetHz),
        };

        private static long MeshTriangles(Mesh mesh)
        {
            long count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++) count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        private void MakeConvex()
        {
            foreach (var t in targets)
            {
                var collider = ((AnimatedMeshCollider)t).Target;
                if (collider == null) continue;
                Undo.RecordObject(collider, Strings.AnimatedMakeConvex);
                collider.convex = true;
                if (PrefabUtility.IsPartOfPrefabInstance(collider)) PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            Refresh();
        }

        private void EnableReadWrite()
        {
            var source = Component.Source;
            var mesh = source != null ? source.sharedMesh : null;
            if (mesh == null) return;
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter importer)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            Refresh();
        }

        private static void Show(VisualElement element, bool visible) => element.EnableInClassList("ac-hidden", !visible);
    }
}
