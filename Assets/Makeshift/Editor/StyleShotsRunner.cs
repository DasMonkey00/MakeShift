using UnityEditor;
using UnityEditor.SceneManagement;

namespace Makeshift.EditorTools
{
    public static class StyleShotsRunner
    {
        // Run with -makeshiftShots; StyleShots starts itself once play mode loads.
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Playground.unity");
            EditorApplication.isPlaying = true;
        }
    }
}
