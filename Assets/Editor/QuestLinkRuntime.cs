using UnityEditor;
using UnityEngine;

/// <summary>
/// Template menu: choose the Meta Quest Link OpenXR runtime for Play Mode (persisted across editor restarts).
/// Needed for passthrough, and handy for plain standalone VR testing over Link / Air Link.
/// </summary>
[InitializeOnLoad]
public static class QuestLinkRuntime {
    // ---- Play Mode OpenXR runtime ------------------------------------------------------------------------------------------------
    // Passthrough needs a runtime that offers XR_FB_passthrough. Virtual Desktop's OpenXR runtime does not (Meta Quest Link's does), and Unity's
    // "Play Mode OpenXR Runtime" choice resets to System Default every time the editor starts, so remember it here.
    const string PrefUseLink = "Template.UseMetaQuestLinkRuntime";
    const string SelectedRuntimeEnv = "XR_SELECTED_RUNTIME_JSON";     // what Unity's OpenXR runtime dropdown reads

    static string MetaLinkRuntimeJson() {
#if UNITY_EDITOR_WIN
        try {
            var dir = (string)Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Oculus", "InstallLocation", "");
            if (string.IsNullOrEmpty(dir)) return "";
            var p = System.IO.Path.Combine(dir, @"Support\oculus-runtime\oculus_openxr_64.json");   // same path Unity's own selector builds
            return System.IO.File.Exists(p) ? p : "";
        } catch { return ""; }
#else
        return "";
#endif
    }

    [InitializeOnLoadMethod]
    static void ApplyRuntimePreference() {
        if (!EditorPrefs.GetBool(PrefUseLink, false)) return;
        var json = MetaLinkRuntimeJson();
        if (json == "") { Debug.LogWarning("QuestLinkRuntime: Meta Quest Link (Oculus) runtime not found - install the Meta Quest Link app."); return; }
        System.Environment.SetEnvironmentVariable(SelectedRuntimeEnv, json);
    }

    [MenuItem("Template/Use Meta Quest Link Runtime in Play Mode")]
    static void ToggleLinkRuntime() {
        bool on = !EditorPrefs.GetBool(PrefUseLink, false);
        EditorPrefs.SetBool(PrefUseLink, on);
        if (on) {
            ApplyRuntimePreference();
            Debug.Log("QuestLinkRuntime: Play Mode will use the Meta Quest Link OpenXR runtime (Quest must be connected through Quest Link / Air Link, not Virtual Desktop).");
        } else {
            System.Environment.SetEnvironmentVariable(SelectedRuntimeEnv, "");
            Debug.Log("QuestLinkRuntime: Play Mode back to the System Default OpenXR runtime.");
        }
    }

    [MenuItem("Template/Use Meta Quest Link Runtime in Play Mode", true)]
    static bool ToggleLinkRuntimeValidate() {
        Menu.SetChecked("Template/Use Meta Quest Link Runtime in Play Mode", EditorPrefs.GetBool(PrefUseLink, false));
        return true;
    }
}
