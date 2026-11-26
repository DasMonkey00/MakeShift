using Autohand;
using UnityEngine;

namespace Makeshift.Network
{
    /// <summary>
    /// Marks the local, never-networked Auto Hand rig and exposes the three transforms the
    /// network rig copies: head, left hand, right hand. Put on the AutoHandPlayer object.
    /// The hardware rig is never spawned over the network and never gets a NetworkObject.
    /// </summary>
    public class HardwareRigLink : MonoBehaviour
    {
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;

        public static HardwareRigLink Local { get; private set; }

        private void Awake()
        {
            var player = GetComponent<AutoHandPlayer>();
            if (player != null)
            {
                if (head == null && player.headCamera != null) head = player.headCamera.transform;
                if (leftHand == null && player.handLeft != null) leftHand = player.handLeft.transform;
                if (rightHand == null && player.handRight != null) rightHand = player.handRight.transform;
            }
            Local = this;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }
    }
}
