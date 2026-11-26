using System.Reflection;
using UnityEngine;
using Autohand;

/// <summary>
/// Authors fixed Auto Hand grab poses in code. Robot hand axes (hand-local): palm normal = -X (right hand) / +X (left hand),
/// fingers extend along +Z, thumb side is +Y. A rod grip is described by: P (rod point, grabbable-local), D (palm-to-rod direction,
/// grabbable-local, perpendicular to the rod) and T (thumb direction along the rod, grabbable-local).
/// </summary>
public static class RoboPoses {
    public struct Grip {
        public Vector3 P, D, T;
        public float rodDepth;       // distance along palm normal from hand origin to rod axis
        public float rodForward;     // distance along fingers (+Z) from hand origin to rod axis
        public float rodSide;        // offset along thumb direction (+Y)
        public float tilt;           // degrees: tilts the rod axis forward (toward the fingers) in hand space
        public float thumb, index, middle, ring, pinky;
    }

    public static Hand FindRobotHand(GameObject player, bool left) {
        foreach (var h in player.GetComponentsInChildren<Hand>(true))
            if (h.name.StartsWith("RobotHand") && h.left == left) return h;
        return null;
    }

    static void InitFingers(Hand hand) {
        var awake = typeof(Finger).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var f in hand.fingers) awake.Invoke(f, null);
    }

    /// <summary>Poses `hand` (an instance in a throwaway scene copy of the player) on `grabbable` and stores the pose on a GrabbablePose child.</summary>
    public static void Author(Grabbable grabbable, Hand hand, Grip g, string poseName = "pose") {
        InitFingers(hand);
        var tr = grabbable.transform;
        Vector3 T = g.T.normalized;
        Vector3 D = Vector3.ProjectOnPlane(g.D, T).normalized;
        Vector3 X = hand.left ? D : -D;                 // hand-local X axis in grabbable space
        Vector3 Z = Vector3.Cross(X, T).normalized;     // fingers
        // LookRotation(forward=Z, up=Y=T) gives right = Y x Z = X for a right-handed basis
        Quaternion localRot = Quaternion.AngleAxis(g.tilt, X) * Quaternion.LookRotation(Z, T);
        Vector3 palmNormalLocal = new Vector3(hand.left ? 1f : -1f, 0, 0);
        Vector3 offset = new Vector3(palmNormalLocal.x * g.rodDepth, g.rodSide, g.rodForward);
        Vector3 localPos = g.P - localRot * offset;

        hand.transform.SetPositionAndRotation(tr.TransformPoint(localPos), tr.rotation * localRot);
        Bend(hand, FingerEnum.thumb, g.thumb); Bend(hand, FingerEnum.index, g.index); Bend(hand, FingerEnum.middle, g.middle);
        Bend(hand, FingerEnum.ring, g.ring); Bend(hand, FingerEnum.pinky, g.pinky);
        foreach (var sk in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { sk.forceMatrixRecalculationPerRender = true; sk.updateWhenOffscreen = true; }

        var pose = grabbable.GetComponentInChildren<GrabbablePose>();
        if (pose == null) {
            var go = new GameObject("Pose " + poseName); go.transform.SetParent(tr, false);
            pose = go.AddComponent<GrabbablePose>();
        }
        pose.SaveHandPose(hand);
        if (hand.left) pose.leftPoseSet = true; else pose.rightPoseSet = true;
    }

    static void Bend(Hand hand, FingerEnum type, float amount) {
        foreach (var f in hand.fingers) if (f.fingerType == type) f.SetFingerBend(amount);
    }

    /// <summary>The XRPlayer prefab's hands have no pinchPointTransform (Auto Hand's Pinch grab pose needs one). Creates it between the thumb and index tips in the pinch pose.</summary>
    public static void EnsurePinchPoints(GameObject player) {
        foreach (var hand in player.GetComponentsInChildren<Hand>(true)) {
            if (!hand.name.StartsWith("RobotHand") || hand.pinchPointTransform != null || hand.palmTransform == null) continue;
            InitFingers(hand);
            var joints = new System.Collections.Generic.List<Transform>(); var rots = new System.Collections.Generic.List<Quaternion>();
            foreach (var f in hand.fingers) foreach (var j in new[] { f.knuckleJoint, f.middleJoint, f.distalJoint }) if (j != null) { joints.Add(j); rots.Add(j.localRotation); }
            Finger thumb = null, index = null;
            foreach (var f in hand.fingers) { if (f.fingerType == FingerEnum.thumb) thumb = f; if (f.fingerType == FingerEnum.index) index = f; }
            bool havePinch = thumb != null && thumb.poseData != null && thumb.poseData.Length > (int)FingerPoseEnum.PinchClosed && thumb.poseData[(int)FingerPoseEnum.PinchClosed].isSet
                          && index.poseData[(int)FingerPoseEnum.PinchClosed].isSet;
            if (havePinch) { thumb.SetFingerBend(1, FingerPoseEnum.Open, FingerPoseEnum.PinchClosed); index.SetFingerBend(1, FingerPoseEnum.Open, FingerPoseEnum.PinchClosed); }
            else { thumb.SetFingerBend(0.6f); index.SetFingerBend(0.5f); }
            Vector3 mid = (thumb.tip.position + index.tip.position) * 0.5f;
            var pp = new GameObject("PinchPoint").transform;
            pp.SetParent(hand.palmTransform.parent, true);
            pp.rotation = hand.palmTransform.rotation;
            pp.position = mid;
            hand.pinchPointTransform = pp;
            for (int i = 0; i < joints.Count; i++) joints[i].localRotation = rots[i];
            Debug.Log("RoboPoses: pinch point created for " + hand.name + (havePinch ? " (pinch poses)" : " (fallback bends)"));
        }
    }
}
