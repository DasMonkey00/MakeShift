using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Autohand;

/// <summary>
/// The player root is built at a fixed spawn point, but the headset starts wherever you were standing in your tracking space (it differs between
/// Air Link, Virtual Desktop and the standalone Quest build), so you could start outside the platform or inside a wall. Once the headset is tracking,
/// this moves the tracking space so the headset sits exactly on the spawn point (Auto Hand's own Recenter).
/// It is added automatically to any scene with an Auto Hand player, except the MR scene (MrSceneMarker): there the real room's planes live in the
/// tracking space, so the player has to stay where they physically are.
/// </summary>
public class RoboSpawnRecenter : MonoBehaviour {
    public AutoHandPlayer player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() {
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        AddTo();
    }
    static void OnSceneLoaded(Scene s, LoadSceneMode m) { AddTo(); }

    static void AddTo() {
        if (FindAnyObjectByType<MrSceneMarker>() != null) return;                       // MR: never move the player off their real position
        var p = FindAnyObjectByType<AutoHandPlayer>();
        if (p == null || p.GetComponent<RoboSpawnRecenter>() != null) return;
        p.gameObject.AddComponent<RoboSpawnRecenter>().player = p;
    }

    IEnumerator Start() {
        if (player == null) player = GetComponent<AutoHandPlayer>();
        if (player == null) yield break;
        // wait for the headset to report a pose (up to 6 s), then let it settle a moment
        float t = 0f;
        while (t < 6f && !InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid) { t += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(0.6f);
        Recenter("first");
        yield return new WaitForSeconds(1.2f);     // tracking can still shift slightly right after it starts
        Recenter("second");
    }

    void Recenter(string which) {
        if (player == null || player.headCamera == null || player.trackingContainer == null) return;
        Vector3 before = player.headCamera.transform.position;
        player.Recenter();
        Debug.Log("RoboSpawnRecenter (" + which + "): head " + before.ToString("F2") + " -> " + player.headCamera.transform.position.ToString("F2") + ", spawn " + player.transform.position.ToString("F2"));
    }
}
