using System;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Machines
{
    /// <summary>
    /// A spot on a machine frame where a part goes. Any part fits, but the wrong part
    /// lowers reliability. Hold a part still inside the socket's trigger and it snaps in.
    /// Needs a trigger Collider on this object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PartSocket : MonoBehaviour
    {
        /// <summary>
        /// Optional hook for the hand system: return true while a body is held.
        /// When set, parts snap only after being let go inside the socket.
        /// </summary>
        public static Func<Rigidbody, bool> IsHeld;

        [Tooltip("Part ids that fit properly. Anything else still fits, badly.")]
        [SerializeField] private string[] idealPartIds = Array.Empty<string>();
        [SerializeField, Range(0f, 1f)] private float wrongPartMultiplier = 0.7f;
        [SerializeField] private bool required = true;
        [Tooltip("How long a part must hover in the socket before it snaps in.")]
        [SerializeField] private float snapDelay = 0.3f;
        [SerializeField] private float ejectImpulse = 2f;

        [Header("Events")]
        public UnityEvent<MachinePart> onPartInserted;
        public UnityEvent<MachinePart> onPartRemoved;

        public MachinePart Part { get; private set; }
        public bool IsFilled => Part != null;
        public bool Required => required;

        private MachinePart candidate;
        private float candidateTime;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        /// <summary>The part's reliability after the wrong-part penalty.</summary>
        public float EffectiveReliability
        {
            get
            {
                if (Part == null) return 0f;
                return Part.Reliability * (IsIdeal(Part) ? 1f : wrongPartMultiplier);
            }
        }

        public bool IsIdeal(MachinePart p)
        {
            if (idealPartIds == null || idealPartIds.Length == 0) return true;
            return Array.IndexOf(idealPartIds, p.PartId) >= 0;
        }

        private void OnTriggerStay(Collider other)
        {
            if (IsFilled) return;
            var part = other.GetComponentInParent<MachinePart>();
            if (part == null || part.IsSocketed) return;

            if (part != candidate)
            {
                candidate = part;
                candidateTime = 0f;
                return;
            }

            bool held = IsHeld != null && IsHeld(part.Body);
            if (held) { candidateTime = 0f; return; }

            candidateTime += Time.fixedDeltaTime;
            if (candidateTime >= snapDelay) Insert(part);
        }

        private void OnTriggerExit(Collider other)
        {
            var part = other.GetComponentInParent<MachinePart>();
            if (part == candidate) candidate = null;
        }

        public void Insert(MachinePart part)
        {
            if (IsFilled || part == null) return;
            Part = part;
            part.Socket = this;
            part.Body.isKinematic = true;
            part.SetSeated(true); // seated parts are locked: no grab, no pulling out, no glitchy tug-of-war
            part.transform.SetParent(transform, true);
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;
            candidate = null;
            onPartInserted?.Invoke(part);
        }

        /// <summary>Pops the part out. Backfires call this; so can a player's yank.</summary>
        public MachinePart Eject()
        {
            if (!IsFilled) return null;
            var part = Part;
            Part = null;
            part.Socket = null;
            part.transform.SetParent(null, true);
            part.SetSeated(false);
            part.Body.isKinematic = false;
            part.Body.AddForce((transform.up + UnityEngine.Random.insideUnitSphere * 0.5f) * ejectImpulse,
                ForceMode.Impulse);
            onPartRemoved?.Invoke(part);
            return part;
        }

        /// <summary>
        /// A hand took the part out. Only forgets it: the hand system already owns the part's
        /// parent and rigidbody by now, so nothing is unparented or pushed.
        /// </summary>
        public MachinePart Detach()
        {
            if (!IsFilled) return null;
            var part = Part;
            Part = null;
            part.Socket = null;
            candidate = null;
            onPartRemoved?.Invoke(part);
            return part;
        }
    }
}
