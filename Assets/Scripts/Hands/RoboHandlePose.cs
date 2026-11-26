using UnityEngine;
using Autohand;

/// <summary>
/// Fixed closed-fist grip for a tool's handle: the authored GrabbablePose is used only when the hand is closer to the handle collider
/// than to any other collider of the tool (the same "closest to the palm" rule Auto Hand uses to choose the grab point). Grabbing the
/// head (or anything else) falls back to Auto Hand's normal physics auto-pose.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class RoboHandlePose : MonoBehaviour {
    public Collider[] handleColliders;
    public GrabbablePose[] poses;
    public float tolerance = 0.01f;
    public bool poseActive { get; private set; }
    Grabbable grabbable;

    void Awake() {
        grabbable = GetComponent<Grabbable>();
        grabbable.OnBeforeGrabEvent += OnBeforeGrab;
    }

    void OnDestroy() { if (grabbable != null) grabbable.OnBeforeGrabEvent -= OnBeforeGrab; }

    void OnBeforeGrab(Hand hand, Grabbable grab) {
        poseActive = NearHandle(hand.palmTransform.position);
        foreach (var p in poses) if (p != null) p.poseEnabled = poseActive;
    }

    bool NearHandle(Vector3 point) {
        float dHandle = float.MaxValue, dOther = float.MaxValue;
        foreach (var c in GetComponentsInChildren<Collider>()) {
            if (c.isTrigger || !c.enabled) continue;
            Vector3 p;
            try { p = Physics.ClosestPoint(point, c, c.transform.position, c.transform.rotation); } catch { continue; }
            float d = Vector3.Distance(p, point);
            if (System.Array.IndexOf(handleColliders, c) >= 0) dHandle = Mathf.Min(dHandle, d); else dOther = Mathf.Min(dOther, d);
        }
        return dHandle <= dOther + tolerance;
    }
}
