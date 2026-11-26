using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using Autohand;
using Autohand.Demo;
using Hand = Autohand.Hand;

/// <summary>
/// HandAlignPro - a tool for reporting misaligned hand positions. Click BOTH controller sticks to switch it on (and again to switch it off).
/// While on, both hands are tinted green and the hand that is holding something is nudged in small steps with the buttons, relative to
/// its grip on the item. Nothing is locked: the hand and item keep following the controller as normal, only the grip changes. A small panel in front of you shows the mode, axis and the total adjustment. When you switch it off a text file is written
/// with the adjustment so it can be applied in code.
///
/// Holding a GUN: the hand and the gun are moved together (aim line drawn), and the file reports the pair's offset from the tracked
/// controller and how far the aim direction moved. Firing is disabled while the tool is on.
///
/// Buttons while on:   RIGHT A = nudge +      RIGHT B = nudge -      (hold to repeat)      A and B together = reset the adjustment
///                     LEFT X = switch between MOVE (4 mm steps) and ROTATE (1.5 degree steps)
///                     axis (the hand's own axes):   X by default,  hold LEFT TRIGGER = Y,  hold LEFT Y = Z
/// Reports go to  [project]/HandAlignPro/  in the editor (otherwise the app's persistent data folder), plus HandAlignPro_latest.txt.
/// </summary>
public class HandAlignPro : MonoBehaviour {
    public static bool Active { get; private set; }
    public static HandAlignPro Instance { get; private set; }
    public static string LastReportPath { get; private set; }

    public float moveStep = 0.004f;       // metres per nudge
    public float rotStep = 1.5f;          // degrees per nudge
    public int injectNudge;               // tests (no headset): +1 / -1 = a press of A / B
    public int injectAxis;                // tests: axis to use when no buttons are available

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() {
        if (FindAnyObjectByType<HandAlignPro>() != null) return;
        var go = new GameObject("HandAlignPro"); go.AddComponent<HandAlignPro>(); DontDestroyOnLoad(go);
    }

    Hand hand; Grabbable item; bool gunMode;
    bool bothWas, aWas, bWas, xWas, resetDone; float lostT;
    float holdT, repeatT; int heldDir;
    bool rotateMode; int axis;
    // hand vs item: both are driven from the controller, with the hand's grip on the item adjustable
    Vector3 basePos, relPos, itemRelPos; Quaternion baseRot, relRot, itemRelRot; Transform followT; Rigidbody itemRb; bool itemWasKinematic, handWasKinematic, handHadMovement;
    // gun: proxy follow target with an offset from the tracked controller
    Transform origFollow, controllerFollow, proxy; Vector3 offPos; Quaternion offRot = Quaternion.identity; Transform muzzle; LineRenderer aim; Vector3 aimStartDir;
    TextMesh hud;
    readonly Dictionary<Renderer, Material[]> tinted = new Dictionary<Renderer, Material[]>();

    void Awake() { Instance = this; }

    void Update() {
        bool both = RoboInput.Pressed(true, CommonButton.primary2DAxisClick) && RoboInput.Pressed(false, CommonButton.primary2DAxisClick);
        if (both && !bothWas) Toggle();
        bothWas = both;
        if (Active) {                                                                                               // the item was put down: leave the tool
            bool held = hand != null && item != null && hand.GetHeldGrabbable() == item;
            lostT = held ? 0f : lostT + Time.deltaTime;
            if (lostT > 0.3f) { End(); return; }
        }
        if (!Active || hand == null || item == null) return;
        Buttons();
        Drive();
        UpdateHud();
        if (gunMode && aim != null && muzzle != null) { aim.SetPosition(0, muzzle.position); aim.SetPosition(1, muzzle.position + muzzle.forward * 6f); }
    }

    public static void ForceToggle() { if (Instance != null) Instance.Toggle(); }
    void Toggle() { if (Active) End(); else Begin(); }

    // ---------------------------------------------------------------------------------------------------------------------------

