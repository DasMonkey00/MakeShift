using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Autohand;

/// <summary>
/// Auto Hand's OpenXR hand tracking (Examples/Scenes/OpenXR): the "Right/Left Hand Tracking" prefabs read the headset's tracked
/// hands through Unity XR Hands and drive the Auto Hand robot hands (grab by pinching/closing the hand, release by opening it).
/// Controllers keep working: the tracker switches to hand tracking only while the hand is actually tracked.
/// </summary>
public static partial class HandTrackingSetup {
    const string HandTrackingDir = "Assets/AutoHand/Examples/Scenes/OpenXR/Prefabs/Prefabs/";

    static void EnableOpenXRHandTrackingFeatures() {
        try {
            var oxr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (oxr == null) return;
            foreach (var f in oxr.GetFeatures()) {
                string n = f.GetType().Name;
                if ((n.Contains("HandTracking") || n.Contains("HandInteraction") || n.Contains("MetaHand")) && !n.Contains("Microsoft")) {
                    f.enabled = true; EditorUtility.SetDirty(f);
                    Debug.Log("HandTrackingSetup: enabled OpenXR feature " + f.GetType().FullName);
                }
            }
            EditorUtility.SetDirty(oxr);
        } catch (System.Exception e) { Debug.LogWarning("OpenXR hand tracking feature setup failed: " + e); }
    }

    /// <summary>Hand tracking is switched off for now: make sure the OpenXR hand features are not enabled.</summary>
    internal static void DisableOpenXRHandTrackingFeatures() {
        try {
            var oxr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (oxr == null) return;
            foreach (var f in oxr.GetFeatures()) {
                if (f == null) continue;
                string n = f.GetType().Name;
                if (n.Contains("HandTracking") || n.Contains("HandInteraction") || n.Contains("MetaHand")) { f.enabled = false; EditorUtility.SetDirty(f); }
            }
            EditorUtility.SetDirty(oxr);
        } catch (System.Exception e) { Debug.LogWarning("OpenXR hand tracking feature disable failed: " + e); }
    }

    static void AddHandTracking(GameObject player) {
        foreach (bool left in new[] { false, true }) {
            var hand = RoboPoses.FindRobotHand(player, left);
            if (hand == null) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandTrackingDir + (left ? "Left" : "Right") + " Hand Tracking.prefab");
            if (prefab == null) { Debug.LogWarning("HandTrackingSetup: hand tracking prefab missing for " + (left ? "left" : "right")); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hand.transform.parent);
            inst.name = (left ? "Left" : "Right") + " Hand Tracking";
            var tracker = inst.GetComponent<OpenXRAutoHandTracking>();
            if (tracker == null) { Debug.LogWarning("HandTrackingSetup: no OpenXRAutoHandTracking on " + inst.name); continue; }
            tracker.hand = hand;
            foreach (var link in hand.GetComponentsInChildren<HandControllerLink>(true)) { tracker.controllerLink = link; break; }
            if (tracker.controllerLink == null) foreach (var link in player.GetComponentsInChildren<HandControllerLink>(true)) if (link.hand == hand) { tracker.controllerLink = link; break; }
            Debug.Log("HandTrackingSetup: hand tracking added for " + hand.name + " (controller link: " + (tracker.controllerLink != null ? tracker.controllerLink.name : "none") + ")");
        }
    }
}
