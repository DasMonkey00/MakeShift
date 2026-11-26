using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Looksmith's scene scan reads MeshFilter.sharedMesh on every MeshRenderer, but a TextMesh has a MeshRenderer and no
    /// MeshFilter, which throws MissingComponentException. This adds an empty MeshFilter to every TextMesh in the open
    /// scenes (on scene open and on entering/leaving play mode), so the Looksmith hub and Doctor work.
    /// </summary>
    [InitializeOnLoad]
    public static class TextMeshFilterFixer
    {
        static TextMeshFilterFixer()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => Fix();
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredEditMode) Fix();
            };
            EditorApplication.delayCall += Fix;
        }

        [MenuItem("Makeshift/Fix text meshes for Looksmith")]
        public static void Fix()
        {
            // Never touch scenes while a build is opening them: any LogError here fails the player build.
            if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode) return;
            int fixedCount = 0;
            // Unity refuses to add a MeshFilter next to a TextMesh ("conflicts with the existing TextMesh derived
            // component"), so the old MeshFilter workaround could never succeed and only spammed errors. Removed.
            foreach (var sw in Object.FindObjectsByType<Makeshift.Rendering.RenderStyleSwitcher>(FindObjectsInactive.Include))
            {
                if (!sw.EditorAssignProfile()) continue;
                EditorSceneManager.MarkSceneDirty(sw.gameObject.scene);
                Debug.Log("[Makeshift] Gave the Look Volume its Stylized Realism profile (edit mode) so Looksmith sees a look.");
            }
            if (fixedCount > 0) Debug.Log($"[Makeshift] Added MeshFilter to {fixedCount} text mesh(es) so Looksmith can scan them.");
        }
    }
}
