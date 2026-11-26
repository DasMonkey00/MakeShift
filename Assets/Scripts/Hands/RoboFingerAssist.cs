using System.Collections;
using UnityEngine;
using Autohand;

/// <summary>
/// Finishes the finger pose after Auto Hand's own physics grab. The hand is placed and posed exactly as Auto Hand does it (no
/// snapping to authored poses). Afterwards:
///   Every grab: the thumb squeezes (closes onto the object). If the thumb can close 50% or more it is kept closed; if the object
///               blocks it before that, Auto Hand's own thumb pose (thumb up) is kept.
///   Pinch hint: thumb and index close on the small item until they touch it; the other three fingers curl out of the way.
/// The result is written into the held pose so Auto Hand keeps it while the object is held.
/// </summary>
[RequireComponent(typeof(Hand))]
public class RoboFingerAssist : MonoBehaviour {
    public float settleDelay = 0.2f;
    public float contactMargin = 0.004f;
    public float maxThumbBend = 0.85f;
    public float minThumbSqueeze = 0.5f;
    Hand hand;

    void OnEnable() { hand = GetComponent<Hand>(); hand.OnGrabbed += OnGrabbed; }
    void OnDisable() { if (hand != null) hand.OnGrabbed -= OnGrabbed; }

    void OnGrabbed(Hand h, Grabbable grab) {
        if (grab == null) return;
        StartCoroutine(Assist(grab, grab.GetComponent<RoboGripHint>()));
    }

    IEnumerator Assist(Grabbable grab, RoboGripHint hint) {
        yield return new WaitForSeconds(settleDelay);
        if (hand.holdingObj != grab) yield break;                    // already let go / thrown
        var hp = grab.GetComponent<RoboHandlePose>(); if (hp != null && hp.poseActive) yield break;   // fixed handle grip: leave it alone
        var colliders = grab.GetComponentsInChildren<Collider>();

        if (hint != null && hint.mode == RoboGripHint.Mode.Pinch) {
            foreach (var t in new[] { FingerEnum.middle, FingerEnum.ring, FingerEnum.pinky }) Curl(Find(t), 0.85f);
            CloseUntilContact(Find(FingerEnum.thumb), colliders);
            CloseUntilContact(Find(FingerEnum.index), colliders);
        } else {
            SqueezeThumb(Find(FingerEnum.thumb), colliders);
        }
    }

    Finger Find(FingerEnum type) {
        foreach (var f in hand.fingers) if (f.fingerType == type) return f;
        return null;
    }

    float Distance(Finger f, Collider[] colliders) {
        Vector3 tip = f.tip.position; float best = float.MaxValue;
        foreach (var c in colliders) {
            if (c == null || c.isTrigger || !c.enabled) continue;
            try { best = Mathf.Min(best, Vector3.Distance(Physics.ClosestPoint(tip, c, c.transform.position, c.transform.rotation), tip)); } catch { }
        }
        return best;
    }

    /// <summary>Thumb squeezes by default; only stays up if the object stops it before it is half closed.</summary>
    void SqueezeThumb(Finger f, Collider[] colliders) {
        if (f == null) return;
        var joints = new[] { f.knuckleJoint, f.middleJoint, f.distalJoint };
        var saved = new Quaternion[joints.Length];
        for (int i = 0; i < joints.Length; i++) saved[i] = joints[i].localRotation;

        float press = f.tipRadius * 0.7f, chosen = maxThumbBend;
        for (float b = 0f; b <= maxThumbBend + 0.001f; b += 0.02f) {
            f.SetFingerBend(b);
            if (Distance(f, colliders) < press) { chosen = b; break; }
        }
        if (chosen < minThumbSqueeze) {                               // blocked early: keep Auto Hand's thumb pose
            for (int i = 0; i < joints.Length; i++) joints[i].localRotation = saved[i];
            return;
        }
        f.SetFingerBend(chosen);
        StorePose(f);
    }

    bool TouchesAny(Finger f, Collider[] colliders) {
        return Distance(f, colliders) <= f.tipRadius + contactMargin;
    }

    /// <summary>Closes a finger from open until its tip touches the object, then stores that as the held pose.</summary>
    void CloseUntilContact(Finger f, Collider[] colliders) {
        if (f == null) return;
        float found = -1f;
        for (float b = 0f; b <= 1.001f; b += 0.03f) {
            f.SetFingerBend(b);
            if (TouchesAny(f, colliders)) { found = b; break; }
        }
        if (found < 0f) { RestoreHeld(f); return; }                  // never reached the object: keep Auto Hand's pose
        f.SetFingerBend(found);
        StorePose(f);
    }

    void Curl(Finger f, float bend) {
        if (f == null) return;
        f.SetFingerBend(bend);
        StorePose(f);
    }

    void StorePose(Finger f) {
        int i = (int)f.fingerType;
        var anim = hand.handAnimator;
        anim.targetGrabPose.fingerPoses[i].SetPoseData(hand, f);
        anim.currentTargetPose.fingerPoses[i].SetPoseData(hand, f);
    }

    void RestoreHeld(Finger f) {
        int i = (int)f.fingerType;
        hand.handAnimator.targetGrabPose.fingerPoses[i].SetFingerPose(f);
    }
}
