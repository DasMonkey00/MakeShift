using System.Collections.Generic;
using Makeshift.Core;
using Makeshift.Crafting;
using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>
    /// Put on anything that can be swung or thrown. Damage comes from how fast the part that touched was moving and
    /// how heavy the object is, so a thrown toaster counts and a gentle tap doesn't. Sharp and Heavy traits add to it.
    /// No weapon types: any object works, including whatever has been glued onto it.
    ///
    /// Hits are found two ways. Physics collisions catch drops and throws. A swung weapon is also swept: every solid
    /// part (a glued-on head included) checks the space it moved through, because a hand-held object reports weak or
    /// no collision impulses and a fast swing can pass a thin target between physics steps.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactDamage : MonoBehaviour
    {
        [Tooltip("A part has to move at least this fast (m/s) to hurt.")]
        [SerializeField] private float minSwingSpeed = 1.4f;
        [Tooltip("Collisions at least this hard (impulse) hurt even if the speed reading is low.")]
        [SerializeField] private float minImpulse = 1.5f;
        [SerializeField] private float damagePerImpulse = 1.5f;
        [SerializeField] private float sharpMultiplier = 1.5f;
        [SerializeField] private float heavyMultiplier = 1.25f;
        [SerializeField] private float perTargetCooldown = 0.45f;

        private const float TeleportSpeed = 25f; // faster than any swing: the player was teleported

        private class Part
        {
            public Collider collider;
            public Vector3 local;   // sample point in the collider's own space
            public float radius;
            public Vector3 last;
            public Vector3 velocity;
        }

        private readonly Dictionary<Component, float> lastHit = new Dictionary<Component, float>();
        private readonly List<Part> parts = new List<Part>();
        private static readonly Collider[] Near = new Collider[16];
        private Rigidbody body;
        private int partsFor = -1;

        private void Awake() { body = GetComponent<Rigidbody>(); }

        private void FixedUpdate()
        {
            if (body == null || body.isKinematic) { partsFor = -1; return; }
            if (body.IsSleeping()) return;

            // Rebuilt when something is glued on or falls off.
            int signature = transform.childCount;
            if (signature != partsFor) { BuildParts(); partsFor = signature; }

            float dt = Time.fixedDeltaTime;
            foreach (var part in parts)
            {
                if (part.collider == null || !part.collider.enabled) continue;
                Vector3 now = part.collider.transform.TransformPoint(part.local);
                Vector3 from = part.last;
                part.last = now;
                part.velocity = (now - from) / dt;
                float speed = part.velocity.magnitude;
                if (speed < minSwingSpeed || speed > TeleportSpeed) continue;

                int count = Physics.OverlapCapsuleNonAlloc(from, now, part.radius, Near, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var other = Near[i];
                    if (other.attachedRigidbody == body || HandCheck.PlayerBody(other.attachedRigidbody)) continue;
                    Strike(other, speed, 0f, other.ClosestPointOnBounds(now), part.velocity);
                }
            }
        }

        private void BuildParts()
        {
            parts.Clear();
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || col.attachedRigidbody != body) continue;
                if (col is CapsuleCollider capsule)
                {
                    // A stick hits with its ends as much as its middle.
                    Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    float reach = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
                    float r = Radius(col, capsule.radius);
                    Add(col, capsule.center - axis * reach, r);
                    Add(col, capsule.center, r);
                    Add(col, capsule.center + axis * reach, r);
                }
                else if (col is SphereCollider sphere)
                {
                    Add(col, sphere.center, Radius(col, sphere.radius));
                }
                else if (col is BoxCollider box)
                {
                    Vector3 s = box.size;
                    float thin = Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.5f;
                    float wide = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f;
                    Add(col, box.center, Radius(col, (thin + wide) * 0.5f));
                }
                else
                {
                    Add(col, col.transform.InverseTransformPoint(col.bounds.center), Mathf.Max(0.03f, col.bounds.extents.magnitude * 0.6f));
                }
            }
        }

        private static float Radius(Collider col, float local)
        {
            Vector3 s = col.transform.lossyScale;
            return Mathf.Max(0.025f, local * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))));
        }

        private void Add(Collider col, Vector3 local, float radius)
        {
            parts.Add(new Part { collider = col, local = local, radius = radius, last = col.transform.TransformPoint(local) });
        }

        private void OnCollisionEnter(Collision collision)
        {
            var other = collision.collider;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : other.transform.position;

            float speed = 0f;
            Vector3 direction = other.transform.position - transform.position;
            foreach (var part in parts)
            {
                if (collision.contactCount == 0 || part.collider != collision.GetContact(0).thisCollider) continue;
                float s = part.velocity.magnitude;
                if (s > speed && s < TeleportSpeed) { speed = s; direction = part.velocity; }
            }
            Strike(other, speed, collision.impulse.magnitude, point, direction);
        }

        /// <summary>One blow against whatever owns <paramref name="other"/>, if it can be hurt and the blow is hard enough.</summary>
        private void Strike(Collider other, float speed, float impulse, Vector3 point, Vector3 direction)
        {
            var damageable = other.GetComponentInParent<Damageable>();
            var harvestable = other.GetComponentInParent<Harvestable>();
            var breakable = other.GetComponentInParent<Breakable>();
            if (breakable != null && breakable.transform.IsChildOf(transform)) breakable = null;
            if (damageable != null && damageable.GetComponentInParent<Makeshift.Survival.Survivor>() != null) return; // never the player
            if (damageable == null && harvestable == null && breakable == null) return;

            bool fastEnough = speed >= minSwingSpeed;
            if (!fastEnough && impulse < minImpulse) return;

            Component id = damageable != null ? damageable : harvestable != null ? (Component)harvestable : breakable;
            if (lastHit.TryGetValue(id, out float t) && Time.time - t < perTargetCooldown) return;
            lastHit[id] = Time.time;

            // A light weapon swung fast still counts for at least half a kilo.
            float strength = Mathf.Max(impulse, fastEnough ? speed * Mathf.Clamp(body.mass, 0.5f, 3f) : 0f);

            Trait traits = TraitHolder.TryGet(this, out var holder) ? holder.Traits : Trait.None;
            float damage = strength * damagePerImpulse;
            if ((traits & Trait.Sharp) != 0) damage *= sharpMultiplier;
            if ((traits & Trait.Heavy) != 0) damage *= heavyMultiplier;

            Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Bonk, point, Mathf.Clamp01(strength * 0.2f));
            if (damageable != null) damageable.Hit(damage, point, direction);
            if (harvestable != null) harvestable.Strike(damage, traits, point);
            // Cracking stones is still the job of a stone breaker (the club); a glued weapon only wears itself on them.
            var bond = GetComponent<GlueBond>();
            if (bond != null) bond.RegisterHit();
        }
    }
}
