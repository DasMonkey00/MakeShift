using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>Renders every imported model on a grid (front view and back view) into Logs/shots/model_sheet_*.png.</summary>
    public static class ModelSheet
    {
        public static void Run()
        {
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var files = Directory.GetFiles("Assets/Makeshift/Models", "*.glb");

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);

            int cols = 6;
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                var root = new GameObject(name);
                root.transform.position = new Vector3((i % cols) * 2f, -(i / cols) * 2f, 0f);
                var holder = GreyboxSceneBuilderAccess.Model(root.transform, name, 1.5f);
                if (holder == null) continue;
                var label = new GameObject("L").AddComponent<TextMesh>();
                label.transform.position = root.transform.position + new Vector3(0, -0.95f, -0.5f);
                label.text = name;
                label.characterSize = 0.03f; label.fontSize = 60; label.anchor = TextAnchor.MiddleCenter;
                label.color = Color.black;
            }

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.75f, 0.85f, 0.95f);
            cam.orthographic = true;
            cam.orthographicSize = 4.2f;
            var rt = new RenderTexture(1920, 1280, 24);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Logs/shots");

            // Unity's front: the camera looks along +Z from -Z. Second sheet looks from the other side.
            foreach (var view in new[] { "front", "back" })
            {
                bool back = view == "back";
                cam.transform.position = new Vector3(5f, -3.5f, back ? 20f : -20f);
                cam.transform.rotation = Quaternion.Euler(0f, back ? 180f : 0f, 0f);
                cam.Render();
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes($"Logs/shots/model_sheet_{view}.png", tex.EncodeToPNG());
            }
            Debug.Log("[MODELS] sheets written");
        }
    }

    /// <summary>Lets the sheet tool call the builder's model loader.</summary>
    static class GreyboxSceneBuilderAccess
    {
        public static GameObject Model(Transform parent, string file, float size)
        {
            var m = typeof(GreyboxSceneBuilder).GetMethod("Model", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (GameObject)m.Invoke(null, new object[] { parent, file, size, -1, GreyboxSceneBuilder.Pivot.Center, default(Vector3), default(Vector3), false, null });
        }
    }
}
