using Makeshift.Audio;
using Makeshift.Core;
using Makeshift.Crafting;
using Makeshift.Nature;
using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>
    /// Marks the flat sharp rock. Glued onto a stick it makes an axe, the only thing that cuts a
    /// <see cref="ChoppableTree"/>. A loose rock does nothing.
    ///
    /// A tree is cut only at waist height. Only a deliberate swing counts: the axe is in a hand, the arm itself is moving it (walking or turning with an
    /// axe moves nothing against the head set), it has travelled a real swing and it is heading into the trunk. It
    /// cuts only blade first: an edge of the slab leads, not its flat side or the back of the head.
    ///
    /// Swings are found by following the head itself, because the physics hand stops on the bark before a collision
    /// reports any real speed. Only a lashed axe cuts: one held together by glue alone falls apart on the first swing
    /// into a tree.
    /// </summary>
    public class AxeHead : MonoBehaviour
    {
        [Tooltip("Speed (m/s) the arm must give the head for a swing to bite.")]
        [SerializeField] private float minArmSpeed = 1.8f;
        [Tooltip("Metres the head must travel against the body in the last moment: a swing, not a twitch.")]
        [SerializeField] private float minSwingTravel = 0.3f;
        [Tooltip("How close the middle of the head must come to the bark.")]
        [SerializeField] private float reach = 0.16f;
        [SerializeField] private float cooldown = 0.5f;
        [Tooltip("How far, in degrees, the swing may be off the slab's own plane and still count as edge first.")]
        [SerializeField] private float edgeTolerance = 55f;

        /// <summary>The head must pull this much further out than <see cref="reach"/> before it can bite again.</summary>
        private const float Rearm = 0.09f;
        private const float TeleportSpeed = 25f;
        private const float PrepareMargin = 0.6f;
        private const float SpeedSpan = 0.05f;  // seconds the arm speed is measured over
        private const float SwingSpan = 0.35f;  // seconds the swing's travel is measured over
        private const float PeakHold = 0.12f;   // contact has already slowed the head, so the best speed is kept this long
        private const float IntoTrunk = 0.25f;  // share of the swing that must point at the trunk
        private const float SideOfHandle = 0.025f; // metres off the handle at which the head has a front and a back

        private struct Sample
        {
            public float time;
            public Vector3 at; // from the head set, in the rig's frame, so moving and turning the player cancel out
        }

        private readonly Sample[] trail = new Sample[48];
        private int count, newest;

        private Collider head;
        private Camera view;
        private CapsuleCollider handle;
        private Vector3 last;
        private Vector3 peakSwing; // arm velocity at its best, rig frame
        private float peakTravel, peakTime, nextHit;
        private bool armed = true;
        private ChoppableTree biting;

        private void FixedUpdate()
        {
            var bond = GetComponentInParent<GlueBond>();
            if (bond == null || bond.Part != transform || !HandCheck.Held(bond.GetComponent<Rigidbody>()))
            {
                count = 0; // not an axe, or not in a hand: nothing it does is a swing
                return;
            }
            if (view == null) view = Camera.main;
            if (view == null) return;
            if (head == null) head = GetComponentInChildren<Collider>();
            Vector3 now = head != null ? head.bounds.center : transform.position;
            Quaternion rig = view.transform.parent != null ? view.transform.parent.rotation : Quaternion.identity;
            var sample = new Sample { time = Time.time, at = Quaternion.Inverse(rig) * (now - view.transform.position) };

            Vector3 from = count > 0 ? last : now;
            last = now;
            if (count > 0 && Vector3.Distance(trail[newest].at, sample.at) > TeleportSpeed * Time.fixedDeltaTime) count = 0;
            newest = (newest + 1) % trail.Length;
            trail[newest] = sample;
            count = Mathf.Min(count + 1, trail.Length);

            Measure(sample);

            if (!armed && (biting == null || !biting.Near(now, reach + Rearm))) armed = true;
            if (!armed || peakSwing.magnitude < minArmSpeed || peakTravel < minSwingTravel || Time.time < nextHit) return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, now) / 0.03f), 1, 12);
            // A swing near any pine readies that tree. The margin covers a thick trunk round a thin collider.
            if (TreeCutting.Instance != null) TreeCutting.Instance.PrepareNear(from, now, reach + PrepareMargin);
            Vector3 swing = rig * peakSwing.normalized;
            foreach (var tree in ChoppableTree.All)
            {
                if (tree == null || tree.Felled) continue;
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(from, now, i / (float)steps);
                    if (!tree.Near(p, reach)) continue;
                    Vector3 toTrunk = tree.Toward(p);
                    if (Vector3.Dot(swing, toTrunk) < IntoTrunk) break; // brushing past, or drawing back
                    armed = false;
                    biting = tree;
                    nextHit = Time.time + cooldown;
                    peakSwing = Vector3.zero;
                    if (!bond.Lashed) bond.Shatter();
                    else if (tree.AtCutHeight(p) && BladeFirst(bond, now, swing, toTrunk)) tree.Chop(p);
                    // Too high or low, flat, or back of the head: it only bounces, silently.
                    return;
                }
            }
        }

        // The arm's speed now, and how far this swing has carried the head. The best of the last moment is kept.
        private void Measure(Sample now)
        {
            Vector3 velocity = Vector3.zero;
            bool measured = false;
            float travel = 0f;
            for (int back = 1; back < count; back++)
            {
                var old = trail[(newest - back + trail.Length) % trail.Length];
                float age = now.time - old.time;
                if (age > SwingSpan) break;
                travel = Mathf.Max(travel, Vector3.Distance(now.at, old.at));
                if (!measured && age >= SpeedSpan)
                {
                    velocity = (now.at - old.at) / age;
                    measured = true;
                }
            }
            if (velocity.magnitude < peakSwing.magnitude && Time.time - peakTime <= PeakHold) return;
            peakSwing = velocity;
            peakTravel = travel;
            peakTime = Time.time;
        }

        /// <summary>
        /// True when a cutting edge meets the trunk: the swing and the trunk both lie near the slab's plane, and the
        /// head does not arrive back first.
        /// </summary>
        private bool BladeFirst(GlueBond bond, Vector3 headAt, Vector3 swing, Vector3 toTrunk)
        {
            Vector3 flat = transform.up; // the slab is thin along its own up axis
            float most = Mathf.Sin(edgeTolerance * Mathf.Deg2Rad);
            if (Mathf.Abs(Vector3.Dot(swing, flat)) > most || Mathf.Abs(Vector3.Dot(toTrunk, flat)) > most + 0.1f)
                return false;

            // Where the head sticks out from the handle is its front. A head on the end of the stick, or flat against
            // its side, has no back: any edge will do.
            if (handle == null || handle.attachedRigidbody != bond.GetComponent<Rigidbody>()) handle = Handle(bond);
            if (handle == null) return true;
            var stick = handle.transform;
            Vector3 along = stick.TransformDirection(handle.direction == 0 ? Vector3.right
                : handle.direction == 1 ? Vector3.up : Vector3.forward);
            Vector3 out_ = Vector3.ProjectOnPlane(headAt - stick.TransformPoint(handle.center), along);
            Vector3 blade = Vector3.ProjectOnPlane(out_, flat);
            if (blade.magnitude < SideOfHandle || blade.magnitude < out_.magnitude * 0.5f) return true;
            return Vector3.Dot(swing, blade.normalized) > -0.1f;
        }

        // The stick's own capsule: the first one on the keeper that is not part of this head.
        private CapsuleCollider Handle(GlueBond bond)
        {
            foreach (var capsule in bond.GetComponentsInChildren<CapsuleCollider>())
                if (!capsule.isTrigger && !capsule.transform.IsChildOf(transform)) return capsule;
            return null;
        }
    }
}
