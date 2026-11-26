#if FUSION_WEAVER
using Fusion;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// The visual-only copy of a player that everyone else sees: head and two hands, no Auto Hand
    /// physics, no joints, no rigidbodies. The owner copies poses in from the HardwareRigLink;
    /// proxies interpolate and show them. The owner hides their own copy.
    /// Running Auto Hand on remote copies makes physics fight network sync, so we never do it.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkRig : NetworkBehaviour
    {
        [SerializeField] private Transform headVisual;
        [SerializeField] private Transform leftHandVisual;
        [SerializeField] private Transform rightHandVisual;

        [Networked] private Vector3 HeadPos { get; set; }
        [Networked] private Quaternion HeadRot { get; set; }
        [Networked] private Vector3 LeftPos { get; set; }
        [Networked] private Quaternion LeftRot { get; set; }
        [Networked] private Vector3 RightPos { get; set; }
        [Networked] private Quaternion RightRot { get; set; }

        public Transform HeadVisual => headVisual;

        public override void Spawned()
        {
            // The owner already sees their own rig; hide the copy so it is never in their face.
            if (Object.HasStateAuthority)
                foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            var rig = HardwareRigLink.Local;
            if (rig == null) return;
            if (rig.head != null) { HeadPos = rig.head.position; HeadRot = rig.head.rotation; }
            if (rig.leftHand != null) { LeftPos = rig.leftHand.position; LeftRot = rig.leftHand.rotation; }
            if (rig.rightHand != null) { RightPos = rig.rightHand.position; RightRot = rig.rightHand.rotation; }
        }

        public override void Render()
        {
            if (Object.HasStateAuthority) return;   // proxies only; networked values interpolate
            Apply(headVisual, HeadPos, HeadRot);
            Apply(leftHandVisual, LeftPos, LeftRot);
            Apply(rightHandVisual, RightPos, RightRot);
        }

        private static void Apply(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t != null) t.SetPositionAndRotation(pos, rot);
        }
    }
}
#endif
