using Autohand;
using Makeshift.Machines;
using UnityEngine;

namespace Makeshift.Integration
{
    /// <summary>
    /// Connects the Makeshift systems to Auto Hand.
    /// Put one in the first scene. Machine parts then snap into sockets only after
    /// the player lets go of them inside the socket.
    ///
    /// Uses Grabbable.GetHeldBy(), which Auto Hand documents. If your modified version
    /// changed it, edit the one line below.
    /// </summary>
    public class AutoHandBridge : MonoBehaviour
    {
        private void Awake()
        {
            PartSocket.IsHeld = IsHeldByAutoHand;
            Makeshift.Creatures.ScavengerCreature.IsHeld = IsHeldByAutoHand;
            Makeshift.Core.HandCheck.IsHeld = IsHeldByAutoHand;
            Makeshift.Core.HandCheck.IsPlayerBody = IsPlayerBody;
            Makeshift.Core.HandCheck.ForceRelease = ForceRelease;
            Makeshift.Core.HandCheck.HoldPoint = HoldPoint;
        }

        private void Start()
        {
            foreach (var part in FindObjectsByType<MachinePart>())
            {
                if (part.GetComponent<Grabbable>() != null && part.GetComponent<AutoHandPartHook>() == null)
                    part.gameObject.AddComponent<AutoHandPartHook>();
            }
            foreach (var glider in FindObjectsByType<Makeshift.Movement.Glider>())
            {
                if (glider.GetComponent<Grabbable>() != null && glider.GetComponent<AutoHandGliderHook>() == null)
                    glider.gameObject.AddComponent<AutoHandGliderHook>();
            }
        }

        private void OnDestroy()
        {
            if (PartSocket.IsHeld == IsHeldByAutoHand) PartSocket.IsHeld = null;
            if (Makeshift.Core.HandCheck.IsHeld == IsHeldByAutoHand) Makeshift.Core.HandCheck.IsHeld = null;
            if (Makeshift.Core.HandCheck.IsPlayerBody == IsPlayerBody) Makeshift.Core.HandCheck.IsPlayerBody = null;
            if (Makeshift.Core.HandCheck.ForceRelease == ForceRelease) Makeshift.Core.HandCheck.ForceRelease = null;
            if (Makeshift.Core.HandCheck.HoldPoint == HoldPoint) Makeshift.Core.HandCheck.HoldPoint = null;
            if (Makeshift.Creatures.ScavengerCreature.IsHeld == IsHeldByAutoHand)
                Makeshift.Creatures.ScavengerCreature.IsHeld = null;
        }

        /// <summary>Hands and head only. Held props are parented under the hand, so this checks the body itself, not its parents.</summary>
        private static bool IsPlayerBody(Rigidbody body)
        {
            return body != null && (body.GetComponent<Hand>() != null
                || body.GetComponent<AutoHandPlayer>() != null
                || body.GetComponent<HeadPhysicsFollower>() != null);
        }

        private static void ForceRelease(Rigidbody body)
        {
            var grabbable = body != null ? body.GetComponent<Grabbable>() : null;
            if (grabbable != null && grabbable.GetHeldBy().Count > 0) grabbable.ForceHandsRelease();
        }

        private static Vector3? HoldPoint(Rigidbody body)
        {
            var grabbable = body != null ? body.GetComponent<Grabbable>() : null;
            if (grabbable == null || grabbable.GetHeldBy().Count == 0) return null;
            var hand = grabbable.GetHeldBy()[0];
            return hand.palmTransform != null ? hand.palmTransform.position : hand.transform.position;
        }

        private static bool IsHeldByAutoHand(Rigidbody body)
        {
            if (body == null) return false;
            var grabbable = body.GetComponent<Grabbable>();
            return grabbable != null && grabbable.GetHeldBy().Count > 0;
        }
    }
}