    void Begin() {
        hand = null;
        foreach (var h in FindObjectsByType<Hand>()) {
            if (!h.isActiveAndEnabled) continue;
            if (h.GetHeldGrabbable() == null) continue;
            if (hand == null || !h.left) hand = h;               // prefer the right hand if both hold something
        }
        if (hand == null) {
            Debug.Log("HandAlignPro: nothing is being held - grab an item first, then click both sticks.");
            foreach (var h in FindObjectsByType<Hand>()) if (h.isActiveAndEnabled) h.PlayHapticVibration(0.08f, 0.3f);
            return;
        }
        item = hand.GetHeldGrabbable();
        gunMode = item.GetComponentInChildren<GunMuzzle>() != null || item.GetComponentInChildren<Autohand.Demo.Pistol>() != null;
        Active = true; aWas = bWas = xWas = true; rotateMode = false; axis = 0; resetDone = false; heldDir = 0;
        TintHands(true);

        if (gunMode) {
            origFollow = hand.follow;
            controllerFollow = (origFollow.name == "grip offset" && origFollow.parent != null) ? origFollow.parent : origFollow;       // the tracked controller itself
            proxy = new GameObject("HandAlignPro Proxy").transform; proxy.SetPositionAndRotation(origFollow.position, origFollow.rotation);
            offPos = Vector3.zero; offRot = Quaternion.identity;
            hand.follow = proxy;
            var pg = item.GetComponentInChildren<GunMuzzle>(); var pistol = item.GetComponentInChildren<Autohand.Demo.Pistol>();
            muzzle = pg != null ? pg.muzzle : (pistol != null ? pistol.barrelTip : item.transform);
            var go = new GameObject("HandAlignPro Aim"); aim = go.AddComponent<LineRenderer>();
            aim.useWorldSpace = true; aim.positionCount = 2; aim.startWidth = aim.endWidth = 0.004f; aim.numCapVertices = 2;
            aim.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.3f, 1f, 0.4f, 0.9f) };
            aimStartDir = muzzle.forward;
        } else {
            followT = hand.follow;
            basePos = relPos = item.transform.InverseTransformPoint(hand.transform.position);
            baseRot = relRot = Quaternion.Inverse(item.transform.rotation) * hand.transform.rotation;
            itemRelPos = followT.InverseTransformPoint(item.transform.position); itemRelRot = Quaternion.Inverse(followT.rotation) * item.transform.rotation;
            itemRb = item.body;
            if (itemRb != null) { itemRb.linearVelocity = Vector3.zero; itemRb.angularVelocity = Vector3.zero; itemWasKinematic = itemRb.isKinematic; itemRb.isKinematic = true; }
            hand.body.linearVelocity = Vector3.zero; hand.body.angularVelocity = Vector3.zero;
            handWasKinematic = hand.body.isKinematic; hand.body.isKinematic = true;
            handHadMovement = hand.enableMovement; hand.enableMovement = false;
        }
        BuildHud();
        hand.PlayHapticVibration(0.12f, 0.6f);
        Debug.Log("HandAlignPro: ON  hand=" + (hand.left ? "left" : "right") + " item=" + item.name + " mode=" + (gunMode ? "gun (hand+gun together)" : "hand vs item"));
    }

    /// <summary>Gun mode: the offset found now stacks on the baked-in one; the TOTAL goes into GripOffsets.txt so it survives rebuilds and is applied at once.</summary>
    void SaveGripTotal() {
        if (!gunMode || hand == null || item == null || origFollow == null) return;
        if (offPos == Vector3.zero && Quaternion.Angle(offRot, Quaternion.identity) < 0.001f) return;
        Vector3 p0 = Vector3.zero; Quaternion q0 = Quaternion.identity;
        if (origFollow.name == "grip offset") { p0 = origFollow.localPosition; q0 = origFollow.localRotation; }
        Vector3 pT = p0 + q0 * offPos; Quaternion qT = q0 * offRot;
        if (hand.left) { pT.x = -pT.x; qT = new Quaternion(qT.x, -qT.y, -qT.z, qT.w); }            // stored as the right hand's
        RoboGripStore.Set(item.name, pT, qT);
        lastTotal = "TOTAL offset now saved for '" + item.name + "' (GripOffsets.txt, applied from now on): pos " + V(pT) + "  quat " + Q(qT);
    }
    string lastTotal;

    void End() {
        try { SaveGripTotal(); } catch (System.Exception e) { Debug.LogWarning("HandAlignPro: saving the grip failed: " + e.Message); }
        try { SaveReport("exit"); } catch (System.Exception e) { Debug.LogWarning("HandAlignPro: saving the report failed: " + e.Message); }
        try { TintHands(false); } catch (System.Exception e) { Debug.LogWarning("HandAlignPro: " + e.Message); }
        try {
            if (hand != null) {
                if (gunMode) {
                    // give the hand its normal follow target back (never a destroyed one)
                    if (proxy != null && hand.follow == proxy) hand.follow = origFollow != null ? origFollow : controllerFollow;
                } else {
                    hand.body.isKinematic = handWasKinematic; hand.enableMovement = handHadMovement;
                    if (itemRb != null) itemRb.isKinematic = itemWasKinematic;
                }
                hand.PlayHapticVibration(0.12f, 0.3f);
            }
        } catch (System.Exception e) { Debug.LogWarning("HandAlignPro: restoring the hand failed: " + e.Message); }
        if (proxy != null) Destroy(proxy.gameObject);
        if (aim != null) Destroy(aim.gameObject);
        if (hud != null) Destroy(hud.gameObject);
        Active = false; item = null; hand = null; origFollow = null; controllerFollow = null;      // always switched off, whatever went wrong above
        Debug.Log("HandAlignPro: OFF");
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    void FixedUpdate() { if (Active && hand != null && item != null) Drive(); }

    /// <summary>Hand-vs-item mode: the item rides on the controller exactly as it was held, and the hand sits on the item at the (adjustable) grip.</summary>
    void Drive() {
        if (gunMode || followT == null) return;
        Vector3 ip = followT.TransformPoint(itemRelPos); Quaternion ir = followT.rotation * itemRelRot;
        item.transform.SetPositionAndRotation(ip, ir); if (itemRb != null) { itemRb.position = ip; itemRb.rotation = ir; }
        Vector3 hp = ip + ir * relPos; Quaternion hr = ir * relRot;
        hand.transform.SetPositionAndRotation(hp, hr); hand.body.position = hp; hand.body.rotation = hr;
    }

    void Buttons() {
        bool a = RoboInput.Pressed(false, CommonButton.primaryButton), b = RoboInput.Pressed(false, CommonButton.secondaryButton);
        bool x = RoboInput.Pressed(true, CommonButton.primaryButton);
        // axis: X, hold left trigger = Y, hold left Y = Z
        axis = RoboInput.Pressed(true, CommonButton.secondaryButton) ? 2 : RoboInput.Pressed(true, CommonButton.triggerButton) ? 1 : injectAxis;
        if (x && !xWas) { rotateMode = !rotateMode; hand.PlayHapticVibration(0.04f, 0.4f); }
        xWas = x;

        int dir = 0;
        if (a && b) { if (!resetDone) { ResetAdjustment(); resetDone = true; } aWas = bWas = true; heldDir = 0; return; }
        resetDone = false;
        if (a) dir = 1; else if (b) dir = -1; else if (injectNudge != 0) { dir = injectNudge; injectNudge = 0; }
        bool fresh = (dir == 1 && !aWas) || (dir == -1 && !bWas) || (dir != 0 && !a && !b);
        aWas = a; bWas = b;
        if (dir == 0) { heldDir = 0; return; }
        if (fresh || heldDir != dir) { Nudge(dir); heldDir = dir; holdT = 0f; repeatT = 0f; return; }
        holdT += Time.deltaTime;
        if (holdT > 0.4f) { repeatT += Time.deltaTime; if (repeatT > 0.12f) { repeatT = 0f; Nudge(dir); } }       // hold to repeat
    }

    void Nudge(int dir) {
        Transform frame = gunMode ? proxy : hand.transform;
        Vector3 ax = axis == 0 ? frame.right : axis == 1 ? frame.up : frame.forward;
        Vector3 p; Quaternion q;
        if (gunMode) { p = proxy.position; q = proxy.rotation; } else { p = hand.transform.position; q = hand.transform.rotation; }
        if (rotateMode) q = Quaternion.AngleAxis(dir * rotStep, ax) * q; else p += ax * (dir * moveStep);
        if (gunMode) {
            offPos = origFollow.InverseTransformPoint(p); offRot = Quaternion.Inverse(origFollow.rotation) * q;
            proxy.SetPositionAndRotation(origFollow.TransformPoint(offPos), origFollow.rotation * offRot);
        } else {
            relPos = Quaternion.Inverse(item.transform.rotation) * (p - item.transform.position); relRot = Quaternion.Inverse(item.transform.rotation) * q;
            Drive();
        }
        hand.PlayHapticVibration(0.02f, 0.35f);
    }

    void ResetAdjustment() {
        if (gunMode) { offPos = Vector3.zero; offRot = Quaternion.identity; }
        else { relPos = basePos; relRot = baseRot; Drive(); }
        hand.PlayHapticVibration(0.06f, 0.5f);
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    void BuildHud() {
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null) return;
        var go = new GameObject("HandAlignPro HUD"); go.transform.SetParent(cam, false); go.transform.localPosition = new Vector3(0f, -0.17f, 0.6f);
        hud = go.AddComponent<TextMesh>(); hud.anchor = TextAnchor.UpperCenter; hud.alignment = TextAlignment.Center; hud.fontSize = 64; hud.characterSize = 0.0042f; hud.color = new Color(0.4f, 1f, 0.5f);
        var mr = go.GetComponent<MeshRenderer>();
        if (hud.font != null) { var m = new Material(hud.font.material); m.renderQueue = 5001; m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); mr.sharedMaterial = m; }
    }

    void UpdateHud() {
        if (hud == null) return;
        string ax = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
        string delta;
        if (gunMode) delta = "pos " + (offPos * 1000f).ToString("F0") + " mm   rot " + offRot.eulerAngles.ToString("F1");
        else {
            Vector3 cp = item.transform.InverseTransformPoint(hand.transform.position); Quaternion cr = Quaternion.Inverse(item.transform.rotation) * hand.transform.rotation;
            delta = "pos " + ((cp - basePos) * 1000f).ToString("F0") + " mm   rot " + Quaternion.Angle(baseRot, cr).ToString("F1") + " deg";
        }
        hud.text = "HandAlignPro  " + (gunMode ? "GUN + HAND" : "HAND") + "   [" + (rotateMode ? "ROTATE" : "MOVE") + "]  axis " + ax + "\n" +
                   "A +   B -   X move/rotate   A+B reset\nL-trigger = Y   L-Y = Z\n" + delta;
    }

    void TintHands(bool on) {
        if (!on) {
            foreach (var kv in tinted) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            tinted.Clear(); return;
        }
        var green = new Color(0.25f, 1f, 0.35f);
        foreach (var h in FindObjectsByType<Hand>()) {
            if (!h.isActiveAndEnabled) continue;
            foreach (var r in h.GetComponentsInChildren<Renderer>()) {
                if (r is ParticleSystemRenderer || r is LineRenderer || tinted.ContainsKey(r)) continue;
                var orig = r.sharedMaterials; tinted[r] = orig;
                var copy = new Material[orig.Length];
                for (int i = 0; i < orig.Length; i++) {
                    if (orig[i] == null) continue;
                    var m = new Material(orig[i]);
                    if (m.HasProperty("_Color")) m.color = Color.Lerp(m.color, green, 0.65f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), green, 0.65f));
                    if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", green * 0.25f); }
                    copy[i] = m;
                }
                r.sharedMaterials = copy;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    static string V(Vector3 v) { return "(" + v.x.ToString("F4") + "f, " + v.y.ToString("F4") + "f, " + v.z.ToString("F4") + "f)"; }
    static string Q(Quaternion q) { return "(" + q.x.ToString("F5") + "f, " + q.y.ToString("F5") + "f, " + q.z.ToString("F5") + "f, " + q.w.ToString("F5") + "f)"; }
    static string E(Quaternion q) { return V(q.eulerAngles); }
    static string PathOf(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

    public void SaveReport(string why) {
        if (hand == null || item == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("HandAlignPro report (" + why + ")");
        sb.AppendLine("time:   " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("scene:  " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        sb.AppendLine("hand:   " + (hand.left ? "LEFT" : "RIGHT") + "   (" + PathOf(hand.transform) + ")");
        sb.AppendLine("item:   " + item.name + "   (" + PathOf(item.transform) + ")");
        sb.AppendLine("mode:   " + (gunMode ? "GUN - hand and gun adjusted together against the tracked controller" : "HAND vs ITEM - item fixed, hand adjusted"));
        sb.AppendLine();
        if (gunMode) {
            sb.AppendLine("Offset of hand+gun from the tracked controller (hand.follow space):");
            sb.AppendLine("  position  " + V(offPos) + "   (metres)");
            sb.AppendLine("  rotation  euler " + E(offRot) + "   quaternion " + Q(offRot));
            if (muzzle != null && origFollow != null) {
                sb.AppendLine("Aim:");
                sb.AppendLine("  muzzle direction before " + V(aimStartDir) + "  after " + V(muzzle.forward) + "  moved " + Vector3.Angle(aimStartDir, muzzle.forward).ToString("F2") + " degrees");
                sb.AppendLine("  muzzle direction in controller space: " + V(Quaternion.Inverse(origFollow.rotation) * muzzle.forward));
                sb.AppendLine("  muzzle local pose on the gun: pos " + V(muzzle.localPosition) + " euler " + E(muzzle.localRotation));
            }
            Vector3 hp = item.transform.InverseTransformPoint(hand.transform.position);
            sb.AppendLine("Hand in gun space (unchanged by this mode): pos " + V(hp) + "  euler " + E(Quaternion.Inverse(item.transform.rotation) * hand.transform.rotation));
            sb.AppendLine();
            sb.AppendLine(lastTotal != null ? lastTotal : "(offset unchanged - nothing saved)");
            sb.AppendLine("The offset above is relative to the grip already in use; the TOTAL line is the absolute value, stored automatically.");
        } else {
            Vector3 curPos = item.transform.InverseTransformPoint(hand.transform.position);
            Quaternion curRot = Quaternion.Inverse(item.transform.rotation) * hand.transform.rotation;
            sb.AppendLine("Hand origin in ITEM space (item fixed):");
            sb.AppendLine("  before    pos " + V(basePos) + "  euler " + E(baseRot) + "  quat " + Q(baseRot));
            sb.AppendLine("  after     pos " + V(curPos) + "  euler " + E(curRot) + "  quat " + Q(curRot));
            sb.AppendLine("Change:");
            sb.AppendLine("  position, item space  " + V(curPos - basePos) + "   hand space " + V(Quaternion.Inverse(baseRot) * (curPos - basePos)));
            Quaternion dRot = curRot * Quaternion.Inverse(baseRot);
            sb.AppendLine("  rotation, item space  euler " + E(dRot) + "  angle " + Quaternion.Angle(baseRot, curRot).ToString("F2") + " degrees");
            if (hand.palmTransform != null) {
                Vector3 pp = item.transform.InverseTransformPoint(hand.palmTransform.position);
                sb.AppendLine("Palm in item space (after): pos " + V(pp) + "  facing " + V(item.transform.InverseTransformDirection(hand.palmTransform.forward)));
            }
            sb.AppendLine("Item has GrabbablePose: " + (item.GetComponentInChildren<GrabbablePose>() != null) + "   RoboHandlePose: " + (item.GetComponentInChildren<RoboHandlePose>() != null));
            sb.AppendLine();
            sb.AppendLine("To apply: the fixed grip should place the hand origin at the 'after' pose in item space (or shift the existing pose by the change above).");
        }
        string dir;
#if UNITY_EDITOR
        dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HandAlignPro");
#else
        dir = Path.Combine(Application.persistentDataPath, "HandAlignPro");
#endif
        try {
            Directory.CreateDirectory(dir);
            string name = "HandAlign_" + item.name.Replace(' ', '_') + "_" + (hand.left ? "L" : "R") + "_" + System.DateTime.Now.ToString("HHmmss") + ".txt";
            File.WriteAllText(Path.Combine(dir, name), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "HandAlignPro_latest.txt"), sb.ToString());
            LastReportPath = Path.Combine(dir, name);
            Debug.Log("HandAlignPro: report written to " + LastReportPath);
            hand.PlayHapticVibration(0.05f, 0.9f);
        } catch (System.Exception e) { Debug.LogWarning("HandAlignPro: could not write report: " + e.Message); }
    }
}
