using Autohand;
using Makeshift.Machines;
using UnityEngine;

namespace Makeshift.Integration
{
    /// <summary>
    /// Lets a player pull a part back out of a socket. When a hand grabs a socketed part,
    /// the socket lets go of it. AutoHandBridge adds this to every part in the scene at start;
    /// add it yourself to parts spawned later (crafting).
    /// </summary>
    [RequireComponent(typeof(MachinePart), typeof(Grabbable))]
    public class AutoHandPartHook : MonoBehaviour
    {
        private MachinePart part;
        private Grabbable grabbable;

        private void Awake()
        {
            part = GetComponent<MachinePart>();
            grabbable = GetComponent<Grabbable>();
            grabbable.OnGrabEvent += OnGrab;
        }

        private void OnDestroy()
        {
            if (grabbable != null) grabbable.OnGrabEvent -= OnGrab;
        }

        private void OnGrab(Hand hand, Grabbable g)
        {
            if (part.IsSocketed) part.Socket.Detach();
        }
    }
}
