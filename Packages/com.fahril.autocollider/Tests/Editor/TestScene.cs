using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoCollider.Editor.Tests
{
    /// <summary>
    /// Scene bookkeeping for tests that create objects. Cleanup first reverts every Undo operation the test recorded, then
    /// destroys the objects it created: the test runner reverts Undo after a run, and an Undo-recorded destroy of a component
    /// would otherwise resurrect its (already destroyed) GameObject in whatever scene is open.
    /// </summary>
    internal sealed class TestScene
    {
        private readonly HashSet<GameObject> _rootsBefore;
        private readonly int _undoGroup;

        public TestScene()
        {
            _rootsBefore = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
        }

        public void Cleanup(string assetFolder = null)
        {
            Undo.RevertAllDownToGroup(_undoGroup);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_rootsBefore.Contains(root)) Object.DestroyImmediate(root);
            if (assetFolder != null) AssetDatabase.DeleteAsset(assetFolder);
        }
    }
}
