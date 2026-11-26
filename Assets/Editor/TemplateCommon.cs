using System.IO;
using UnityEditor;
using UnityEngine;
using Autohand;

/// <summary>
/// Shared setup used by both template scene builders: physics tuned for Auto Hand, OpenXR (Meta Quest) loader setup,
/// the XRPlayer rig with the grip helpers wired in, and a few grabbable demo props.
/// </summary>
public static class TemplateCommon {
    public const string XRPlayerPrefab = "Assets/AutoHand/Examples/Scenes/XR/Prefabs/XRPlayer.prefab";

    /// <summary>Layers + collision matrix for Auto Hand, and the physics values these scenes were tuned with (72 Hz, high solver iterations).</summary>
    public static void ApplyPhysics() {
        AutoHand.AutoHandSetupWizard.SetRequiredSettings();            // Grabbing / Grabbable / Hand / HandPlayer layers + collision matrix
        Physics.gravity = new Vector3(0, -9.81f, 0);
        Time.fixedDeltaTime = 1f / 72f;
        Physics.defaultSolverIterations = 20;
        Physics.defaultSolverVelocityIterations = 10;
        Physics.defaultMaxAngularSpeed = 35f;
    }

    /// <summary>OpenXR loader on Standalone (PC / Link) and Android (Quest), Touch controller profiles on.</summary>
    public static void SetupXR() {
        try {
            UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget perTarget;
            EditorBuildSettings.TryGetConfigObject(UnityEngine.XR.Management.XRGeneralSettings.settingsKey, out perTarget);
            if (perTarget == null) {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(UnityEngine.XR.Management.XRGeneralSettings.settingsKey, perTarget, true);
            }
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android }) {
                if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
                var general = perTarget.SettingsForBuildTarget(group);
                general.InitManagerOnStart = true;
                var manager = perTarget.ManagerSettingsForBuildTarget(group);
                UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
                EditorUtility.SetDirty(general); EditorUtility.SetDirty(manager);
            }
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
        } catch (System.Exception e) { Debug.LogWarning("TemplateCommon: XR management setup failed: " + e); }
        try {
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android }) {
                var oxr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (oxr == null) continue;
                foreach (var f in oxr.GetFeatures()) {
                    if (f == null) continue;
                    string n = f.GetType().Name;
                    if (n.Contains("OculusTouchControllerProfile") || n.Contains("MetaQuestTouchProControllerProfile") || n.Contains("MetaQuestTouchPlusControllerProfile")) { f.enabled = true; EditorUtility.SetDirty(f); }
                }
                EditorUtility.SetDirty(oxr);
            }
        } catch (System.Exception e) { Debug.LogWarning("TemplateCommon: OpenXR feature setup failed: " + e); }
    }

    /// <summary>Instantiates the XRPlayer and adds the grip helpers: pinch points, finger assist, crouch (left stick click), floor tracking origin.</summary>
    public static GameObject AddPlayer(Vector3 position, Quaternion rotation) {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(XRPlayerPrefab);
        if (asset == null) { Debug.LogError("TemplateCommon: missing " + XRPlayerPrefab); return null; }
        var player = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        player.name = "XRPlayer";
        player.transform.SetPositionAndRotation(position, rotation);

        RoboPoses.EnsurePinchPoints(player);
        foreach (bool left in new[] { false, true }) {
            var hand = RoboPoses.FindRobotHand(player, left);
            if (hand != null) hand.gameObject.AddComponent<RoboFingerAssist>();     // finishes the thumb after a physics grab
        }
        var ahp = player.GetComponentInChildren<AutoHandPlayer>(true);
        if (ahp != null) ahp.gameObject.AddComponent<RoboCrouch>().player = ahp;
        var headTracking = player.AddComponent<Autohand.Demo.XRHeadTracking>();
        headTracking.mode = UnityEngine.XR.TrackingOriginModeFlags.Floor;
        return player;
    }

    /// <summary>A grabbable rigidbody prop with the settings we ended up using (interpolation, continuous dynamic, light throw).</summary>
    public static GameObject MakeGrabbable(string name, PrimitiveType shape, Vector3 position, Vector3 scale, float mass, Color color) {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.position = position; go.transform.localScale = scale;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { color = color, name = name + "_mat" };
        go.GetComponent<Renderer>().sharedMaterial = mat;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass; rb.linearDamping = 0.05f; rb.angularDamping = 0.8f;
        rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var g = go.AddComponent<Grabbable>();
        g.throwPower = 1.2f; g.ignoreReleaseTime = 0.3f;
        int layer = LayerMask.NameToLayer("Grabbable"); if (layer >= 0) go.layer = layer;
        return go;
    }

    public static void SaveSceneToBuild(UnityEngine.SceneManagement.Scene scene, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Template: scene saved to " + path);
    }
}
