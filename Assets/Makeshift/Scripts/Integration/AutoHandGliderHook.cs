using Autohand;
using Makeshift.Movement;
using UnityEngine;

namespace Makeshift.Integration
{
    /// <summary>
    /// Umbrella controls with Auto Hand: squeeze the trigger while holding it to open or close the
    /// canopy; while held, the glider acts on the player's body. Letting go closes it.
    /// </summary>
    [RequireComponent(typeof(Glider), typeof(Grabbable))]
    public class AutoHandGliderHook : MonoBehaviour
    {
        private Glider glider;
        private Grabbable grabbable;
        private Rigidbody ownBody;

        private void Awake()
        {
            glider = GetComponent<Glider>();
            grabbable = GetComponent<Grabbable>();
            ownBody = GetComponent<Rigidbody>();
            grabbable.OnGrabEvent += OnGrab;
            grabbable.OnReleaseEvent += OnRelease;
            grabbable.OnSqueezeEvent += OnSqueeze;
        }

        private Transform visual;
        private AutoHandPlayer holderPlayer;
        private Hand holder;
        private bool floating;

        private void Start() { visual = transform.Find("Visual"); }

        /// <summary>
        /// While open and in the air the umbrella stands upright above the hand, however the wrist is turned. Only what
        /// is drawn is turned: forcing the held body would fight the hand. Ends on closing, letting go or landing.
        /// </summary>
        private void LateUpdate()
        {
            if (visual == null) return;
            bool held = holder != null && grabbable.GetHeldBy().Contains(holder);
            floating = held && glider.IsOpen && holderPlayer != null && !holderPlayer.IsGrounded();
            if (!floating)
            {
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                return;
            }
            // Turn about the point of the shaft that is in the hand.
            var grip = new Vector3(0f, Mathf.Clamp(transform.InverseTransformPoint(holder.transform.position).y, -0.3f, 0.45f), 0f);
            Vector3 facing = Vector3.ProjectOnPlane(holderPlayer.headCamera.transform.forward, Vector3.up);
            var upright = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : Quaternion.identity;
            visual.SetPositionAndRotation(transform.TransformPoint(grip) - upright * grip, upright);
        }

        private void OnDestroy()
        {
            if (grabbable == null) return;
            grabbable.OnGrabEvent -= OnGrab;
            grabbable.OnReleaseEvent -= OnRelease;
            grabbable.OnSqueezeEvent -= OnSqueeze;
        }

        private void OnGrab(Hand hand, Grabbable g)
        {
            var player = hand != null ? hand.GetComponentInParent<AutoHandPlayer>() : null;
            if (player == null) player = FindAnyObjectByType<AutoHandPlayer>();
            if (player != null) glider.Target = player.body;
            holder = hand;
            holderPlayer = player;
        }

        private void OnRelease(Hand hand, Grabbable g)
        {
            if (grabbable.GetHeldBy().Count > 0) return;
            glider.SetOpen(false);
            glider.Target = ownBody;
        }

        private void OnSqueeze(Hand hand, Grabbable g) => glider.Toggle();
    }
}
