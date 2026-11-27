using Autohand;
using Makeshift.Engine;
using Makeshift.Machines;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Makeshift.Integration
{
    /// <summary>
    /// Connects the Makeshift systems to Auto Hand: what is held, vines and glue, and machine
    /// parts that snap into sockets only after the player lets go of them inside the socket.
    /// A scene with an Auto Hand player and no bridge gets one when it loads.
    ///
    /// Uses Grabbable.GetHeldBy(), which Auto Hand documents. If your modified version
    /// changed it, edit the one line below.
    /// </summary>
    public class AutoHandBridge : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void WatchScenes()
        {
            SceneManager.sceneLoaded -= AddIfMissing;
            SceneManager.sceneLoaded += AddIfMissing;
        }

        // Without the bridge nothing counts as held, so a vine never attaches and no error says why.
        private static void AddIfMissing(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<AutoHandPlayer>() == null || FindAnyObjectByType<AutoHandBridge>() != null) return;
            new GameObject("MakeshiftBridge").AddComponent<AutoHandBridge>();
            Debug.Log($"[Makeshift] added the missing hand bridge to {scene.name}");
        }

        private void Awake()
        {
            PartSocket.IsHeld = IsHeldByAutoHand;
            Makeshift.Creatures.ScavengerCreature.IsHeld = IsHeldByAutoHand;
            Makeshift.Core.HandCheck.IsHeld = IsHeldByAutoHand;
            Makeshift.Core.HandCheck.IsPlayerBody = IsPlayerBody;
            Makeshift.Core.HandCheck.ForceRelease = ForceRelease;
            Makeshift.Core.HandCheck.HoldPoint = HoldPoint;
            PhysicsBubble.IsExempt = BubbleExempt;
            PhysicsBubble.IsHeld = IsHeldByAutoHand;
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
            if (PhysicsBubble.IsExempt == BubbleExempt) PhysicsBubble.IsExempt = null;
            if (PhysicsBubble.IsHeld == IsHeldByAutoHand) PhysicsBubble.IsHeld = null;
        }

        /// <summary>
        /// Never frozen by the physics bubble: hands and the head, anything a player holds, and anything networked
        /// (its state belongs to the network, so the bubble must not touch it).
        /// </summary>
        private static bool BubbleExempt(Rigidbody body)
        {
            return IsPlayerBody(body) || IsHeldByAutoHand(body) || body.GetComponentInParent<Fusion.NetworkObject>() != null;
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
