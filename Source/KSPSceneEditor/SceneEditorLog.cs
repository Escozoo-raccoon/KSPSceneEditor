using UnityEngine;
namespace KSPSceneEditor
{
    internal static class SceneEditorLog
    {
        internal static void Info(string s) { Debug.Log("[KSPSceneEditor] " + s); }
        internal static void Warn(string s) { Debug.LogWarning("[KSPSceneEditor] " + s); }
    }
}
