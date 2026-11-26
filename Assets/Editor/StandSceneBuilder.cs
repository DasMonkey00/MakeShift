using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Template/Build Standalone VR Scene: a non-passthrough scene with the Auto Hand rig, grip helpers (HandAlignPro, finger assist,
/// crouch, spawn recenter) and a table of grabbable test props. Rebuild any time; it overwrites Assets/Scenes/Standalone.unity.
/// </summary>
public static class StandSceneBuilder {
    const string ScenePath = "Assets/Scenes/Standalone.unity";

    [MenuItem("Template/Build Standalone VR Scene")]
    public static void Build() {
        TemplateCommon.ApplyPhysics();
        TemplateCommon.SetupXR();
        HandTrackingSetup.DisableOpenXRHandTrackingFeatures();     // controllers by default; Template/Enable Hand Tracking switches it on
        DisablePassthroughFeatures();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f); RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.42f); RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.25f);
        var sunGo = new GameObject("Sun"); sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1f; sun.shadows = LightShadows.Soft;
        RenderSettings.sun = sun;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        // floor
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor";
        floor.transform.position = new Vector3(0, -0.5f, 0); floor.transform.localScale = new Vector3(20, 1, 20);
        floor.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.35f, 0.37f, 0.4f), name = "Floor_mat" };

        // table + props (replace with your own items; author grips with HandAlignPro, see README)
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.name = "Table";
        table.transform.position = new Vector3(0, 0.375f, 0.9f); table.transform.localScale = new Vector3(1.4f, 0.75f, 0.7f);
        table.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.5f, 0.38f, 0.25f), name = "Table_mat" };
        TemplateCommon.MakeGrabbable("Cube", PrimitiveType.Cube, new Vector3(-0.4f, 0.85f, 0.9f), Vector3.one * 0.08f, 0.2f, new Color(0.9f, 0.3f, 0.2f));
        TemplateCommon.MakeGrabbable("Ball", PrimitiveType.Sphere, new Vector3(-0.15f, 0.85f, 0.9f), Vector3.one * 0.09f, 0.15f, new Color(0.2f, 0.6f, 0.9f));
        TemplateCommon.MakeGrabbable("Handle", PrimitiveType.Capsule, new Vector3(0.15f, 0.88f, 0.9f), new Vector3(0.035f, 0.1f, 0.035f), 0.3f, new Color(0.95f, 0.8f, 0.2f));
        TemplateCommon.MakeGrabbable("Block", PrimitiveType.Cube, new Vector3(0.4f, 0.85f, 0.9f), new Vector3(0.14f, 0.06f, 0.06f), 0.4f, new Color(0.3f, 0.8f, 0.4f));

        var player = TemplateCommon.AddPlayer(new Vector3(0, 0.05f, 0), Quaternion.identity);
        foreach (var cam in player.GetComponentsInChildren<Camera>(true)) { cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1000f; }

        TemplateCommon.SaveSceneToBuild(scene, ScenePath);
    }

    [MenuItem("Template/Enable Hand Tracking (OpenXR features)")]
    static void EnableHandTracking() {
        var m = typeof(HandTrackingSetup).GetMethod("EnableOpenXRHandTrackingFeatures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (m != null) m.Invoke(null, null);
    }

    /// <summary>A standalone scene has no use for passthrough / AR features (they add start-up cost and permission prompts on Quest).</summary>
    static void DisablePassthroughFeatures() {
        string[] mr = { "OpenXRCompositionLayersFeature", "ARSessionFeature", "ARCameraFeature", "ARPlaneFeature", "ARRaycastFeature" };
        foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android }) {
            var oxr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) continue;
            foreach (var f in oxr.GetFeatures()) {
                if (f == null || System.Array.IndexOf(mr, f.GetType().Name) < 0) continue;
                f.enabled = false; EditorUtility.SetDirty(f);
            }
            EditorUtility.SetDirty(oxr);
        }
        AssetDatabase.SaveAssets();
    }
}
