using System.Collections.Generic;
using Autohand;
using Makeshift.Audio;
using Makeshift.Core;
using UnityEngine;

namespace Makeshift.Crafting
{
    /// <summary>
    /// What holds a glued weapon together. Glue alone is weak: the head flies off after 2 or 3 hits and can be glued
    /// on again. Wrapping a vine round the joint (<see cref="StickyBinder"/>) calls <see cref="Lash"/>, and a lashed
    /// weapon never breaks. Lives on the keeper (the stick).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GlueBond : MonoBehaviour
    {
        public static readonly List<GlueBond> All = new List<GlueBond>();

        private const int MinHits = 2, MaxHits = 3;

        private Rigidbody body;
        private Transform part;          // the glued-on piece, now a child of this object
        private Transform spare;         // an untouched, switched-off copy of it, brought back if the glue gives way
        private Transform shelf;         // inactive parent of the spare, so the copy never wakes up while stored
        private Vector3 jointLocal;
        private float massBefore;
        private Trait addedTraits;
        private bool breaking;

        public bool Lashed { get; private set; }
        public int Hits { get; private set; }
        public int HitsNeeded { get; private set; }

        /// <summary>Where the two pieces meet, in the world.</summary>
        public Vector3 JointWorld => transform.TransformPoint(jointLocal);
        public Vector3 JointLocal => jointLocal;
        public Transform Part => part;

        /// <summary>Glues <paramref name="other"/> onto <paramref name="keep"/>. Call from Update or FixedUpdate code.</summary>
        public static GlueBond Join(Rigidbody keep, Rigidbody other, Vector3 jointWorld)
        {
            var bond = keep.gameObject.AddComponent<GlueBond>();
            bond.jointLocal = keep.transform.InverseTransformPoint(jointWorld);
            bond.massBefore = keep.mass;
            bond.HitsNeeded = Random.Range(MinHits, MaxHits + 1);

            // Copy the piece while it is still whole: merging strips its body, grab and damage scripts.
            var shelfGo = new GameObject("GlueSpare");
            shelfGo.SetActive(false);
            bond.shelf = shelfGo.transform;
            bond.spare = Instantiate(other.gameObject, bond.shelf).transform;
            bond.spare.name = other.name;
            // A piece that was itself glued together comes back as it is, without a half-remembered bond.
            foreach (var old in bond.spare.GetComponents<GlueBond>()) DestroyImmediate(old);

            var before = keep.TryGetComponent(out TraitHolder holder) ? holder.Traits : Trait.None;
            bond.part = other.transform;
            RigidMerge.Absorb(keep, other);
            bond.addedTraits = holder != null ? holder.Traits & ~before : Trait.None;

            // Whatever got glued on, the result is a weapon.
            if (keep.GetComponent<Combat.ImpactDamage>() == null) keep.gameObject.AddComponent<Combat.ImpactDamage>();
            return bond;
        }

        private void Awake() { body = GetComponent<Rigidbody>(); }
        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        private void OnDestroy()
        {
            if (shelf != null) Destroy(shelf.gameObject);
        }

        /// <summary>The vine is on: this joint is permanent.</summary>
        public void Lash()
        {
            Lashed = true;
            breaking = false;
            if (shelf != null) Destroy(shelf.gameObject);
        }

        /// <summary>The weapon struck something. Enough of these and unlashed glue gives way.</summary>
        public void RegisterHit()
        {
            if (Lashed || breaking) return;
            Hits++;
            if (Hits >= HitsNeeded) breaking = true;
            else PlaceholderSfx.Play(SfxKind.Crack, JointWorld, 0.6f);
        }

        private void LateUpdate()
        {
            if (part == null) return;
            // Auto Hand moves its own colliders between layers when grabbing and releasing, but does not know about
            // the glued-on ones, so they follow the keeper's layer here.
            int layer = gameObject.layer;
            foreach (var col in part.GetComponentsInChildren<Collider>())
                if (!col.isTrigger) col.gameObject.layer = layer;
        }

        private void FixedUpdate()
        {
            // Not done inside the collision callback: bodies cannot be taken apart there.
            if (breaking) Break();
        }

        private void Break()
        {
            breaking = false;
            Vector3 at = part != null ? part.position : JointWorld;
            Quaternion rot = part != null ? part.rotation : transform.rotation;
            Vector3 velocity = body.GetPointVelocity(at);

            if (part != null)
            {
                foreach (var col in part.GetComponentsInChildren<Collider>()) col.enabled = false;
                part.gameObject.SetActive(false);
                Destroy(part.gameObject);
                part = null;
            }

            if (spare != null)
            {
                spare.SetParent(null, false);
                spare.SetPositionAndRotation(at, rot);
                spare.gameObject.SetActive(true);
                int grabbable = LayerMask.NameToLayer("Grabbable");
                if (grabbable >= 0 && spare.GetComponent<Grabbable>() != null)
                    foreach (var col in spare.GetComponentsInChildren<Collider>())
                        if (!col.isTrigger) col.gameObject.layer = grabbable;
                if (spare.TryGetComponent(out Rigidbody rb))
                {
                    rb.position = at;
                    rb.rotation = rot;
                    rb.linearVelocity = Vector3.ClampMagnitude(velocity, 3f) + Vector3.up * 1.2f;
                }
                spare = null;
            }

            body.mass = massBefore;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            if (addedTraits != Trait.None && TryGetComponent(out TraitHolder holder)) holder.Remove(addedTraits);
            if (TryGetComponent(out Glue glue)) glue.ComeUnstuck();

            PlaceholderSfx.Play(SfxKind.Break, at, 1f);
            All.Remove(this);
            Destroy(this);
        }
    }
}
